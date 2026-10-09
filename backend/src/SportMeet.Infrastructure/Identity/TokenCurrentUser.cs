using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;

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
    private UserRole _role = UserRole.Member;

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

    /// <summary>Same lazy resolution as <see cref="UserId"/>: a request that never
    /// asks about privileges pays no extra read, and one that does reads the role
    /// off the row the id lookup already loaded.</summary>
    public bool IsAdmin
    {
        get
        {
            if (_resolved is null) ResolveAsync().GetAwaiter().GetResult();
            return _role.IsAdmin();
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
            (_resolved, _role) = await _users.ResolveUserAsync(identity, context.RequestAborted);
            _isDemo = false;
            return;
        }

        _resolved = _demo.AsUserId;
        _isDemo = _demo.AsUserId is not null;
        // The demo identity is the local stand-in for "the person running this
        // stack", so it carries the privileges that person needs to see the whole
        // product - including the admin review queue. A verified token never
        // reaches this line: its role comes from its users row.
        //
        // The role is read from the demo id's users row rather than hardcoded to
        // Admin, so the review gate can be exercised locally: point Demo:AsUserId
        // at a Member row (e.g. the seeded participant) and public creates land in
        // PendingReview; point it at an Admin row and they publish directly. A
        // missing row (or an unreachable database) stays Member - the safe
        // default, since a demo identity that cannot be looked up has no proven
        // privilege. The demo warning in StartupTasks still fires on AsUserId
        // alone, so demo mode remains visible in the logs either way.
        _role = UserRole.Member;
        if (_isDemo)
        {
            try
            {
                _role = await _users.FindRoleByIdAsync(_demo.AsUserId!.Value,
                    context?.RequestAborted ?? CancellationToken.None);
            }
            catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException
                                       or InvalidOperationException
                                       or Npgsql.PostgresException
                                       or System.Threading.Tasks.TaskCanceledException)
            {
                // Lookup failure is not fatal: browse and demo relations still
                // work with the id alone, so keep serving and stay non-admin.
            }
        }
    }
}
