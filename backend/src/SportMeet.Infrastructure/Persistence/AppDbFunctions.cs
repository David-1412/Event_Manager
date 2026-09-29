namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Placeholder for the sportsmeet.haversine_km Postgres function. No PostGIS in
/// the MVP (IMPLEMENTATION_PLAN.md §2): a bounding-box predicate plus this
/// function keeps the (lat, lng) index usable, and distance only ever sorts and
/// labels a card - it never gates a booking.
///
/// Takes coordinates rather than a row. EF requires every DbFunction parameter to
/// be a mappable type, and VwEventFeed is a keyless view type that cannot map -
/// asking for it fails at model build with "has an invalid type". Passing
/// (v.Lat, v.Lng) translates to the same SQL and keeps the function usable from
/// both the view and the events table.
///
/// The body exists solely so EF can read the signature. Reaching it means a query
/// fell back to client-side evaluation, so it throws loudly instead of quietly
/// pulling the table into memory.
/// </summary>
public static class AppDbFunctions
{
    public static double HaversineKm(double lat, double lng, double originLat, double originLng)
        => throw new InvalidOperationException(
            nameof(HaversineKm) + " is translatable to SQL only and must never run in memory.");
}
