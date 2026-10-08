namespace SportMeet.Application.Imports;

public interface IImportService
{
    /// <summary>
    /// Turns user-provided content into a pre-filled event proposal and records it.
    /// Throws <see cref="ImportException"/> for an input the user can fix.
    /// </summary>
    Task<ImportDraftDto> ImportAsync(ImportInput input, CancellationToken ct = default);

    /// <summary>
    /// Records how long publishing took and, for an import, how the published event
    /// differs from the proposal. Idempotent per event. The published values are read back
    /// from the stored event, never taken from the caller.
    /// </summary>
    Task RecordPublishAsync(PublishMetricsRequest request, CancellationToken ct = default);
}
