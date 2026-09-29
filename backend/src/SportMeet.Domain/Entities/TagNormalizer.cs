namespace SportMeet.Domain.Entities;

/// <summary>
/// The single place a raw "#Tennis" becomes a stored tag name. The client runs
/// the same rules in frontend/src/lib/tags.ts; this is the authoritative copy,
/// and the server is the one that decides what reaches the database.
///
/// Deliberately not case-insensitive-collision-aware beyond lowercasing: two
/// spellings that normalize identically collapse to one row because they are
/// the same string by the time they are compared, not because Postgres folds
/// case (the unique index is ordinal).
/// </summary>
public static class TagNormalizer
{
    public const int MaxTagsPerEvent = 5;
    public const int MaxTagLength = 25;
    public const int MinTagLength = 2;

    /// <summary>Lowercase, strip leading '#', collapse internal whitespace to a
    /// single space, trim. Returns null for anything that is not a usable tag:
    /// too short, too long, or punctuation/emoji only (so a stray "#" or "!!!"
    /// does not become a tag that can never be typed back into a search box).</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().TrimStart('#').Trim();
        if (value.Length == 0)
        {
            return null;
        }

        // Collapse runs of whitespace so "#board games" and "#board  games" agree.
        value = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        value = value.ToLowerInvariant();

        if (value.Length is < MinTagLength or > MaxTagLength)
        {
            return null;
        }

        // At least one letter or digit, so "#" alone or "#?!." never lands in the
        // tags table.
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>True when every entry in <paramref name="raws"/> survives
    /// normalization. Exists so a validator can reject a malformed tag instead of
    /// silently dropping it and telling the user their tag was saved.
    /// Null/empty list is valid: tags are optional.</summary>
    public static bool HasOnlyValid(IEnumerable<string> raws)
    {
        foreach (var raw in raws)
        {
            if (Normalize(raw) is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Normalize, drop invalid, dedupe preserving first-seen order, then
    /// cap. Order is preserved so the client's chip order survives a round trip
    /// rather than coming back alphabetized.</summary>
    public static List<string> NormalizeMany(IEnumerable<string?> raws, int max = MaxTagsPerEvent)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(max);

        foreach (var raw in raws)
        {
            var name = Normalize(raw);
            if (name is null || !seen.Add(name))
            {
                continue;
            }

            result.Add(name);
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }
}
