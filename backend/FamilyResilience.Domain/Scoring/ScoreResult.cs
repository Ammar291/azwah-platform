using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// What one approved scoring rule set computed for one submitted session, with the policy code and algorithm version
/// that produced it. Never changed after it is recorded; scoring with another approved rule set adds another result.
/// An Incomplete result (for example too many non-substantive answers) has no total, tier or category scores.
/// </summary>
public sealed class ScoreResult : AggregateRoot<ScoreResultId>
{
    public const decimal MaxMagnitude = 999_999.999m;

    private readonly List<ScoreContribution> _contributions = [];
    private readonly List<ScoreCategoryScore> _categoryScores = [];

    private ScoreResult(ScoreResultId id)
        : base(id)
    {
    }

    public AssessmentSessionId AssessmentSessionId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public RuleSetId RuleSetId { get; private set; }

    public string ScoringPolicyCode { get; private set; } = null!;

    public int ScoringAlgorithmVersion { get; private set; }

    public ScoreStatus Status { get; private set; }

    public string? IncompleteReason { get; private set; }

    public decimal? TotalScore { get; private set; }

    public string? TierCode { get; private set; }

    public DateTimeOffset ComputedAt { get; private set; }

    public IReadOnlyList<ScoreContribution> Contributions => _contributions.AsReadOnly();

    public IReadOnlyList<ScoreCategoryScore> CategoryScores => _categoryScores.AsReadOnly();

    public static Result<ScoreResult> Record(
        AssessmentSession session, RuleSet ruleSet, ScoringComputation computation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(computation);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<ScoreResult>(CommonErrors.TimeNotUtc);
        }

        if (session.Status is not (SessionStatus.Submitted or SessionStatus.Scored))
        {
            return Result.Failure<ScoreResult>(ScoringErrors.SessionNotSubmitted);
        }

        if (ruleSet.Kind != RuleSetKind.Scoring)
        {
            return Result.Failure<ScoreResult>(ScoringErrors.RuleSetWrongKind);
        }

        if (ruleSet.Status != RuleSetStatus.Approved)
        {
            return Result.Failure<ScoreResult>(ScoringErrors.RuleSetNotApproved);
        }

        if (ruleSet.InstrumentVersionId != session.InstrumentVersionId)
        {
            return Result.Failure<ScoreResult>(ScoringErrors.RuleSetVersionMismatch);
        }

        var consistent = CheckComputation(computation);
        if (consistent.IsFailure)
        {
            return Result.Failure<ScoreResult>(consistent.Error!);
        }

        var matching = CheckContributions(session, computation.Contributions);
        if (matching.IsFailure)
        {
            return Result.Failure<ScoreResult>(matching.Error!);
        }

        var scored = session.MarkScored(now);
        if (scored.IsFailure)
        {
            return Result.Failure<ScoreResult>(scored.Error!);
        }

        var result = new ScoreResult(ScoreResultId.New())
        {
            AssessmentSessionId = session.Id,
            InstrumentVersionId = session.InstrumentVersionId,
            RuleSetId = ruleSet.Id,
            ScoringPolicyCode = computation.PolicyCode,
            ScoringAlgorithmVersion = computation.AlgorithmVersion,
            Status = computation.Status,
            IncompleteReason = computation.IncompleteReason,
            TotalScore = computation.TotalScore,
            TierCode = computation.TierCode,
            ComputedAt = now,
        };
        foreach (var contribution in computation.Contributions)
        {
            result._contributions.Add(ScoreContribution.Create(result.Id, session.InstrumentVersionId, contribution));
        }

        foreach (var category in computation.CategoryScores)
        {
            result._categoryScores.Add(ScoreCategoryScore.Create(result.Id, category));
        }

        result.Raise(new ScoreResultRecorded(result.Id, session.Id, ruleSet.Id, now));
        return Result.Success(result);
    }

    private static Result CheckComputation(ScoringComputation computation)
    {
        var incomplete = computation.Status == ScoreStatus.Incomplete;
        if (!Codes.IsSnakeCode(computation.PolicyCode)
            || computation.AlgorithmVersion < 1
            || !Enum.IsDefined(computation.Status)
            || incomplete != (computation.IncompleteReason is not null)
            || (computation.IncompleteReason is not null && !Codes.IsSnakeCode(computation.IncompleteReason))
            || (incomplete && (computation.TotalScore is not null || computation.TierCode is not null || computation.CategoryScores.Count > 0))
            || (computation.TierCode is not null && computation.TotalScore is null))
        {
            return Result.Failure(ScoringErrors.ComputationInconsistent);
        }

        if (computation.TierCode is not null && !Codes.IsSnakeCode(computation.TierCode))
        {
            return Result.Failure(ScoringErrors.TierCodeInvalid);
        }

        var categoryCodes = computation.CategoryScores.Select(category => category.CategoryCode).ToList();
        if (categoryCodes.Exists(code => !Codes.IsSnakeCode(code))
            || categoryCodes.Distinct(StringComparer.Ordinal).Count() != categoryCodes.Count
            || computation.CategoryScores.Any(category => category.ItemCount < 1))
        {
            return Result.Failure(ScoringErrors.ComputationInconsistent);
        }

        var storable = (computation.TotalScore is not { } total || IsStorable(total))
            && computation.CategoryScores.All(category => IsStorable(category.Score));
        return storable ? Result.Success() : Result.Failure(ScoringErrors.ScoreOutOfRange);
    }

    private static Result CheckContributions(AssessmentSession session, IReadOnlyList<ItemContribution> contributions)
    {
        if (contributions.Select(contribution => contribution.ItemId).Distinct().Count() != contributions.Count)
        {
            return Result.Failure(ScoringErrors.DuplicateContribution);
        }

        foreach (var contribution in contributions)
        {
            var response = session.FindResponse(contribution.ItemId);
            if (response is null)
            {
                return Result.Failure(ScoringErrors.ContributionForUnansweredItem);
            }

            if (response.RawValue != contribution.RawValue)
            {
                return Result.Failure(ScoringErrors.ContributionRawValueMismatch);
            }

            if (contribution.CalculatedValue is { } value && !IsStorable(value))
            {
                return Result.Failure(ScoringErrors.ScoreOutOfRange);
            }

            if (contribution.Category is not null && !Codes.IsSnakeCode(contribution.Category))
            {
                return Result.Failure(ScoringErrors.ComputationInconsistent);
            }
        }

        return Result.Success();
    }

    private static bool IsStorable(decimal value) => Math.Abs(value) <= MaxMagnitude && decimal.Round(value, 3) == value;
}
