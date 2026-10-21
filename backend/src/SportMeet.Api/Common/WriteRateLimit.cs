using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SportMeet.Infrastructure.Identity;

namespace SportMeet.Api.Common;

/// <summary>
/// Per-user ceilings on the writes people can repeat quickly: direct messages and
/// forum comments. Generous for a person typing, low enough that a script cannot
/// flood someone's inbox or a thread.
///
/// Partitioned like <see cref="ImportRateLimit"/>: by the verified Firebase uid, or
/// the IP address when there is none. The 429 response shape and Retry-After header
/// come from the rejection handler ImportRateLimit installs.
/// </summary>
public static class WriteRateLimit
{
    public const string Messages = "messages";
    public const string Comments = "comments";

    /// <summary>Page-view recording. Generous for real browsing, but a cap on how fast
    /// one client can push a post up Monash Pulse.</summary>
    public const string Views = "views";

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(Messages, http => PerUser(http, permitsPerMinute: 30));
        options.AddPolicy(Comments, http => PerUser(http, permitsPerMinute: 10));
        options.AddPolicy(Views, http => PerUser(http, permitsPerMinute: 60));
    }

    private static RateLimitPartition<string> PerUser(HttpContext http, int permitsPerMinute) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            http.User.FindFirst(FirebaseJwtBearerEvents.AuthUidClaimType)?.Value
                ?? http.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
}
