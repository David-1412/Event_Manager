using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SportMeet.Application.Imports;
using SportMeet.Infrastructure.Identity;

namespace SportMeet.Api.Common;

/// <summary>
/// Per-user ceiling on imports. A signed-in caller can otherwise trigger as many model and
/// geocoder calls as they like. Partitioned by the verified Firebase uid (the same claim
/// <c>ICurrentUser</c> reads), so one account cannot spend another's allowance, with the
/// client address as the fallback for the unauthenticated requests that are about to be
/// refused anyway.
/// </summary>
public static class ImportRateLimit
{
    public const string PolicyName = "imports";

    public static void Configure(RateLimiterOptions options, ImportOptions import)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, http => RateLimitPartition.GetSlidingWindowLimiter(
            http.User.FindFirst(FirebaseJwtBearerEvents.AuthUidClaimType)?.Value
                ?? http.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = import.MaxPerUserPerHour,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }));

        options.OnRejected = async (context, ct) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
            var problem = ProblemDetailsDefaults.RateLimited();
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(problem, ct);
        };
    }
}
