using FluentValidation;
using SportMeet.Application.Events;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The create-path rule that an empty Spots field is a choice rather than a gap.
///
/// These exist mainly to pin down two behaviours that are not obvious from the
/// code and that nothing else would catch:
///
///  - FluentValidation's comparison rules are skipped for a null property by
///    default. That is what lets the validator drop its NotNull and still bound a
///    number that *is* sent, so the rule reads as though a null would be compared
///    against 2 and rejected. If a future change adds `.NotNull()` back, or sets
///    `Options.DefaultPropertyValidatorMode` to disallow nulls, the first test here
///    fails and the reason is written down rather than rediscovered.
///  - The capacity check on join uses `MaxParticipants is { } max`, which must not
///    be reached by a null being silently treated as a limit of zero. See
///    Unlimited_capacity_allows_any_number_of_joins in JoinRequestApprovalTests.
/// </summary>
public sealed class CreateEventCapacityTests
{
    private static CreateEventDto Valid(int? maxParticipants, decimal? cost = null) => new()
    {
        Title = "Tuesday Social Badminton",
        StartAt = new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.Zero),
        EndAt = new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero),
        VenueName = "Queen Victoria Women's Centre",
        Address = "210 Latrobe St, Melbourne VIC 3000",
        Latitude = -37.8136,
        Longitude = 144.9631,
        MaxParticipants = maxParticipants,
        Cost = cost,
    };

    private static async Task<string?> ErrorFor(int? maxParticipants)
    {
        var result = await new CreateEventDtoValidator()
            .ValidateAsync(Valid(maxParticipants));
        return result.IsValid
            ? null
            : string.Join(" | ", result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public async Task No_capacity_is_valid_and_reports_no_error()
    {
        Assert.Null(await ErrorFor(null));
    }

    [Fact]
    public async Task A_capacity_below_the_minimum_is_still_rejected()
    {
        // The bounds survive the removal of NotNull: null is allowed, a bad number
        // is not. If comparisons ever start running against null, this is the test
        // that shows the chain was rewritten rather than loosened.
        Assert.Contains("At least 2 people", await ErrorFor(1));
        Assert.Contains("maximum for now", await ErrorFor(51));
        Assert.Null(await ErrorFor(2));
        Assert.Null(await ErrorFor(50));
    }

    [Fact]
    public async Task Cost_keeps_its_own_null_rule_untouched()
    {
        // Guards against the capacity change bleeding into the sibling nullable
        // rule: null cost means free and must stay valid, a negative one must not.
        var withBadCost = Valid(null, cost: -1m);
        var result = await new CreateEventDtoValidator().ValidateAsync(withBadCost);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("negative"));
    }
}
