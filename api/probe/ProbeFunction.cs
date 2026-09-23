using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ContactFormApi;

// TEMPORARY: measures which client-IP headers SWA forwards. Never merge.
public class ProbeFunction
{
    [Function("probe")]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "probe")] HttpRequestData req)
    {
        var h = req.Headers.Where(x => !x.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => string.Join(" | ", x.Value));
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        res.WriteString(JsonSerializer.Serialize(h));
        return res;
    }
}
