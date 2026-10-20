using System.Globalization;
using System.Text;
using SportMeet.Application.Common;

namespace SportMeet.Application.Forum;

/// <summary>
/// The position after the last post of a page: "give me the posts that sort after
/// this one". Keyset rather than offset paging, so a post arriving at the top of the
/// feed while someone scrolls does not shift every later page by one and show them a
/// duplicate.
///
/// Opaque to the client (base64url) so the frontend cannot come to depend on its
/// shape. The hot score is written round-trip ("R") because the next page compares it
/// for equality against the stored double.
/// </summary>
public sealed record ForumCursor(double? HotScore, long Seq)
{
    public string Encode()
    {
        var raw = HotScore is { } score
            ? $"h:{score.ToString("R", CultureInfo.InvariantCulture)}:{Seq}"
            : $"n:{Seq}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Null for no cursor; a malformed one is the client's error, not a
    /// silent first page, so it throws.</summary>
    public static ForumCursor? Decode(string? cursor, ForumSort sort)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split(':');
            return (sort, parts) switch
            {
                (ForumSort.Hot, ["h", var score, var seq]) => new ForumCursor(
                    double.Parse(score, NumberStyles.Float, CultureInfo.InvariantCulture),
                    long.Parse(seq, CultureInfo.InvariantCulture)),
                (ForumSort.New, ["n", var seq]) => new ForumCursor(
                    null, long.Parse(seq, CultureInfo.InvariantCulture)),
                _ => throw new FormatException(),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new DomainRuleException("That page link is no longer valid. Reload the feed.");
        }
    }
}
