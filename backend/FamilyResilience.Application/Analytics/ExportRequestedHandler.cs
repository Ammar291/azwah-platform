using System.Text.Json;
using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Consumes <c>analytics.export_requested.v1</c> by running <see cref="ProcessExport"/>. An unexpected failure is
/// thrown, so the outbox records the attempt and retries it; the job's own attempt count bounds the work.
/// </summary>
public sealed class ExportRequestedHandler(ProcessExport processExport) : IOutboxMessageHandler
{
    public const string MessageType = "analytics.export_requested.v1";

    public string Type => MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var payload = JsonDocument.Parse(message.Payload);
        var exportJobId = payload.RootElement.GetProperty("exportJobId").GetGuid();
        var processed = await processExport.HandleAsync(exportJobId, cancellationToken);
        if (processed.IsFailure)
        {
            throw new InvalidOperationException($"Export processing failed with {processed.Error!.Code}.");
        }
    }
}
