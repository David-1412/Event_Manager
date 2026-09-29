// Healthcheck probe for the API container. Compose runs it as the service
// healthcheck; exit 0 means healthy, anything else is unhealthy.
//
// Only System.Net.Http is used so the build needs nothing beyond the framework
// reference assemblies. A non-2xx (including the 503 /healthz returns when the
// database is unreachable) is a failed check on purpose: the container should
// not be reported healthy when its dependency is down.
using System.Net;

var url = args.Length > 0 ? args[0] : "http://localhost:8080/healthz";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

try
{
    using var response = await http.GetAsync(url);
    var healthy = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
    if (!healthy)
        await Console.Error.WriteLineAsync($"{url} -> {(int)response.StatusCode}");
    return healthy ? 0 : 1;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"{url} -> {ex.GetType().Name}: {ex.Message}");
    return 1;
}
