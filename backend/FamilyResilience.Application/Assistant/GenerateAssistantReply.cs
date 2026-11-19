using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One assistant reply. First the person's latest message goes through the crisis phrase screen: a match starts
/// recording for staff at once (D-135, D-136), before the model is asked or even known to exist, so a missing or failing
/// model, or a person who closes the app, never loses the alert. The recording runs beside the rest of the turn, so a
/// slow database never holds the model back, and the reply waits for it before it returns. Without a configured model it
/// then answers <c>assistant.unavailable</c> and builds nothing. Otherwise it builds the caller's family context on the
/// server, runs the agent (<see cref="AssistantAgent"/>: the model, its tools, the guard, the safety check and the
/// memory) on the validated conversation under <see cref="AssistantLimits.ModelTimeout"/>, records a danger verdict of
/// the safety check as <c>ai_moderation</c> unless the phrase screen already recorded that same concern, and audits
/// <c>assistant.reply.generated</c> (the household, the outcome, no text) each time the context is sent. Any model
/// failure, fault or time-out is <c>assistant.model_failed</c>; only the caller's own cancellation propagates. When
/// either detector fired the outcome carries <c>crisis_support</c> (D-138), success or failure, even if recording
/// failed, and a reply offers SOS first; a server fault after a detector fired (the context, the caller lookup, the
/// audit) is caught and answered <see cref="AssistantErrors.Faulted"/> with the notice, logging only the exception's
/// type, because the notice must never depend on the database. Without a signal a fault propagates as before.
/// Nothing is stored or logged, and an alert holds codes only.
/// </summary>
public sealed partial class GenerateAssistantReply(
    IAssistantModel model,
    AssistantAgent agent,
    FamilyContextBuilder contexts,
    ICallerAccess access,
    CrisisSignalScreen screen,
    ISafetySignalRecorder recorder,
    IAuditLog auditLog,
    AssistantLimits limits,
    AssistantMetrics metrics,
    TimeProvider timeProvider,
    ILogger<GenerateAssistantReply> logger)
{
    public const string AuditAction = "assistant.reply.generated";

    public async Task<AssistantReplyOutcome> HandleAsync(AssistantReplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = request.Language!;
        var match = screen.Screen(request.Messages![^1].Text);

        // Never the request's token: a person who closes the app still raises the alert; the recorder bounds itself.
        var phraseRecording = match is null
            ? Task.CompletedTask
            : recorder.RecordForCallerAsync(
                new SafetySignal(SafetyAlertSource.AssistantChat, match.Concern, SafetyDetector.PhraseScreen, match.RuleCode, language),
                CancellationToken.None);
        var moderationRecording = Task.CompletedTask;
        var notice = match is null ? null : SafetyNotices.CrisisSupport;
        try
        {
            if (!model.IsConfigured)
            {
                metrics.ReplyCompleted("unavailable", TimeSpan.Zero);
                return Failed(AssistantErrors.Unavailable, notice);
            }

            var started = timeProvider.GetTimestamp();
            var context = await contexts.BuildAsync(language, cancellationToken);
            var messages = request.Messages!.Select(message => message.ToMessage()).ToList();
            var result = await RunAsync(
                new AssistantAgentRequest(context, language, AssistantMemory.Normalise(request.Memory), messages), cancellationToken);
            if (result is not null && result.Safety.IsDanger())
            {
                // A concern the phrase screen already recorded for this message is not counted twice.
                var moderated = result.Safety.Concern();
                if (match?.Concern != moderated)
                {
                    moderationRecording = recorder.RecordForCallerAsync(
                        new SafetySignal(SafetyAlertSource.AssistantChat, moderated, SafetyDetector.AiModeration, null, language),
                        CancellationToken.None);
                }

                notice = SafetyNotices.CrisisSupport;
            }

            var household = (await access.GetCallerAsync(cancellationToken))?.Household?.Id.Value.ToString();
            var recorded = await auditLog.RecordAsync(
                new AuditRecord(AuditAction, "household", household, result is null ? AuditOutcome.Failed : AuditOutcome.Succeeded),
                cancellationToken);
            metrics.ReplyCompleted(result is null ? "model_failed" : "succeeded", timeProvider.GetElapsedTime(started));
            if (recorded.IsFailure)
            {
                return Failed(recorded.Error!, notice);
            }

            if (result is null)
            {
                return Failed(AssistantErrors.ModelFailed, notice);
            }

            var actions = notice is null ? result.Actions : AssistantScreens.Order([AssistantScreens.Sos, .. result.Actions]);
            return new AssistantReplyOutcome(
                Result.Success(new AssistantReplyResponse(result.Reply, timeProvider.GetUtcNow(), actions, result.Sources, result.Memory, notice)),
                notice);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (notice is not null)
        {
            // The notice must never depend on the database (D-138). Only the type is logged: a message can quote rows.
            LogFaulted(logger, exception.GetType().Name);
            return Failed(AssistantErrors.Faulted, notice);
        }
        finally
        {
            // The recordings never throw for a token they were not given; waiting keeps them inside the request.
            await Task.WhenAll(phraseRecording, moderationRecording);
        }
    }

    private static AssistantReplyOutcome Failed(Error error, string? notice) =>
        new(Result.Failure<AssistantReplyResponse>(error), notice);

    /// <summary>The finished turn, or null when the model failed, threw, answered nothing or ran out of time.</summary>
    private async Task<AssistantAgentResult?> RunAsync(AssistantAgentRequest agentRequest, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.ModelTimeout);
        try
        {
            // WaitAsync bounds the turn even when an adapter ignores its token.
            return await agent.RunAsync(agentRequest, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Broad on purpose: a time-out, a provider fault or an adapter bug all mean the same to the person (502).
            // The exception is dropped unlogged, because an adapter's message could quote the request.
            return null;
        }
    }

    [LoggerMessage(EventId = 1700, Level = LogLevel.Error, Message = "An assistant reply stopped on a fault of type {ExceptionType} after a crisis signal.")]
    private static partial void LogFaulted(ILogger logger, string exceptionType);
}
