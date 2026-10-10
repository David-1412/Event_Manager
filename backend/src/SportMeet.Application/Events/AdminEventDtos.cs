using SportMeet.Domain.Enums;
using SportMeet.Domain.Entities;
using SportMeet.Application.Common;

namespace SportMeet.Application.Events;

/// <summary>Time relationship of an event to the current instant.</summary>
public enum AdminEventTimeFrame
{
    Past,
    Current,
    Future,
}

/// <summary>Filters and pagination for the administrator event list.</summary>
public sealed record AdminEventQuery(
    string? Search,
    AdminEventTimeFrame? TimeFrame,
    EventStatus? Status,
    int Page,
    int PageSize);

/// <summary>A page of events visible to an administrator.</summary>
public sealed record AdminEventPage(
    IReadOnlyList<AdminEventDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>An event row with host details for administrator management.</summary>
public sealed record AdminEventDto(
    Guid Id,
    string Title,
    Guid HostId,
    string HostName,
    string? Description,
    string VenueName,
    string? Address,
    string? ThumbnailUrl,
    double Latitude,
    double Longitude,
    string Timezone,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int MaxParticipants,
    SkillLevel? SkillLevel,
    decimal? Cost,
    EventStatus Status,
    EventVisibility Visibility,
    IReadOnlyList<string> Tags);

/// <summary>Replacement event fields supplied by an administrator.</summary>
public sealed record UpdateAdminEventRequest
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string VenueName { get; init; }
    public string? Address { get; init; }
    public string? ThumbnailUrl { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required string Timezone { get; init; }
    public required DateTimeOffset StartAt { get; init; }
    public required DateTimeOffset EndAt { get; init; }
    public required int MaxParticipants { get; init; }
    public SkillLevel? SkillLevel { get; init; }
    public decimal? Cost { get; init; }
    public required EventStatus Status { get; init; }
    public required EventVisibility Visibility { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

internal static class EventUpdateRules
{
    public static void Validate(UpdateAdminEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 3 or > 120)
            throw new DomainRuleException("Title must be between 3 and 120 characters.");
        if (request.Description?.Length > 2000)
            throw new DomainRuleException("Description must be at most 2000 characters.");
        if (string.IsNullOrWhiteSpace(request.VenueName) || request.VenueName.Trim().Length > 120)
            throw new DomainRuleException("Venue is required and must be at most 120 characters.");
        if (request.Address?.Length > 400 || request.ThumbnailUrl?.Length > 400)
            throw new DomainRuleException("Address and thumbnail URL must be at most 400 characters.");
        if (string.IsNullOrWhiteSpace(request.Timezone) || request.Timezone.Length > 64)
            throw new DomainRuleException("Timezone is required and must be at most 64 characters.");
        if (!IsKnownTimeZone(request.Timezone))
            throw new DomainRuleException("Unknown timezone.");
        if (!Event.HasValidTimeRange(request.StartAt, request.EndAt))
            throw new DomainRuleException("Finish must be after the start.");
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new DomainRuleException("Location coordinates are invalid.");
        if (request.MaxParticipants is < 2 or > 50)
            throw new DomainRuleException("Maximum participants must be between 2 and 50.");
        if (request.Cost is < 0 or > 1000)
            throw new DomainRuleException("Cost must be between 0 and 1000.");
        if (!Enum.IsDefined(request.Status) || !Enum.IsDefined(request.Visibility))
            throw new DomainRuleException("Event status or visibility is invalid.");
        if (request.Tags is null
            || request.Tags.Count > TagNormalizer.MaxTagsPerEvent
            || !TagNormalizer.HasOnlyValid(request.Tags))
            throw new DomainRuleException("Use up to five valid event tags.");
    }

    public static void Apply(Event entity, UpdateAdminEventRequest request, DateTimeOffset now)
    {
        entity.Title = request.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.VenueName = request.VenueName.Trim();
        entity.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        entity.ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? null : request.ThumbnailUrl.Trim();
        entity.Lat = request.Latitude;
        entity.Lng = request.Longitude;
        entity.Timezone = request.Timezone.Trim();
        entity.StartAt = request.StartAt;
        entity.EndAt = request.EndAt;
        entity.MaxParticipants = request.MaxParticipants;
        entity.SkillLevel = request.SkillLevel;
        entity.Cost = request.Cost;
        entity.Status = request.Status;
        entity.Visibility = request.Visibility;
        entity.UpdatedAt = now;
    }

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
