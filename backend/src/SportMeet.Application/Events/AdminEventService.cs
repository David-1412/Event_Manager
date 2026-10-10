using SportMeet.Application.Common;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

public sealed class AdminEventService(
    IAdminEventRepository events,
    ICurrentUser currentUser,
    INotificationRepository notifications) : IAdminEventService
{
    public async Task<AdminEventPage> ListAsync(AdminEventQuery query, CancellationToken ct = default)
    {
        RequireAdmin();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var (items, totalCount) = await events.QueryAdminAsync(
            query.Search?.Trim(),
            query.TimeFrame,
            query.Status,
            page,
            pageSize,
            DateTimeOffset.UtcNow,
            ct);
        return new AdminEventPage(items, page, pageSize, totalCount);
    }

    public async Task<AdminEventDto> UpdateAsync(
        Guid id,
        UpdateAdminEventRequest request,
        CancellationToken ct = default)
    {
        RequireAdmin();
        EventUpdateRules.Validate(request);

        var entity = await events.FindForAdminAsync(id, ct)
            ?? throw new NotFoundException("Event", id);
        var previousStatus = entity.Status;
        var now = DateTimeOffset.UtcNow;

        EventUpdateRules.Apply(entity, request, now);
        if (entity.Status == EventStatus.Cancelled && previousStatus != EventStatus.Cancelled)
            entity.CancelledAt = now;
        else if (entity.Status != EventStatus.Cancelled)
            entity.CancelledAt = null;

        await events.RunInTransactionAsync(async inner =>
        {
            await events.SaveChangesAsync(inner);
            await events.ReplaceEventTagsAsync(id, TagNormalizer.NormalizeMany(request.Tags), now, inner);

            if (previousStatus == EventStatus.PendingReview
                && entity.Status == EventStatus.Published
                && entity.Visibility == EventVisibility.Public)
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = entity.HostId,
                    Title = "Event approved",
                    Message = $"Your event '{entity.Title}' has been approved!",
                    Type = "EventApproved",
                    Link = $"/events/{entity.Id}",
                    Read = false,
                    CreatedAt = now,
                }, inner);
            }
        }, ct);

        return await GetDtoAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        RequireAdmin();
        var entity = await events.FindForAdminAsync(id, ct)
            ?? throw new NotFoundException("Event", id);
        await events.SoftDeleteAsync(entity, DateTimeOffset.UtcNow, ct);
    }

    private async Task<AdminEventDto> GetDtoAsync(Guid id, CancellationToken ct)
    {
        var entity = await events.FindForAdminAsync(id, ct)
            ?? throw new NotFoundException("Event", id);
        return new AdminEventDto(
            entity.Id,
            entity.Title,
            entity.HostId,
            entity.Host.Name,
            entity.Description,
            entity.VenueName,
            entity.Address,
            entity.ThumbnailUrl,
            entity.Lat,
            entity.Lng,
            entity.Timezone,
            entity.StartAt,
            entity.EndAt,
            entity.MaxParticipants,
            entity.SkillLevel,
            entity.Cost,
            entity.Status,
            entity.Visibility,
            entity.EventTags.Select(x => x.Tag.Name).OrderBy(x => x).ToList());
    }

    private void RequireAdmin()
    {
        _ = currentUser.UserId ?? throw new DomainRuleException("You must be signed in to manage events.");
        if (!currentUser.IsAdmin) throw new NotFoundException("Event", Guid.Empty);
    }

}
