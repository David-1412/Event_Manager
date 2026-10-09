using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Imports;

namespace SportMeet.Infrastructure.Imports;

public sealed class GoogleGeocodingOptions
{
    public const string SectionName = "Google";

    /// <summary>A server-side key restricted to the Geocoding API. Deliberately not the
    /// browser Maps key: that one ships to every visitor. Empty disables geocoding, and
    /// the form falls back to the venue picker.</summary>
    public string GeocodingApiKey { get; init; } = "";
}

/// <summary>
/// Google Geocoding, address first.
///
/// An address is the reliable query (every one of 30 real venues resolved), so it is the
/// primary. When a venue name is also present it runs as a second query in parallel, purely
/// as a cross-check: if the two land more than 300 m apart the pin is flagged for
/// confirmation. A venue name alone is allowed but always flagged, because it matched the
/// address result within 300 m only 17 times in 30.
///
/// Restricted to Australia: this is a Melbourne product, and an unscoped query for
/// "Park St" resolves to the wrong continent often enough to matter.
/// </summary>
public sealed class GoogleGeocoder(
    IHttpClientFactory http,
    IOptions<GoogleGeocodingOptions> options,
    ILogger<GoogleGeocoder> log) : IGeocoder
{
    public const string ClientName = "google-geocoding";

    private sealed record Hit(double Lat, double Lon, string LocationType, bool Partial);

    public async Task<GeocodeResult?> GeocodeAsync(
        string? venueName, string? address, CancellationToken ct = default)
    {
        var key = options.Value.GeocodingApiKey;
        var hasAddress = !string.IsNullOrWhiteSpace(address);
        var hasVenue = !string.IsNullOrWhiteSpace(venueName);
        if (string.IsNullOrEmpty(key) || (!hasAddress && !hasVenue)) return null;

        try
        {
            var primaryTask = QueryAsync(hasAddress ? address! : venueName!, key, ct);
            var crossCheck = hasAddress && hasVenue ? VenueQuery(venueName!, address!) : null;
            var secondTask = crossCheck is null ? Task.FromResult<Hit?>(null) : QueryAsync(crossCheck, key, ct);
            await Task.WhenAll(primaryTask, secondTask);

            var primary = primaryTask.Result;
            if (primary is null) return null;

            double? apart = secondTask.Result is { } second
                ? GeocodePolicy.DistanceMetres(primary.Lat, primary.Lon, second.Lat, second.Lon)
                : null;

            return new GeocodeResult(
                primary.Lat, primary.Lon, primary.LocationType,
                GeocodePolicy.NeedsConfirm(primary.LocationType, primary.Partial, venueOnly: !hasAddress, apart));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The message is logged, the exception object is not: an HttpRequestException's
            // text can carry the request URL, and the URL carries the key.
            log.LogWarning("Geocoding failed ({Type}); the form falls back to the venue picker.", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>"Seddon Park, 42 Railway Ave, Seddon" gives "Seddon Park, Seddon": the
    /// venue plus whatever follows the street, which is what disambiguates the name.</summary>
    private static string? VenueQuery(string venue, string address)
    {
        var comma = address.IndexOf(',');
        return comma > 0 && comma < address.Length - 1
            ? $"{venue.Trim()}, {address[(comma + 1)..].Trim()}"
            : null;
    }

    private async Task<Hit?> QueryAsync(string query, string key, CancellationToken ct)
    {
        var url = "https://maps.googleapis.com/maps/api/geocode/json"
                  + $"?address={Uri.EscapeDataString(query)}&region=au&components=country:AU&key={Uri.EscapeDataString(key)}";
        using var response = await http.CreateClient(ClientName).GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString();
        if (status != "OK")
        {
            // ZERO_RESULTS is an ordinary answer. Anything else (REQUEST_DENIED, an API that is
            // not enabled, OVER_QUERY_LIMIT) is a configuration problem someone has to see.
            if (status != "ZERO_RESULTS") log.LogWarning("Geocoding returned {Status}.", status);
            return null;
        }
        var first = root.GetProperty("results")[0];

        // Asked for something it cannot find, Google answers with the whole country (or
        // state) rather than nothing, and the pin lands in the middle of the outback. A
        // result that coarse is not a venue, so it is no result.
        if (first.TryGetProperty("types", out var types)
            && types.EnumerateArray().Any(t => t.GetString() is "country" or "administrative_area_level_1"))
            return null;

        var geometry = first.GetProperty("geometry");
        var location = geometry.GetProperty("location");
        return new Hit(
            location.GetProperty("lat").GetDouble(),
            location.GetProperty("lng").GetDouble(),
            geometry.GetProperty("location_type").GetString() ?? "APPROXIMATE",
            first.TryGetProperty("partial_match", out var partial) && partial.GetBoolean());
    }
}
