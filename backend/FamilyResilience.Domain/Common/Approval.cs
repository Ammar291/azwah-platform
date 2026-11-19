namespace FamilyResilience.Domain.Common;

/// <summary>
/// Who approved a piece of content or a rule set, when, and against which source document. Required before any
/// instrument version, rule set or consent policy becomes usable.
/// </summary>
public sealed record Approval
{
    public const int MaxApprovedByLength = 200;
    public const int MaxSourceLength = 300;

    private Approval(string approvedBy, DateTimeOffset approvedAt, string source)
    {
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        Source = source;
    }

    /// <summary>The approver's token subject. Never a name.</summary>
    public string ApprovedBy { get; }

    public DateTimeOffset ApprovedAt { get; }

    public string Source { get; }

    public static Result<Approval> Create(string? approvedBy, DateTimeOffset approvedAt, string? source)
    {
        if (string.IsNullOrWhiteSpace(approvedBy) || approvedBy.Length > MaxApprovedByLength)
        {
            return Result.Failure<Approval>(CommonErrors.ApproverInvalid);
        }

        if (!UtcTime.IsUtc(approvedAt))
        {
            return Result.Failure<Approval>(CommonErrors.TimeNotUtc);
        }

        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxSourceLength)
        {
            return Result.Failure<Approval>(CommonErrors.ApprovalSourceInvalid);
        }

        return Result.Success(new Approval(approvedBy, approvedAt, source));
    }
}
