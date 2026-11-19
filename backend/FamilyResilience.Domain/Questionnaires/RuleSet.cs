using System.Text.Json;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>
/// Versioned scoring, eligibility or referral rules for one instrument version. The definition's schema belongs to
/// the engine phases; this type only guarantees it is a JSON object and that nothing unapproved is ever used.
/// </summary>
public sealed class RuleSet : AggregateRoot<RuleSetId>
{
    public const int MaxDefinitionLength = 100_000;

    private RuleSet(RuleSetId id)
        : base(id)
    {
    }

    public RuleSetKind Kind { get; private set; }

    public string Code { get; private set; } = null!;

    public int VersionNumber { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public RuleSetStatus Status { get; private set; }

    public string Definition { get; private set; } = null!;

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ApprovalSource { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<RuleSet> Propose(
        RuleSetKind kind,
        string code,
        int versionNumber,
        InstrumentVersion instrumentVersion,
        string definition,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrumentVersion);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<RuleSet>(CommonErrors.TimeNotUtc);
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<RuleSet>(QuestionnaireErrors.RuleSetKindInvalid);
        }

        if (!Codes.IsSnakeCode(code))
        {
            return Result.Failure<RuleSet>(QuestionnaireErrors.RuleSetCodeInvalid);
        }

        if (versionNumber < 1)
        {
            return Result.Failure<RuleSet>(QuestionnaireErrors.VersionNumberInvalid);
        }

        if (!IsJsonObject(definition))
        {
            return Result.Failure<RuleSet>(QuestionnaireErrors.RuleSetDefinitionInvalid);
        }

        return Result.Success(new RuleSet(RuleSetId.New())
        {
            Kind = kind,
            Code = code,
            VersionNumber = versionNumber,
            InstrumentVersionId = instrumentVersion.Id,
            Status = RuleSetStatus.Proposed,
            Definition = definition,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    public Result Approve(Approval approval, InstrumentVersion instrumentVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(instrumentVersion);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != RuleSetStatus.Proposed)
        {
            return Result.Failure(QuestionnaireErrors.RuleSetNotProposed);
        }

        if (instrumentVersion.Id != InstrumentVersionId)
        {
            return Result.Failure(QuestionnaireErrors.RuleSetVersionMismatch);
        }

        if (instrumentVersion.Status != InstrumentVersionStatus.Approved)
        {
            return Result.Failure(QuestionnaireErrors.VersionNotApproved);
        }

        Status = RuleSetStatus.Approved;
        ApprovedBy = approval.ApprovedBy;
        ApprovedAt = approval.ApprovedAt;
        ApprovalSource = approval.Source;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Retire(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != RuleSetStatus.Approved)
        {
            return Result.Failure(QuestionnaireErrors.RuleSetNotApproved);
        }

        Status = RuleSetStatus.Retired;
        RetiredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private static bool IsJsonObject(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition) || definition.Length > MaxDefinitionLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(definition);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
