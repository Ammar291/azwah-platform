using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Opens a live consultant session for the caller. Without a configured model it answers <c>assistant.unavailable</c>
/// and builds nothing. Otherwise it claims the subject's one slot (ending their earlier live session), builds the
/// family context on the server while <see cref="ConsultantHistoryScreen"/> holds back earlier lines that would trip the
/// provider's filter (replaced by <see cref="AssistantPrompt.WithheldHistoryNote"/>, D-141), both while the model
/// connects, opens the call's first model session under <see cref="AssistantLimits.ModelTimeout"/> with the validated
/// memory, the kept messages and the agent's
/// tools (D-123, D-124) as a <see cref="ContinuingConsultantSession"/>, so the call outlives a session the provider's
/// filter stops, and audits <c>assistant.consultant.started</c> (the household and the outcome, no content). A model that
/// fails, throws or runs out of time ends the start with <c>assistant.model_failed</c>. The relay of audio and events
/// belongs to the API.
/// </summary>
public sealed class StartConsultantSession(
    IVoiceConsultantModel model,
    FamilyContextBuilder contexts,
    LiveConsultantSessions live,
    ICurrentUser currentUser,
    ICallerAccess access,
    IAuditLog auditLog,
    IAssistantToolRunner tools,
    AssistantLimits limits,
    ConsultantHistoryScreen historyScreen,
    IConsultantCrisisReply crisisReply,
    AssistantMetrics metrics,
    ILogger<ContinuingConsultantSession> sessionLogger)
{
    public const string AuditAction = "assistant.consultant.started";

    public async Task<Result<LiveConsultation>> HandleAsync(
        ConsultantStartRequest request, ConsultantStartMessage start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(start);
        if (!model.IsConfigured)
        {
            return Result.Failure<LiveConsultation>(AssistantErrors.Unavailable);
        }

        var subject = currentUser.SubjectId ?? throw new InvalidOperationException("A live session requires a signed-in caller.");
        var language = request.Language!;
        var slot = live.Claim(subject);
        try
        {
            // The model connects while the request is prepared, so neither the context nor the check of the earlier
            // lines adds a wait of its own.
            var preparing = PrepareAsync(language, start, cancellationToken);
            var session = await ContinuingConsultantSession.OpenAsync(
                model, preparing, crisisReply, limits, metrics, sessionLogger, cancellationToken);
            if (session is null)
            {
                // The open may have failed before the request was ready. The preparation shares this request's database
                // context, so it finishes before anything else reads, and a fault of its own (building the context)
                // propagates as it did before.
                await preparing;
            }

            var household = (await access.GetCallerAsync(cancellationToken))?.Household?.Id.Value.ToString();
            var recorded = await auditLog.RecordAsync(
                new AuditRecord(AuditAction, "household", household, session is null ? AuditOutcome.Failed : AuditOutcome.Succeeded),
                cancellationToken);
            if (session is null)
            {
                slot.Dispose();
                return Result.Failure<LiveConsultation>(AssistantErrors.ModelFailed);
            }

            if (recorded.IsFailure)
            {
                await session.DisposeAsync();
                slot.Dispose();
                return Result.Failure<LiveConsultation>(recorded.Error!);
            }

            return Result.Success(new LiveConsultation(session, slot));
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    /// <summary>The first session's request: the family context, with the earlier lines checked meanwhile.</summary>
    private async Task<VoiceConsultantRequest> PrepareAsync(string language, ConsultantStartMessage start, CancellationToken cancellationToken)
    {
        var screening = historyScreen.ScreenAsync((start.Messages ?? []).Select(message => message.ToMessage()).ToList(), cancellationToken);
        var context = await contexts.BuildAsync(language, cancellationToken);
        var history = await screening;
        return new VoiceConsultantRequest(context, language, AssistantMemory.Normalise(start.Memory), history.Kept, tools)
        {
            Note = history.Withheld ? AssistantPrompt.WithheldHistoryNote : null,
        };
    }
}
