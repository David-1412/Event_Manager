using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SportMeet.Infrastructure.Imports;

namespace SportMeet.Import.Tests;

public class GoogleGeocoderTests
{
    // Shapes copied from real responses.
    private static string Ok(double lat, double lng, string type, bool partial = false, string kind = "street_address")
        => "{\"status\":\"OK\",\"results\":[{\"types\":[\"" + kind + "\"],\"partial_match\":" + (partial ? "true" : "false")
           + ",\"geometry\":{\"location\":{\"lat\":" + lat.ToString(CultureInfo.InvariantCulture)
           + ",\"lng\":" + lng.ToString(CultureInfo.InvariantCulture)
           + "},\"location_type\":\"" + type + "\"}}]}";

    private const string ZeroResults = """{"status":"ZERO_RESULTS","results":[]}""";
    private const string Denied = """{"status":"REQUEST_DENIED","results":[],"error_message":"The provided API key is invalid."}""";

    private sealed class Stub(Func<string, string> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request.RequestUri!.Query), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (GoogleGeocoder geocoder, Stub stub) Make(Func<string, string> respond, string key = "k")
    {
        var stub = new Stub(respond);
        var geocoder = new GoogleGeocoder(
            new Factory(stub),
            Microsoft.Extensions.Options.Options.Create(new GoogleGeocodingOptions { GeocodingApiKey = key }),
            NullLogger<GoogleGeocoder>.Instance);
        return (geocoder, stub);
    }

    [Fact]
    public async Task An_exact_building_comes_back_trusted()
    {
        var (g, _) = Make(_ => Ok(-37.81, 144.89, "ROOFTOP"));
        var r = await g.GeocodeAsync(null, "42 Railway Ave, Seddon");
        Assert.NotNull(r);
        Assert.Equal(-37.81, r.Latitude);
        Assert.Equal(144.89, r.Longitude);
        Assert.Equal("ROOFTOP", r.LocationType);
        Assert.False(r.NeedsConfirm);
    }

    [Fact]
    public async Task A_street_level_result_needs_confirming()
    {
        var (g, _) = Make(_ => Ok(-37.81, 144.89, "GEOMETRIC_CENTER"));
        Assert.True((await g.GeocodeAsync(null, "Ross Gregory Dr, St Kilda"))!.NeedsConfirm);
    }

    [Fact]
    public async Task A_partial_match_needs_confirming()
    {
        var (g, _) = Make(_ => Ok(-37.81, 144.89, "ROOFTOP", partial: true));
        Assert.True((await g.GeocodeAsync(null, "42 Railway Ave"))!.NeedsConfirm);
    }

    [Fact]
    public async Task A_venue_name_alone_is_always_confirmed()
    {
        var (g, _) = Make(_ => Ok(-37.81, 144.89, "ROOFTOP"));
        Assert.True((await g.GeocodeAsync("Seddon Park", null))!.NeedsConfirm);
    }

    [Fact]
    public async Task Address_and_venue_queries_that_agree_are_trusted()
    {
        var (g, stub) = Make(_ => Ok(-37.81, 144.89, "ROOFTOP"));
        var r = await g.GeocodeAsync("Seddon Park", "42 Railway Ave, Seddon");
        Assert.False(r!.NeedsConfirm);
        Assert.Equal(2, stub.Urls.Count);
        Assert.Contains(stub.Urls, u => u.Contains("Seddon%20Park%2C%20Seddon"));
    }

    [Fact]
    public async Task Address_and_venue_queries_that_disagree_are_confirmed()
    {
        // The address resolves in Seddon; the venue-name query lands about 5 km away.
        var (g, _) = Make(query => query.Contains("Seddon%20Park")
            ? Ok(-37.86, 144.89, "ROOFTOP")
            : Ok(-37.81, 144.89, "ROOFTOP"));
        var r = await g.GeocodeAsync("Seddon Park", "42 Railway Ave, Seddon");
        Assert.Equal(-37.81, r!.Latitude);
        Assert.True(r.NeedsConfirm);
    }

    [Theory]
    [InlineData("country")]
    [InlineData("administrative_area_level_1")]
    public async Task A_whole_country_or_state_is_not_a_venue(string kind)
    {
        // What Google returns for an address it cannot find: the middle of Australia.
        var (g, _) = Make(_ => Ok(-25.27, 133.78, "APPROXIMATE", kind: kind));
        Assert.Null(await g.GeocodeAsync("Zzqx Hall", "99 Nowhere Zzq Lane"));
    }

    [Fact]
    public async Task A_suburb_level_result_is_kept_but_confirmed()
    {
        var (g, _) = Make(_ => Ok(-37.86, 144.98, "APPROXIMATE", kind: "locality"));
        var r = await g.GeocodeAsync(null, "St Kilda");
        Assert.NotNull(r);
        Assert.True(r.NeedsConfirm);
    }

    [Fact]
    public async Task No_result_is_null_and_not_an_error()
    {
        var (g, _) = Make(_ => ZeroResults);
        Assert.Null(await g.GeocodeAsync(null, "Nowhere Lane"));
    }

    [Fact]
    public async Task A_rejected_key_degrades_to_null_so_the_form_falls_back_to_the_picker()
    {
        var (g, _) = Make(_ => Denied);
        Assert.Null(await g.GeocodeAsync(null, "42 Railway Ave"));
    }

    [Fact]
    public async Task Garbage_from_the_provider_degrades_to_null()
    {
        var (g, _) = Make(_ => "<html>not json</html>");
        Assert.Null(await g.GeocodeAsync(null, "42 Railway Ave"));
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var (g, stub) = Make(_ => Ok(0, 0, "ROOFTOP"), key: "");
        Assert.Null(await g.GeocodeAsync("Seddon Park", "42 Railway Ave"));
        Assert.Empty(stub.Urls);
    }

    [Fact]
    public async Task With_nothing_to_look_up_nothing_is_sent()
    {
        var (g, stub) = Make(_ => Ok(0, 0, "ROOFTOP"));
        Assert.Null(await g.GeocodeAsync(" ", null));
        Assert.Empty(stub.Urls);
    }

    [Fact]
    public async Task Queries_are_scoped_to_australia()
    {
        var (g, stub) = Make(_ => Ok(-37.81, 144.89, "ROOFTOP"));
        await g.GeocodeAsync(null, "42 Park St");
        Assert.Contains("components=country:AU", Assert.Single(stub.Urls));
    }
}
