using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ContactFormApi;

/// <summary>Public, non-secret settings the form needs before it renders.</summary>
public class ConfigFunction
{
    [Function("config")]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config")] HttpRequestData req)
    {
        var siteKey = Environment.GetEnvironmentVariable("TURNSTILE_SITE_KEY");
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Cache-Control", "public, max-age=300");
        response.WriteString(JsonSerializer.Serialize(new
        {
            turnstileSiteKey = string.IsNullOrWhiteSpace(siteKey) ? null : siteKey.Trim()
        }));
        return response;
    }
}
