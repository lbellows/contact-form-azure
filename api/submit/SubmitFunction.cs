using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ContactFormApi;

public class SubmitFunction
{
    private const int WindowMinutes = 10;
    private const int MaxPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(WindowMinutes);
    private static readonly Dictionary<string, List<DateTime>> IpBuckets = new();
    private static readonly object BucketLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Function("submit")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "submit")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger("SubmitFunction");

        var parseResult = await ParseBodyAsync(req);
        if (parseResult.Error != null)
        {
            return CreateJson(req, HttpStatusCode.BadRequest, new
            {
                ok = false,
                error = "validation_error",
                details = new[] { "invalid_json" }
            });
        }

        var payload = parseResult.Payload ?? new SubmitPayload();
        var validationErrors = ValidatePayload(payload, out var cleaned);
        if (validationErrors.Count > 0)
        {
            return CreateJson(req, HttpStatusCode.BadRequest, new
            {
                ok = false,
                error = "validation_error",
                details = validationErrors
            });
        }

        var allowedSites = GetAllowedSites();
        if (string.IsNullOrWhiteSpace(cleaned.Site) || !allowedSites.Contains(cleaned.Site))
        {
            return CreateJson(req, HttpStatusCode.Forbidden, new { ok = false, error = "forbidden_site" });
        }

        var ip = GetClientIp(req);
        if (IsRateLimited(ip))
        {
            return CreateJson(req, (HttpStatusCode)429, new { ok = false, error = "rate_limited" });
        }

        var connectionString = Environment.GetEnvironmentVariable("ACS_EMAIL_CONNECTION_STRING");
        var fromEmail = Environment.GetEnvironmentVariable("ACS_FROM_EMAIL");
        var toEmail = Environment.GetEnvironmentVariable("TO_EMAIL");

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail))
        {
            logger.LogWarning("Missing ACS configuration.");
            return CreateJson(req, HttpStatusCode.InternalServerError, new { ok = false, error = "server_error" });
        }

        var subjectLine = $"[ContactForm][{cleaned.Site}] {(!string.IsNullOrWhiteSpace(cleaned.Subject) ? cleaned.Subject : "(no subject)")}";
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var userAgent = GetHeader(req, "user-agent") ?? "unknown";

        var bodyText = string.Join("\n", new[]
        {
            $"Name: {cleaned.Name}",
            $"Email: {cleaned.Email}",
            $"Subject: {(string.IsNullOrWhiteSpace(cleaned.Subject) ? "(no subject)" : cleaned.Subject)}",
            $"Message: {cleaned.Message}",
            $"Site: {cleaned.Site}",
            $"Timestamp: {timestamp}",
            $"IP: {ip}",
            $"User-Agent: {userAgent}"
        });

        var bodyHtml = $@"
<p><strong>Name:</strong> {System.Net.WebUtility.HtmlEncode(cleaned.Name)}</p>
<p><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(cleaned.Email)}</p>
<p><strong>Subject:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(cleaned.Subject) ? "(no subject)" : cleaned.Subject)}</p>
<p><strong>Message:</strong><br/>{System.Net.WebUtility.HtmlEncode(cleaned.Message).Replace("\n", "<br/>")}</p>
<p><strong>Site:</strong> {System.Net.WebUtility.HtmlEncode(cleaned.Site)}</p>
<p><strong>Timestamp:</strong> {System.Net.WebUtility.HtmlEncode(timestamp)}</p>
<p><strong>IP:</strong> {System.Net.WebUtility.HtmlEncode(ip)}</p>
<p><strong>User-Agent:</strong> {System.Net.WebUtility.HtmlEncode(userAgent)}</p>
";

        try
        {
            var client = new EmailClient(connectionString);
            var message = new EmailMessage(
                fromEmail,
                new EmailRecipients(new List<EmailAddress> { new(toEmail) }),
                new EmailContent(subjectLine)
                {
                    PlainText = bodyText,
                    Html = bodyHtml
                });

            var sendOperation = await client.SendAsync(WaitUntil.Completed, message);
            var result = sendOperation.Value;
            logger.LogInformation("Email sent. Status: {Status}, Site: {Site}, Ip: {Ip}, Ua: {Ua}, MessageLength: {MessageLength}",
                result?.Status,
                cleaned.Site,
                ip,
                userAgent,
                cleaned.Message.Length);

            return CreateJson(req, HttpStatusCode.OK, new { ok = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email send failed. Site: {Site}, Ip: {Ip}", cleaned.Site, ip);
            return CreateJson(req, HttpStatusCode.InternalServerError, new { ok = false, error = "email_send_failed" });
        }
    }

    private static async Task<ParseResult> ParseBodyAsync(HttpRequestData req)
    {
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ParseResult { Payload = new SubmitPayload() };
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SubmitPayload>(body, JsonOptions);
            return new ParseResult { Payload = payload ?? new SubmitPayload() };
        }
        catch (JsonException)
        {
            return new ParseResult { Error = "invalid_json" };
        }
    }

    private static List<string> ValidatePayload(SubmitPayload payload, out SubmitPayload cleaned)
    {
        var errors = new List<string>();

        var name = (payload.Name ?? string.Empty).Trim();
        var email = (payload.Email ?? string.Empty).Trim();
        var subject = (payload.Subject ?? string.Empty).Trim();
        var message = (payload.Message ?? string.Empty).Trim();
        var site = (payload.Site ?? string.Empty).Trim();
        var company = (payload.Company ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name)) errors.Add("name_required");
        if (string.IsNullOrWhiteSpace(email)) errors.Add("email_required");
        if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email)) errors.Add("email_invalid");
        if (string.IsNullOrWhiteSpace(message)) errors.Add("message_required");

        if (name.Length > 100) errors.Add("name_too_long");
        if (email.Length > 254) errors.Add("email_too_long");
        if (subject.Length > 150) errors.Add("subject_too_long");
        if (message.Length > 4000) errors.Add("message_too_long");
        if (site.Length > 50) errors.Add("site_too_long");
        if (!string.IsNullOrWhiteSpace(company)) errors.Add("honeypot_triggered");

        cleaned = new SubmitPayload
        {
            Name = name,
            Email = email,
            Subject = subject,
            Message = message,
            Site = site,
            Company = company
        };

        return errors;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> GetAllowedSites()
    {
        var raw = Environment.GetEnvironmentVariable("ALLOWED_SITES") ?? string.Empty;
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetClientIp(HttpRequestData req)
    {
        var forwarded = GetHeader(req, "x-forwarded-for");
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Trim();
            }
        }

        var clientIp = GetHeader(req, "x-client-ip");
        return !string.IsNullOrWhiteSpace(clientIp) ? clientIp : "unknown";
    }

    private static string? GetHeader(HttpRequestData req, string key)
    {
        if (req.Headers.TryGetValues(key, out var values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    private static bool IsRateLimited(string ip)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - Window;

        lock (BucketLock)
        {
            if (!IpBuckets.TryGetValue(ip, out var list))
            {
                list = new List<DateTime>();
                IpBuckets[ip] = list;
            }

            list.RemoveAll(ts => ts < cutoff);
            if (list.Count >= MaxPerWindow)
            {
                return true;
            }

            list.Add(now);
            return false;
        }
    }

    private static HttpResponseData CreateJson(HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        response.WriteString(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    private sealed class ParseResult
    {
        public SubmitPayload? Payload { get; init; }
        public string? Error { get; init; }
    }

    private sealed class SubmitPayload
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Site { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
    }
}
