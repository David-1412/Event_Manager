using SportMeet.Application.Events;
using SportMeet.Application.Ingestion;

namespace SportMeet.Application.Imports;

public static class ExtractedEventMapping
{
    /// <summary>
    /// The create form's body, from an extraction. Model chatter (confidence, reasoning,
    /// missing fields) is dropped here, so what the user posts back is exactly a
    /// <c>CreateEventDto</c> and nothing model-authored reaches the event service.
    /// Coordinates are passed in because the extractor cannot produce them; they come from
    /// the geocoder.
    /// </summary>
    public static CreateEventDto ToPayload(
        this ExtractedEvent e, string defaultTimezone, double? latitude, double? longitude) => new()
    {
        Title = e.Title,
        Tags = e.Tags,
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        Timezone = string.IsNullOrWhiteSpace(e.Timezone) ? defaultTimezone : e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = e.Latitude ?? latitude,
        Longitude = e.Longitude ?? longitude,
        MaxParticipants = e.MaxParticipants,
        Cost = e.Cost,
        Description = e.Description,
        SkillLevel = e.SkillLevel,
    };
}
