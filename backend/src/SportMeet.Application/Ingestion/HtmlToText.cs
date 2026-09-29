using System.Globalization;
using System.Text.RegularExpressions;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// HTML email body to the plain text the extractor reads.
///
/// Deliberately not a general HTML parser and deliberately not HtmlAgilityPack. It has
/// one job: get invitation sentences out of an HTML email. Adding a dependency to obtain
/// a correct DOM from mail that is *adversarial input* would imply we render the result
/// somewhere; we do not — it goes to a text extractor and a database column. A
/// throw-away text scan with the tag noise removed is the honest scope, and the
/// <see cref="IngestionOptions.MaxBodyChars"/> cap bounds the blast radius either way.
///
/// Two things it does that a naive regex strip does not, both because the input is
/// untrusted:
/// <list type="bullet">
/// <item>script/style are removed *with their contents*, so JavaScript and CSS never
/// reach the extractor. Leaving them in would feed "ignore previous instructions" text an
/// author hid in a style block.</item>
/// <item>tags are stripped before entities are decoded, so
/// <c>&amp;lt;script&amp;gt;</c> cannot reconstitute into markup after the strip.</item>
/// </list>
/// </summary>
public static class HtmlToText
{
    /// <summary>Elements whose *text content* is not prose. Their insides are dropped,
    /// not unwrapped. Non-greedy and single-occurrence-per-match so nested repeats of
    /// the same tag still terminate.</summary>
    private static readonly Regex DropContent = Block(@"script|style|head|title|noscript|template");

    private static readonly Regex StripTags = new(@"<[^>]*>", Tags());

    /// <summary>Block-level closers and void elements become line breaks. Without this
    /// an entire newsletter collapses to one run-on line and every line-oriented
    /// heuristic in the extractor reads garbage.</summary>
    private static readonly Regex BlockBreak = Block(@"br|/p|/div|/li|/tr|/table|/h[1-6]|/td|/th|/blockquote");

    private static readonly Regex CollapseBlankLines = new(@"\n{3,}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CollapseSpaces = new(@"[ \t]{2,}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Longest entity we bother with. Numeric refs are handled generically.</summary>
    private static readonly Regex NumericEntity = new(@"&#(x?[0-9a-fA-F]+);", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static RegexOptions Tags() => RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled;

    private static Regex Block(string inner) =>
        new($"<{inner}[^>]*>.*?</{inner}>", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = DropContent.Replace(html, " ");
        text = BlockBreak.Replace(text, "\n");
        text = StripTags.Replace(text, " ");
        text = DecodeEntities(text);

        text = CollapseSpaces.Replace(text, " ");
        text = CollapseBlankLines.Replace(text, "\n\n");

        // Per-line trim, then drop lines that emptied out — table-heavy invites leave a
        // lot of whitespace-only lines where cells used to be.
        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// Named entities the ones that actually appear in mail, plus numeric refs.
    /// Unknown <c>&amp;foo;</c> is left alone rather than guessed: silently dropping
    /// text the extractor might have needed is worse than one stray ampersand.
    /// </summary>
    private static string DecodeEntities(string value)
    {
        var text = value
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'")
            .Replace("&ndash;", "-")
            .Replace("&mdash;", "-")
            .Replace("&hellip;", "...")
            .Replace("&deg;", " deg")
            .Replace("&plusmn;", "+/-");

        return NumericEntity.Replace(text, m =>
        {
            var raw = m.Groups[1].Value;
            var hex = raw.StartsWith('x') || raw.StartsWith('X');
            var parsed = hex
                ? int.TryParse(raw[1..], NumberStyles.HexNumber, null, out var h) ? h : -1
                : int.TryParse(raw, out var d) ? d : -1;

            // Unpaired surrogates and out-of-range values would throw in
            // char.ConvertFromUtf32; a mail body is not the place to find out.
            return IsScalar(parsed) ? char.ConvertFromUtf32(parsed) : m.Value;
        });
    }

    /// <summary>Valid Unicode scalar value: in range and not a surrogate code point,
    /// which is what <see cref="char.ConvertFromUtf32"/> rejects.</summary>
    private static bool IsScalar(int codePoint) =>
        codePoint is > 0 and <= 0x10FFFF && (codePoint < 0xD800 || codePoint > 0xDFFF);
}
