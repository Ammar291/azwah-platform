namespace FamilyResilience.Application.SupportResources;

/// <summary>Where the review is recorded and when the entry must be reviewed again (after the approval, within 366 days).</summary>
public sealed record ApproveSupportResourceRequest(string? ApprovalSource, DateTimeOffset? ReviewDueAt);
