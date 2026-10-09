using SportMeet.Application.Imports;
using SportMeet.Application.Ingestion;

namespace SportMeet.Import.Tests;

public class FieldFlaggerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(11));
    private static readonly GeocodeResult Exact = new(-37.81, 144.96, "ROOFTOP", NeedsConfirm: false);

    private const string Clear = "Friday social badminton on 14 Nov at 7:30pm, Seddon Park, 42 Railway Ave. 12 spots.";

    private static ExtractedEvent Full() => new()
    {
        Title = "Social badminton",
        StartAt = Now.AddDays(37),
        EndAt = Now.AddDays(37).AddHours(2),
        VenueName = "Seddon Park",
        Address = "42 Railway Ave, Seddon",
        MaxParticipants = 12,
        Confidence = 0.9m,
    };

    private static string[] Fields(ExtractedEvent e, GeocodeResult? geo, string text)
        => FieldFlagger.Flag(e, geo, text, Now).Select(f => $"{f.Field}:{f.Reason}").ToArray();

    [Fact]
    public void A_complete_confident_extraction_raises_no_flags()
        => Assert.Empty(Fields(Full(), Exact, Clear));

    [Fact]
    public void A_relative_date_is_flagged_because_it_was_resolved_against_today()
        => Assert.Contains("startAt:unclear", Fields(Full(), Exact, "Badminton Friday 7:30pm at Seddon Park"));

    [Fact]
    public void A_date_with_no_time_is_flagged()
        => Assert.Contains("startAt:unclear", Fields(Full(), Exact, "Badminton on 14 Nov at Seddon Park"));

    [Theory]
    [InlineData("14 Nov 7pm")]
    [InlineData("Nov 14 7pm")]
    [InlineData("14th of November, 7.30pm")]
    [InlineData("14/11 at 19:30")]
    [InlineData("2026-11-14 noon")]
    public void An_explicit_date_and_time_are_not_flagged(string text)
        => Assert.DoesNotContain(Fields(Full(), Exact, text), f => f.StartsWith("startAt"));

    [Fact]
    public void A_time_range_is_not_mistaken_for_a_numeric_date()
        => Assert.Contains("startAt:unclear", Fields(Full(), Exact, "Friday 7-9pm"));

    [Fact]
    public void A_past_start_is_flagged_past()
        => Assert.Contains("startAt:past", Fields(Full().With(start: Now.AddDays(-2)), Exact, Clear));

    [Fact]
    public void Missing_fields_are_flagged_and_defaults_are_marked_assumed()
    {
        var flags = Fields(new ExtractedEvent { Confidence = 0.9m }, null, "hello");
        Assert.Contains("title:missing", flags);
        Assert.Contains("startAt:missing", flags);
        Assert.Contains("endAt:assumed", flags);
        Assert.Contains("venue:missing", flags);
        Assert.Contains("maxParticipants:assumed", flags);
    }

    [Fact]
    public void A_low_confidence_title_is_flagged()
        => Assert.Contains("title:unclear", Fields(Full().With(confidence: 0.4m), Exact, Clear));

    [Fact]
    public void A_named_place_with_no_pin_is_unconfirmed()
        => Assert.Contains("venue:unconfirmed", Fields(Full(), null, Clear));

    [Fact]
    public void A_pin_the_geocoder_is_unsure_of_is_unconfirmed()
        => Assert.Contains("venue:unconfirmed",
            Fields(Full(), Exact with { NeedsConfirm = true }, Clear));
}

internal static class ExtractedEventTestExtensions
{
    /// <summary>ExtractedEvent has init-only properties and is not a record, so a test
    /// variant is rebuilt rather than mutated.</summary>
    public static ExtractedEvent With(
        this ExtractedEvent e, DateTimeOffset? start = null, decimal? confidence = null) => new()
    {
        Title = e.Title,
        StartAt = start ?? e.StartAt,
        EndAt = e.EndAt,
        VenueName = e.VenueName,
        Address = e.Address,
        MaxParticipants = e.MaxParticipants,
        Confidence = confidence ?? e.Confidence,
    };
}
