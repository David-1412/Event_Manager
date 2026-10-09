namespace SportMeet.Application.Imports;

/// <summary>
/// When a geocoded pin is trusted without a look. Kept apart from the HTTP client so the
/// rule can be tested and tuned without a network.
///
/// Measured on 30 real venues: a street address resolved every time, but only 19 of 30
/// came back as an exact building, and a venue-name query disagreed with the address
/// query by more than 300 m in 5. A venue name on its own matched the address result
/// within 300 m just 17 times in 30, so it is never trusted alone.
/// </summary>
public static class GeocodePolicy
{
    public const double DisagreeMetres = 300;
    private const string Rooftop = "ROOFTOP";

    /// <param name="locationType">Provider precision for the address (or venue) query.</param>
    /// <param name="partialMatch">The provider says it matched only part of the query.</param>
    /// <param name="venueOnly">There was no address, so the pin came from a name alone.</param>
    /// <param name="queriesApartMetres">Distance between the address and venue queries
    /// when both ran, else null.</param>
    public static bool NeedsConfirm(
        string locationType, bool partialMatch, bool venueOnly, double? queriesApartMetres)
        => venueOnly
           || partialMatch
           || !string.Equals(locationType, Rooftop, StringComparison.OrdinalIgnoreCase)
           || queriesApartMetres > DisagreeMetres;

    public static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        const double Rad = Math.PI / 180;
        var dLat = (lat2 - lat1) * Rad;
        var dLon = (lon2 - lon1) * Rad;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }
}
