using System.Text.Json;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Consumes <c>scoring.score_result_recorded.v1</c> (D-136): a complete result whose tier <see cref="ScoreAlertSettings"/>
/// lists under the code of the scoring rule set that produced it raises a <c>low_resilience_score</c> alert about the
/// assessed member and their household, from the household assessment, detected by <c>score_band</c> with the tier as the
/// signal code. With <see cref="ScoreAlertSettings.Enabled"/> off it completes every message and records nothing. The
/// recorder skips a session that already has an alert, so a second delivery adds nothing. A result that no longer exists
/// (its household was erased) raises nothing. A malformed payload, and a recording that failed, are thrown, so the outbox
/// records the attempt and retries it within its bounded attempts; the message names no ID.
/// </summary>
public sealed class ScoreResultRecordedSafetyHandler(
    IScoreResultFacts facts, ScoreAlertSettings settings, ISafetySignalRecorder recorder) : IOutboxMessageHandler
{
    public const string MessageType = "scoring.score_result_recorded.v1";

    public string Type => MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!settings.Enabled)
        {
            return;
        }

        using var payload = JsonDocument.Parse(message.Payload);
        var scoreResultId = ScoreResultId.From(payload.RootElement.GetProperty("scoreResultId").GetGuid());
        var result = await facts.GetAsync(scoreResultId, cancellationToken);
        if (result is not { Status: ScoreStatus.Complete, TierCode: { } tier }
            || !settings.Tiers.TryGetValue(result.RuleSetCode, out var alerting)
            || !alerting.Contains(tier, StringComparer.Ordinal))
        {
            return;
        }

        // Repeated folds into an unresolved alert and cannot happen for a session; like Raised and Skipped, it completes.
        var outcome = await recorder.RecordForSessionAsync(
            new SafetyAlertSubject(null, result.HouseholdId, result.MemberId),
            result.SessionId,
            new SafetySignal(SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand, tier, null),
            cancellationToken);
        if (outcome == SafetyAlertOutcome.Failed)
        {
            throw new InvalidOperationException("The low-score safety alert could not be recorded.");
        }
    }
}
