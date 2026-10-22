using SportMeet.Application.Common;

namespace SportMeet.Application.Admin;

/// <summary>What one press of the demo-engagement button did (or undid).</summary>
/// <param name="DemoAccounts">Placeholder accounts that exist after the call.</param>
/// <param name="Events">Events touched.</param>
/// <param name="Posts">Forum posts touched.</param>
/// <param name="Views">View rows added (or removed).</param>
/// <param name="Interests">Interested marks added (or removed), events and posts together.</param>
public sealed record DemoEngagementResultDto(int DemoAccounts, int Events, int Posts, int Views, int Interests);

public interface IDemoEngagementRepository
{
    /// <summary>Creates demo1..demo<paramref name="count"/> where missing; returns how many exist.</summary>
    Task<int> EnsureDemoAccountsAsync(int count, CancellationToken ct = default);

    /// <summary>Every live event and post gets <paramref name="minViews"/>-<paramref name="maxViews"/>
    /// anonymous, tagged views and <paramref name="minInterests"/>-<paramref name="maxInterests"/>
    /// Interested marks from demo accounts that have not already marked it.</summary>
    Task<DemoEngagementResultDto> AddAsync(
        int minViews, int maxViews, int minInterests, int maxInterests, CancellationToken ct = default);

    /// <summary>Deletes every tagged view and every demo account's Interested mark,
    /// and puts the counters back.</summary>
    Task<DemoEngagementResultDto> RemoveAsync(CancellationToken ct = default);
}

/// <summary>
/// The admin demo-engagement tool: sample views and Interested marks, so a new site
/// does not look empty. Everything it adds is tagged and comes from placeholder
/// accounts that other users never see, and <see cref="RemoveAsync"/> takes all of
/// it back without touching real engagement. Admin only.
/// </summary>
public sealed class DemoEngagementService(IDemoEngagementRepository repository, ICurrentUser currentUser)
{
    public const int DemoAccountCount = 20;

    public async Task<DemoEngagementResultDto> AddAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        await repository.EnsureDemoAccountsAsync(DemoAccountCount, ct);
        return await repository.AddAsync(minViews: 1, maxViews: 9, minInterests: 1, maxInterests: 3, ct);
    }

    public async Task<DemoEngagementResultDto> RemoveAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        return await repository.RemoveAsync(ct);
    }

    // The same answer a non-admin gets from the rest of the admin surface: it does not exist.
    private void RequireAdmin()
    {
        if (!currentUser.IsAdmin) throw new NotFoundException("Demo engagement", "admin");
    }
}
