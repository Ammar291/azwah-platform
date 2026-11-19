using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Consent;

/// <summary>
/// A version of the consent text for one purpose. The text itself lives in the reviewed documents the references
/// point to; nothing can be granted against a policy until it is approved, and an approved policy never changes.
/// </summary>
public sealed class ConsentPolicyVersion : AggregateRoot<ConsentPolicyVersionId>
{
    public const int MaxDocumentReferenceLength = 300;

    private ConsentPolicyVersion(ConsentPolicyVersionId id)
        : base(id)
    {
    }

    public ConsentType ConsentType { get; private set; }

    public int VersionNumber { get; private set; }

    public ConsentPolicyStatus Status { get; private set; }

    public string DocumentReferenceAr { get; private set; } = null!;

    public string DocumentReferenceEn { get; private set; } = null!;

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ApprovalSource { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<ConsentPolicyVersion> CreateDraft(
        ConsentType consentType,
        int versionNumber,
        string documentReferenceAr,
        string documentReferenceEn,
        DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<ConsentPolicyVersion>(CommonErrors.TimeNotUtc);
        }

        if (!Enum.IsDefined(consentType))
        {
            return Result.Failure<ConsentPolicyVersion>(ConsentErrors.ConsentTypeInvalid);
        }

        if (versionNumber < 1)
        {
            return Result.Failure<ConsentPolicyVersion>(ConsentErrors.VersionNumberInvalid);
        }

        if (!IsDocumentReference(documentReferenceAr) || !IsDocumentReference(documentReferenceEn))
        {
            return Result.Failure<ConsentPolicyVersion>(ConsentErrors.DocumentReferenceInvalid);
        }

        return Result.Success(new ConsentPolicyVersion(ConsentPolicyVersionId.New())
        {
            ConsentType = consentType,
            VersionNumber = versionNumber,
            Status = ConsentPolicyStatus.Draft,
            DocumentReferenceAr = documentReferenceAr.Trim(),
            DocumentReferenceEn = documentReferenceEn.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    public Result Approve(Approval approval, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != ConsentPolicyStatus.Draft)
        {
            return Result.Failure(ConsentErrors.PolicyNotDraft);
        }

        Status = ConsentPolicyStatus.Approved;
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

        if (Status != ConsentPolicyStatus.Approved)
        {
            return Result.Failure(ConsentErrors.PolicyNotApproved);
        }

        Status = ConsentPolicyStatus.Retired;
        RetiredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private static bool IsDocumentReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxDocumentReferenceLength;
}
