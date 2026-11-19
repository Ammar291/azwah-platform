namespace FamilyResilience.Application.Households;

/// <summary>
/// What one run of <see cref="ApplyRetention"/> deleted. <see cref="ErasureNotConfigured"/>: a household period is set
/// but <c>Erasure:ConsentRecords</c> is not, so no household was erased. <see cref="Failures"/> holds error codes only.
/// </summary>
public sealed record RetentionReport(
    int HouseholdsErased,
    int OutboxMessagesDeleted,
    int ExportJobsDeleted,
    bool ErasureNotConfigured,
    IReadOnlyList<string> Failures)
{
    public static RetentionReport Nothing { get; } = new(0, 0, 0, false, []);

    public bool Equals(RetentionReport? other) =>
        other is not null
        && (HouseholdsErased, OutboxMessagesDeleted, ExportJobsDeleted, ErasureNotConfigured)
            == (other.HouseholdsErased, other.OutboxMessagesDeleted, other.ExportJobsDeleted, other.ErasureNotConfigured)
        && Failures.SequenceEqual(other.Failures, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(HouseholdsErased, OutboxMessagesDeleted, ExportJobsDeleted, ErasureNotConfigured);
}
