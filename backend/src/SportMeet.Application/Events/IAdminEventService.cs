namespace SportMeet.Application.Events;

public interface IAdminEventService
{
    Task<AdminEventPage> ListAsync(AdminEventQuery query, CancellationToken ct = default);
    Task<AdminEventDto> UpdateAsync(Guid id, UpdateAdminEventRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
