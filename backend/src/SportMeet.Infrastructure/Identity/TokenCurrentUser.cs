using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// The identity resolved from a verified bearer token. This is what makes user-owned
/// drafts private: UserId comes from a token the API validated, not from configuration,
/// so one caller can never act as another.
///
/// The database lookup (Firebase UID to internal user id, creating the row on first
/// sight) runs lazily on first access so anonymous GETs and any endpoint that never
/// touches ICurrentUser pay nothing for it. Falls back to the configured demo identity
/// only when there is no verified user AND a demo id is configured, so Milestone-1
/// anonymous dev/tests keep working while production (demo unset) has no fallback.
/// </summary>
public sealed class TokenCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserStore _users;
    private readonly DemoUserOptions _demo;
    private Guid? _resolved;
    private bool _isDemo;

    public TokenCurrentUser(
        IHttpContextAccessor httpContextAccessor,
        IUserStore users,
        IOptions<DemoUserOptions> demo)
    {
        _httpContextAccessor = httpContextAccessor;
        _users = users;
        _demo = demo.Value;
    }

    public Guid? UserId
    {
        get
        {
            if (_resolved is null) ResolveAsync().GetAwaiter().GetResult();
            return _resolved;
        }
    }

    public bool IsDemo
    {
        get
        {
            if (_resolved is null) ResolveAsync().GetAwaiter().GetResult();
            return _isDemo;
        }
    }

    private async Task ResolveAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        var authUid = context?.User?.FindFirst(FirebaseJwtBearerEvents.AuthUidClaimType)?.Value;

        if (!string.IsNullOrEmpty(authUid))
        {
            var identity = new VerifiedIdentity(
                authUid,
                context!.User.FindFirst(ClaimTypes.Email)?.Value,
                context.User.FindFirst(ClaimTypes.Name)?.Value,
                context.User.FindFirst("picture")?.Value);
            _resolved = await _users.ResolveUserIdAsync(identity, context.RequestAborted);
            _isDemo = false;
            return;
        }

        _resolved = _demo.AsUserId;
        _isDemo = _demo.AsUserId is not null;
    }
}
