namespace SportMeet.Application.Common;

/// <summary>
/// Mirrors the Paged&lt;T&gt; the frontend sends to SWR in
/// frontend/src/types/events.ts. Property order is irrelevant but the four
/// names are load-bearing: useEvents destructures items/totalCount and the
/// browse header renders totalCount.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
