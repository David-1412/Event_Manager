using SportMeet.Application.Events;
using SportMeet.Application.Imports;

namespace SportMeet.Import.Tests;

public class ImportDiffTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 19, 30, 0, TimeSpan.FromHours(11));

    private static CreateEventDto Extracted() => new()
    {
        Title = "Social badminton",
        Description = "Bring a racket",
        StartAt = Start,
        EndAt = Start.AddHours(2),
        VenueName = "Seddon Park",
        Address = "42 Railway Ave, Seddon",
        Latitude = -37.8100,
        Longitude = 144.8900,
        MaxParticipants = 12,
        Cost = 5,
        Tags = ["#badminton"],
    };

    private static IReadOnlyDictionary<string, string> Diff(CreateEventDto extracted, CreateEventDto final)
        => ImportDiff.Compare(extracted, final);

    [Fact]
    public void An_untouched_proposal_is_all_kept()
    {
        var outcome = Diff(Extracted(), Extracted());
        Assert.All(outcome.Values, v => Assert.Equal("kept", v));
        Assert.Equal(10, outcome.Count);
    }

    [Fact]
    public void An_edited_title_is_changed_and_the_rest_stay_kept()
    {
        var outcome = Diff(Extracted(), Merge(Extracted(), title: "Friday badminton"));
        Assert.Equal("changed", outcome["title"]);
        Assert.Equal("kept", outcome["startAt"]);
    }

    [Fact]
    public void A_value_the_extractor_missed_is_filled_and_one_the_user_removed_is_cleared()
    {
        var extracted = Extracted();
        var final = Merge(extracted, cost: null, maxParticipants: 8);
        var outcome = Diff(new CreateEventDto { Title = "x", MaxParticipants = null, Cost = 5 }, final);
        Assert.Equal("filled", outcome["maxParticipants"]);
        Assert.Equal("cleared", outcome["cost"]);
    }

    [Fact]
    public void A_field_empty_on_both_sides_is_not_reported()
    {
        var outcome = Diff(new CreateEventDto { Title = "x" }, new CreateEventDto { Title = "x" });
        Assert.Equal(["title"], outcome.Keys);
    }

    [Fact]
    public void Free_as_null_or_zero_is_not_a_change()
    {
        var outcome = Diff(new CreateEventDto { Cost = 0 }, new CreateEventDto { Cost = null });
        Assert.False(outcome.ContainsKey("cost"));
    }

    [Fact]
    public void Tags_compare_as_normalised_sets()
    {
        var outcome = Diff(
            new CreateEventDto { Tags = ["#Badminton", "social"] },
            new CreateEventDto { Tags = ["social", "badminton"] });
        Assert.Equal("kept", outcome["tags"]);
    }

    [Fact]
    public void Whitespace_only_text_differences_are_kept()
    {
        var outcome = Diff(
            new CreateEventDto { Title = "Social  badminton " },
            new CreateEventDto { Title = "Social badminton" });
        Assert.Equal("kept", outcome["title"]);
    }

    [Fact]
    public void Times_within_a_minute_are_kept_and_further_apart_are_changed()
    {
        Assert.Equal("kept", Diff(new() { StartAt = Start }, new() { StartAt = Start.AddSeconds(30) })["startAt"]);
        Assert.Equal("changed", Diff(new() { StartAt = Start }, new() { StartAt = Start.AddMinutes(30) })["startAt"]);
    }

    [Fact]
    public void A_nudged_pin_within_100m_is_kept_and_a_moved_pin_is_changed()
    {
        var near = Merge(Extracted(), lat: -37.8105, lon: 144.8900);   // ~55 m
        var far = Merge(Extracted(), lat: -37.8200, lon: 144.8900);    // ~1.1 km
        Assert.Equal("kept", Diff(Extracted(), near)["location"]);
        Assert.Equal("changed", Diff(Extracted(), far)["location"]);
    }

    [Fact]
    public void The_forms_zero_zero_pin_counts_as_no_pin()
    {
        var outcome = Diff(
            new CreateEventDto { Title = "x" },
            new CreateEventDto { Title = "x", Latitude = 0, Longitude = 0 });
        Assert.False(outcome.ContainsKey("location"));
    }

    private static CreateEventDto Merge(
        CreateEventDto e, string? title = null, decimal? cost = 5, int? maxParticipants = 12,
        double? lat = null, double? lon = null) => new()
    {
        Title = title ?? e.Title,
        Description = e.Description,
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = lat ?? e.Latitude,
        Longitude = lon ?? e.Longitude,
        MaxParticipants = maxParticipants,
        Cost = cost,
        Tags = e.Tags,
    };
}
