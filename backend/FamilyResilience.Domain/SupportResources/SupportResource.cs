using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.SupportResources;

/// <summary>
/// One version of an entry in the approved support-resource directory. Entered by an administrator, approved with a
/// named approver and a review date, and findable only while approved and before that date. An approved version never
/// changes: edits make a new draft version of the same code.
/// </summary>
public sealed class SupportResource : AggregateRoot<SupportResourceId>
{
    /// <summary>The longest an approval may stand before the entry must be reviewed again.</summary>
    public static readonly TimeSpan MaxReviewInterval = TimeSpan.FromDays(366);

    private readonly List<string> _categories = [];
    private readonly List<string> _audiences = [];
    private readonly List<string> _serviceLanguages = [];

    private SupportResource(SupportResourceId id)
        : base(id)
    {
    }

    public string Code { get; private set; } = null!;

    public int VersionNumber { get; private set; }

    public SupportResourceStatus Status { get; private set; }

    public SupportResourceVisibility Visibility { get; private set; }

    public LocalizedText Name { get; private set; } = null!;

    public LocalizedText Summary { get; private set; } = null!;

    public LocalizedText ProviderName { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    public IReadOnlyList<string> Categories => _categories.AsReadOnly();

    public IReadOnlyList<string> Audiences => _audiences.AsReadOnly();

    public IReadOnlyList<string> ServiceLanguages => _serviceLanguages.AsReadOnly();

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ApprovalSource { get; private set; }

    public DateTimeOffset? ReviewDueAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<SupportResource> CreateDraft(
        string code, int versionNumber, SupportResourceContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<SupportResource>(CommonErrors.TimeNotUtc);
        }

        if (!Codes.IsSnakeCode(code))
        {
            return Result.Failure<SupportResource>(SupportResourceErrors.CodeInvalid);
        }

        if (versionNumber < 1)
        {
            return Result.Failure<SupportResource>(SupportResourceErrors.VersionNumberInvalid);
        }

        var resource = new SupportResource(SupportResourceId.New())
        {
            Code = code,
            VersionNumber = versionNumber,
            Status = SupportResourceStatus.Draft,
            CreatedAt = now,
        };
        resource.Apply(content, now);
        return Result.Success(resource);
    }

    public Result UpdateDraft(SupportResourceContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != SupportResourceStatus.Draft)
        {
            return Result.Failure(SupportResourceErrors.NotDraft);
        }

        Apply(content, now);
        return Result.Success();
    }

    public Result Approve(Approval approval, DateTimeOffset reviewDueAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != SupportResourceStatus.Draft)
        {
            return Result.Failure(SupportResourceErrors.NotDraft);
        }

        if (!Name.IsComplete || !Summary.IsComplete || !ProviderName.IsComplete)
        {
            return Result.Failure(SupportResourceErrors.ArabicTextMissing);
        }

        if (!UtcTime.IsUtc(reviewDueAt)
            || reviewDueAt <= approval.ApprovedAt
            || reviewDueAt > approval.ApprovedAt + MaxReviewInterval)
        {
            return Result.Failure(SupportResourceErrors.ReviewDueInvalid);
        }

        Status = SupportResourceStatus.Approved;
        ApprovedBy = approval.ApprovedBy;
        ApprovedAt = approval.ApprovedAt;
        ApprovalSource = approval.Source;
        ReviewDueAt = reviewDueAt;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Retire(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != SupportResourceStatus.Approved)
        {
            return Result.Failure(SupportResourceErrors.NotApproved);
        }

        Status = SupportResourceStatus.Retired;
        RetiredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>True while the version is approved and its review date has not been reached.</summary>
    public bool IsDiscoverableAt(DateTimeOffset now) =>
        Status == SupportResourceStatus.Approved && ReviewDueAt > now;

    private void Apply(SupportResourceContent content, DateTimeOffset now)
    {
        Visibility = content.Visibility;
        Name = content.Name;
        Summary = content.Summary;
        ProviderName = content.ProviderName;
        Url = content.Url;
        _categories.Clear();
        _categories.AddRange(content.Categories);
        _audiences.Clear();
        _audiences.AddRange(content.Audiences);
        _serviceLanguages.Clear();
        _serviceLanguages.AddRange(content.ServiceLanguages);
        UpdatedAt = now;
    }
}
