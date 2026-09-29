using Microsoft.AspNetCore.Mvc;

namespace SportMeet.Api.Controllers;

/// <summary>A venue in the shape frontend/src/components/event/venue-picker.tsx
/// expects. Field names are the contract: VenuePicker maps over results reading
/// venueName/address/latitude/longitude, so renaming one silently yields a list of
/// blank rows rather than an error.</summary>
public sealed record VenueSuggestion(string Id, string VenueName, string Address, double Latitude, double Longitude);

/// <summary>
/// STUB. Not a geocoder. venue-picker.tsx calls GET /api/geo/search?q= and, on any
/// failure, disables itself with "Venue search is unavailable right now" - which
/// would make the create form impossible to complete, since a venue is mandatory.
/// Returning this fixed list is what lets Milestone 1 be exercised end to end in a
/// browser.
///
/// Deliberately NOT placed behind a feature flag: an unauthenticated public
/// endpoint that returns a hardcoded list is harmless and obvious, and the swap to
/// the real Places API (IMPLEMENTATION_PLAN.md §5) replaces this body without
/// changing the route or the response shape. It returns the same eight venues the
/// client's offline fallback uses, so demo data and search results agree.
/// </summary>
[ApiController]
[Route("api/geo")]
public class GeoSearchController : ControllerBase
{
    private static readonly VenueSuggestion[] KnownVenues =
    [
        new("venue-glen-waverley-badminton", "Glen Waverley Badminton Centre", "100 Kingsway, Glen Waverley VIC 3150", -37.8708, 145.1615),
        new("venue-bob-jane-track", "Bob Jane Running Track", "Acland St, Melbourne VIC 3000", -37.8247, 144.9786),
        new("venue-lakeside-stadium", "Lakeside Stadium", "Storramadrup Rd, Melbourne VIC 3009", -37.8136, 144.9331),
        new("venue-msac", "Melbourne Sports and Aquatic Centre", "732 Ferrars St, West Melbourne VIC 3003", -37.8024, 144.9437),
        new("venue-yarra-park", "Yarra Park", "Michaelenthle Cres, East Melbourne VIC 3002", -37.8159, 144.9921),
        new("venue-coode-island", "Coode Island", "Kings Way, Melbourne VIC 3008", -37.8055, 144.9525),
        new("venue-olympic-park", "Olympic Park", "747 Lewar St, Melbourne VIC 3000", -37.81, 144.9803),
        new("venue-wesley-badc", "Wesley Badminton Centre", "1221 Nepean Hwy, Hughesdale VIC 3166", -37.8759, 145.0748),
    ];

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<VenueSuggestion>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<VenueSuggestion>> Search([FromQuery] string? q, CancellationToken ct)
    {
        var term = q?.Trim();

        // Empty query returns the whole list: the picker opens with suggestions
        // before anything is typed, and an empty list there reads as "broken".
        if (string.IsNullOrEmpty(term))
        {
            return Ok(KnownVenues);
        }

        var matches = KnownVenues
            .Where(v => v.VenueName.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || v.Address.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(matches);
    }
}
