using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Seeding;

/// <summary>
/// Sports are no longer the browse vocabulary - free tags replaced them, and the
/// client's SportKey union is gone. The rows survive for exactly one reason:
/// events.sport_id is a nullable emoji lookup, so the seeded glyph still renders
/// on cards, map pins and the detail header without a component owning a hardcoded
/// icon map.
///
/// The slug = lower(slug) CHECK and the name/slug unique indexes still apply, and
/// the list still mirrors frontend/src/lib/sports.ts's fallback map so offline
/// dev and production show the same glyph.
/// </summary>
public static class DemoSeed
{
    /// <summary>The only host in a database with no auth. Referenced by
    /// events.host_id, which is NOT NULL, so some user must exist for demo events
    /// to be referentially honest rather than pointing at a fabricated id.</summary>
    public static readonly Guid DefaultHostId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public const string DefaultHostName = "Demo Host";

    /// <summary>The demo *participant* — a second seeded user who hosts nothing, so
    /// the current-user identity can join/leave the demo events. With one user the
    /// viewer is always the host and "join" can never succeed ("You cannot join an
    /// event you are hosting"); a non-hosting identity is what makes joining real.
    /// <see cref="DemoUserOptions.AsUserId"/> defaults to this.</summary>
    public static readonly Guid DefaultParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public const string DefaultParticipantName = "Sam Rivera";

    public static readonly Sport[] Sports =
    [
        new() { Id = 1, Name = "Badminton", Slug = "badminton", Icon = "\U0001F3F8" },
        new() { Id = 2, Name = "Basketball", Slug = "basketball", Icon = "\U0001F3C0" },
        new() { Id = 3, Name = "Running", Slug = "running", Icon = "\U0001F3C3" },
        new() { Id = 4, Name = "Soccer", Slug = "soccer", Icon = "\U0001F3D2" },
        new() { Id = 5, Name = "Tennis", Slug = "tennis", Icon = "\U0001F3BE" },
        new() { Id = 6, Name = "Cricket", Slug = "cricket", Icon = "\U0001F3CF" },
        new() { Id = 7, Name = "Netball", Slug = "netball", Icon = "\U0001F94E" },
        new() { Id = 8, Name = "Volleyball", Slug = "volleyball", Icon = "\U0001F3D0" },
    ];

    public static readonly DemoEventSeed[] Events =
    [
        new(new Guid("11111111-1111-4111-8111-000000000001"), "Badminton Monday", "badminton", SkillLevel.Intermediate,
            0, 18, 20, "Glen Waverley Badminton Centre", "100 Kingsway, Glen Waverley VIC 3150", -37.8708, 145.1615, 15m, 4,
            "Two courts booked 6-8pm. Bring your own racket, shuttlecocks provided. Doubles format, rotate partners every game."),
        new(new Guid("11111111-1111-4111-8111-000000000002"), "Thursday Run at the Track", "running", SkillLevel.Beginner,
            1, 7, 8, "Bob Jane Running Track", "Acland St, Melbourne VIC 3000", -37.8247, 144.9786, null, 8,
            "Four easy k's at a pace where nobody gets dropped. Water fountains on the north side."),
        new(new Guid("11111111-1111-4111-8111-000000000003"), "Social Soccer", "soccer", SkillLevel.Intermediate,
            3, 16, 17, "Lakeside Stadium", "Storramadrup Rd, Melbourne VIC 3009", -37.8136, 144.9331, 10m, 8,
            "Full five-a-side. Moulded boots and shin pads are on you."),
        new(new Guid("11111111-1111-4111-8111-000000000004"), "Basketball Thursday", "basketball", SkillLevel.Advanced,
            1, 20, 21, "Melbourne Sports and Aquatic Centre", "732 Ferrars St, West Melbourne VIC 3003", -37.8024, 144.9437, 12.5m, 6,
            "Half-court, winner stays. Competitive but nobody keeps score."),
        new(new Guid("11111111-1111-4111-8111-000000000005"), "Badminton Wednesday", "badminton", SkillLevel.Intermediate,
            2, 18, 20, "Glen Waverley Badminton Centre", "100 Kingsway, Glen Waverley VIC 3150", -37.8708, 145.1615, 15m, 4,
            "The midweek one. Same court, same crew, slightly less traffic."),
        new(new Guid("11111111-1111-4111-8111-000000000006"), "Sunday Cricket at the Park", "cricket", SkillLevel.Beginner,
            6, 10, 14, "Yarra Park", "Michaelenthle Cres, East Melbourne VIC 3002", -37.8159, 144.9921, 8m, 12,
            "Two-hour casual hit-up. Bat and ball provided."),
        new(new Guid("11111111-1111-4111-8111-000000000007"), "Tennis Saturday", "tennis", SkillLevel.Advanced,
            5, 9, 10, "Coode Island", "Kings Way, Melbourne VIC 3008", -37.8055, 144.9525, 20m, 4,
            "Singles if four show, doubles if six do. Courts 3 and 4."),
        new(new Guid("11111111-1111-4111-8111-000000000008"), "Netball Tuesday", "netball", SkillLevel.Beginner,
            8, 19, 20, "Olympic Park", "747 Lewar St, Melbourne VIC 3000", -37.81, 144.9803, 7m, 10,
            "Newcomers welcome - we teach the positions on the night."),
    ];
}

/// <summary>
/// A demo event expressed as an offset from today rather than a fixed instant, so
/// a freshly seeded database always has upcoming events. Times are interpreted in
/// Melbourne time because every seeded venue is a Melbourne venue and the client
/// renders start times in the event's own timezone.
/// </summary>
public sealed record DemoEventSeed(
    Guid Id,
    string Title,
    string SportSlug,
    SkillLevel Skill,
    int DaysFromNow,
    int StartHour,
    int EndHour,
    string Venue,
    string Address,
    double Lat,
    double Lng,
    decimal? Cost,
    int Max,
    string? Description)
{
    private const string MelbourneZone = "Australia/Melbourne";

    public DateTimeOffset StartFromNow(DateTimeOffset nowUtc) => At(nowUtc, DaysFromNow, StartHour);

    public DateTimeOffset EndFromNow(DateTimeOffset nowUtc) => At(nowUtc, DaysFromNow, EndHour);

    private static DateTimeOffset At(DateTimeOffset nowUtc, int days, int hour)
    {
        var zone = ResolveZone(MelbourneZone);
        var localToday = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var local = new DateTime(localToday.Year, localToday.Month, localToday.Day, hour, 0, 0, DateTimeKind.Unspecified)
            .AddDays(days);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// Windows accepts the IANA id through the compatibility mapping but hands
    /// back the Windows id as DisplayName, so the round trip is what proves the
    /// zone resolved - not FindSystemTimeZoneById succeeding. If a machine ever
    /// has no mapping at all, UTC is the honest fallback: a demo event three or
    /// four hours off is a cosmetic issue, whereas throwing here stops the API
    /// from starting and looks like a database failure.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string ianaId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return zone.Id == ianaId || zone.DisplayName == ianaId
                ? zone
                : TimeZoneInfo.Utc;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
