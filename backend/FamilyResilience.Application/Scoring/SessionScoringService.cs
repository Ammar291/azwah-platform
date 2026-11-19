using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// Scores a submitted session with the one approved scoring rule set that covers its scope. The calculation runs on a
/// snapshot without database access. A new result is staged with its audit event and moves the session to Scored; the
/// caller commits. With no approved rule set the result is <see cref="ScoringErrors.PolicyNotApproved"/>.
/// </summary>
public sealed class SessionScoringService(
    IInstrumentVersionRepository versions,
    IRuleSetRepository ruleSets,
    IScoreResultRepository scoreResults,
    ScoringStrategyCatalog catalog,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<ScoringAttempt>> ScoreAsync(AssessmentSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Status is not (SessionStatus.Submitted or SessionStatus.Scored))
        {
            return Result.Failure<ScoringAttempt>(ScoringErrors.SessionNotSubmitted);
        }

        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var sectionCode = ApprovedScoringPolicy.SectionCodeOf(session, version);

        var policy = await ApprovedScoringPolicy.FindAsync(ruleSets, version, sectionCode, cancellationToken);
        if (policy.IsFailure)
        {
            return Result.Failure<ScoringAttempt>(policy.Error!);
        }

        var (ruleSet, definition) = policy.Value;
        if (await scoreResults.GetForSessionAndRuleSetAsync(session.Id, ruleSet.Id, cancellationToken) is { } existing)
        {
            return Result.Success(new ScoringAttempt(existing, Created: false, sectionCode));
        }

        var computation = Compute(session, version, sectionCode, definition);
        if (computation.IsFailure)
        {
            return Result.Failure<ScoringAttempt>(computation.Error!);
        }

        var recorded = ScoreResult.Record(session, ruleSet, computation.Value, timeProvider.GetUtcNow());
        if (recorded.IsFailure)
        {
            return Result.Failure<ScoringAttempt>(recorded.Error!);
        }

        scoreResults.Add(recorded.Value);
        var staged = auditLog.Stage(new AuditRecord(
            "scoring.score_result.recorded", "score_result", recorded.Value.Id.Value.ToString(), AuditOutcome.Succeeded));
        return staged.IsFailure
            ? Result.Failure<ScoringAttempt>(staged.Error!)
            : Result.Success(new ScoringAttempt(recorded.Value, Created: true, sectionCode));
    }

    private Result<ScoringComputation> Compute(
        AssessmentSession session, InstrumentVersion version, string? sectionCode, ScoringDefinition definition)
    {
        var strategy = catalog.Resolve(definition.PolicyCode, definition.AlgorithmVersion);
        if (strategy.IsFailure)
        {
            return Result.Failure<ScoringComputation>(strategy.Error!);
        }

        var content = ScoringContent.From(version, sectionCode);
        if (content.IsFailure)
        {
            return Result.Failure<ScoringComputation>(content.Error!);
        }

        var parameters = strategy.Value.ValidateParameters(definition.Parameters, content.Value);
        if (parameters.IsFailure)
        {
            return Result.Failure<ScoringComputation>(parameters.Error!);
        }

        var responses = session.Responses
            .Select(response => new ScoringResponse(response.InstrumentItemId, response.ResponseOptionId, response.RawValue))
            .ToList();
        return strategy.Value.Score(new ScoringInput(content.Value, parameters.Value, responses));
    }
}
