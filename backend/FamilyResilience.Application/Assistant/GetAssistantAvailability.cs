using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>Whether the assistant, the consultant and the score report are switched on, so the app can say so before anyone asks.</summary>
public sealed class GetAssistantAvailability(IAssistantModel assistant, IVoiceConsultantModel consultant, IScoreReportModel scoreReport)
{
    public Task<Result<AssistantAvailabilityResponse>> HandleAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new AssistantAvailabilityResponse(assistant.IsConfigured, consultant.IsConfigured, scoreReport.IsConfigured)));
}
