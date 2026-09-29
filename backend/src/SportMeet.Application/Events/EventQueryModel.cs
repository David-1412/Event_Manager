using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>How results are ordered. Maps to the client's sort param.</summary>
public enum EventSort
{
    Date,
    Distance,
}

/// <summary>
/// The browse filter set, translated from the query string by the controller.
///
/// Field names follow what frontend/src/lib/query.ts actually emits, not the
/// wider contract in IMPLEMENTATION_PLAN.md §3: the client sends exactly
/// q / tag / date / radius / sort / page. The plan's lat, lng, from, to, skill
/// and pageSize are accepted as optional aliases but are not required for the
/// frontend to work.
/// </summary>
public sealed class EventQueryModel
{
    public string? Search { get; init; }


    /// <summary>Normalized tag name; null means no tag filter. Replaces the old
    /// `?sport=` slug filter. Accepts `?tag=` or `?tags=`.</summary>
    public string? TagName { get; init; }


    /// <summary>null = "any". Translated to a window in the repository: today is
    /// the next 24h, week the next 7 days. Always bounded below by now, which
    /// fixes the fixture behaviour of treating the filter as an upper bound only
    /// (so yesterday's game stops appearing under "Today").</summary>
    public EventDateFilter DateFilter { get; init; } = EventDateFilter.Any;

    /// <summary>Radius in km. Only meaningful with both origin coordinates.</summary>
    public double? RadiusKm { get; init; }

    public double? OriginLat { get; init; }
    public double? OriginLng { get; init; }

    public EventSort Sort { get; init; } = EventSort.Date;

    /// <summary>The client sends it but has no "load more" control yet; honoured
    /// server-side so paging needs no retrofit when the control lands.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>Distance needs an origin. Without one, distance ordering has no
    /// defined meaning and the repository falls back to date order rather than
    /// inventing an ordering the client cannot reproduce.</summary>
    public bool HasOrigin => OriginLat is not null && OriginLng is not null;

    public bool UseDistance => Sort == EventSort.Distance && HasOrigin;
}

public enum EventDateFilter
{
    Any,
    Today,
    Week,
}
