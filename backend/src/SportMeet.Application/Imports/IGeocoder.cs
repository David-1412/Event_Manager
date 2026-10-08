namespace SportMeet.Application.Imports;

/// <summary>
/// Turns what the extractor read into a map point. Returns null when nothing could be
/// placed, which is an ordinary outcome: the form falls back to the venue picker.
/// </summary>
public interface IGeocoder
{
    Task<GeocodeResult?> GeocodeAsync(string? venueName, string? address, CancellationToken ct = default);
}

/// <param name="LocationType">The provider's precision grade (ROOFTOP is a building).</param>
/// <param name="NeedsConfirm">True when the pin should be eyeballed before it is trusted.</param>
public sealed record GeocodeResult(double Latitude, double Longitude, string LocationType, bool NeedsConfirm);
