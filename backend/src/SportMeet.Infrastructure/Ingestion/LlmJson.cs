using System.Text;
using System.Text.Json;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// Repairs and salvages the JSON an LLM returns, because a model's output is not a
/// machine-emitted payload. Neither provider guarantees parseable text at the transport level
/// (OpenAI asks for structured output; Claude has no structured-output mode), so the model can
/// hand back JSON a strict <see cref="JsonSerializer"/> rejects even though every field it
/// wrote is correct. The two shapes seen in practice: a raw control character inside a string
/// (a literal newline in address/description - illegal inside a JSON string, so the reader dies
/// mid-value at that property), and truncation mid-string (a token limit or stream drop).
///
/// Repair only ever escapes bytes or closes containers it opened - it never invents a value,
/// coerces a type, or guesses a missing key. Valid JSON is returned untouched; a response that
/// cannot be made to yield a usable object is reported as unparsable rather than fabricated.
/// </summary>
public static class LlmJson
{
    /// <summary>True when <paramref name="json"/> is already a parseable JSON value.</summary>
    public static bool IsValid(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Repair a model's JSON text so a strict parser accepts it, or return <c>null</c> when it
    /// cannot be made structurally valid. A single forward scan tracks whether it is inside a
    /// string literal: raw control characters there are escaped, an unterminated string is
    /// closed at the truncation point, and any containers left open at the end are closed.
    /// Valid input is returned byte-identical.
    /// </summary>
    public static string? Repair(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (IsValid(json)) return json;

        var sb = new StringBuilder(json.Length + 32);
        bool inString = false;
        bool escaped = false;
        bool afterColon = false;   // just consumed an object key's ':'
        bool stringIsValue = false; // the string currently open is a value, not a key
        int openObjects = 0;
        int openArrays = 0;
        // Length of sb just after the last byte that is a complete JSON value boundary - the
        // close of a string/number/bool/null or a whole container. Cutting here on a truncation
        // drops only the incomplete trailing member and keeps every complete one.
        int lastComplete = 0;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped) { sb.Append(c); escaped = false; continue; }
                if (c == '\\') { sb.Append(c); escaped = true; continue; }
                if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                    lastComplete = sb.Length; // a string (key or value) just closed
                    continue;
                }
                // A raw control byte inside the literal: escape the ones JSON requires, drop
                // any other control byte rather than corrupt the value with it.
                if (c < 0x20)
                {
                    sb.Append(c switch
                    {
                        '\n' => "\\n",
                        '\r' => "\\r",
                        '\t' => "\\t",
                        '\b' => "\\b",
                        '\f' => "\\f",
                        _ => string.Empty,
                    });
                    continue;
                }
                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    // A quote right after a ':' opens a value string; one after '{' or ',' inside
                    // an object is a key, and one after '[' or ',' inside an array is a value.
                    stringIsValue = afterColon;
                    afterColon = false;
                    sb.Append(c);
                    break;
                case '{': openObjects++; sb.Append(c); afterColon = false; break;
                case '[': openArrays++; sb.Append(c); afterColon = false; break;
                case '}': if (openObjects > 0) openObjects--; sb.Append(c); afterColon = false; lastComplete = sb.Length; break;
                case ']': if (openArrays > 0) openArrays--; sb.Append(c); afterColon = false; lastComplete = sb.Length; break;
                case ',': sb.Append(c); afterColon = false; break;
                case ':': sb.Append(c); afterColon = true; break;
                default:
                    sb.Append(c);
                    // A number or bare literal (true/false/null) has no delimiter of its own, so
                    // every non-delimiter byte is a candidate complete-value end until the next
                    // delimiter or the truncation point settles it.
                    if (!char.IsWhiteSpace(c)) lastComplete = sb.Length;
                    break;
            }
        }
        // Closed cleanly: only control-character escaping was needed.
        if (!inString && openObjects == 0 && openArrays == 0)
            return TrimTrailingCommas(sb.ToString());

        // Truncated. If the cut landed inside a *value* string, close the literal and keep the
        // partial value - it is real text the model was writing. If it landed inside a property
        // *name* ("...confidence"), on a dangling `"key":`, or mid-number, the trailing member is
        // incomplete: rewind to the last complete value boundary and keep only what finished. A
        // partial number is never trusted ("1" of "12" would silently change a capacity).
        string repaired;
        if (inString && stringIsValue)
        {
            repaired = sb.ToString() + "\"";
        }
        else if (inString)
        {
            // Cut inside a property name (or a key we never got a ':' for): drop the whole
            // incomplete member.
            repaired = sb.ToString()[..lastComplete];
        }
        else
        {
            // Cut right after a `"key":` (with no value) or right after a bare literal
            // (number/true/false/null, which is not delimiter-terminated and may be truncated).
            // Both leave the trailing member incomplete, so rewind before its key and keep only
            // what finished.
            repaired = RewindBeforeKey(sb.ToString(), lastComplete);
        }
        repaired = TrimTrailingCommas(repaired);

        // Close whatever containers the cut left open. These responses are a single root object,
        // so a '}' per open container is correct; IsValid rejects the candidate if the shape was
        // ever more exotic, and the caller then reports the response unparsable rather than
        // trusting a guess.
        var (objs, arrs) = ScanOpenContainers(repaired);
        var candidate = repaired + new string('}', objs + arrs);
        return IsValid(candidate) ? candidate : null;
    }

    /// <summary>Trim whitespace and a leading/trailing Markdown code fence (```json ... ```).
    /// Shared by both extractors so a fenced reply parses identically on either provider.</summary>
    public static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return value;

        var firstBreak = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstBreak < 0 || lastFence <= firstBreak
            ? value
            : trimmed[(firstBreak + 1)..lastFence];
    }

    private static string TrimTrailingCommas(string json)
    {
        var s = json.TrimEnd();
        while (s.EndsWith(",", StringComparison.Ordinal))
            s = s[..^1].TrimEnd();
        return s;
    }

    /// <summary>
    /// Rewind a truncated prefix that ended mid-member back to the delimiter that opened that
    /// member (the previous <c>,</c> or the enclosing <c>{</c>), so the incomplete trailing
    /// member is dropped rather than trusted.
    ///
    /// <paramref name="end"/> is the offset of the last complete token - the end of the dangling
    /// key, or the end of a partial number. Everything from there on is the fragment to discard.
    /// If no member-start delimiter precedes it (a malformed head of the object), returns the
    /// prefix unchanged and lets IsValid decide.
    /// </summary>
    private static string RewindBeforeKey(string json, int end)
    {
        var s = json[..end];
        int comma = s.LastIndexOf(',');
        int open = s.LastIndexOf('{');
        int at = Math.Max(comma, open);
        return at >= 0 ? s[..(at + 1)] : s;
    }

    /// <summary>Count containers left open by a (possibly truncated) prefix, ignoring any
    /// inside string literals.</summary>
    private static (int Objects, int Arrays) ScanOpenContainers(string json)
    {
        int objs = 0, arrs = 0;
        bool inString = false, escaped = false;
        foreach (var c in json)
        {
            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': objs++; break;
                case '[': arrs++; break;
                case '}': if (objs > 0) objs--; break;
                case ']': if (arrs > 0) arrs--; break;
            }
        }
        return (objs, arrs);
    }
}
