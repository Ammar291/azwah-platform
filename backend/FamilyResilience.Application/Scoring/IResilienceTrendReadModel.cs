using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Scoring;

/// <summary>The household assessment's submitted check-ins, for the family's trend chart (D-132).</summary>
public interface IResilienceTrendReadModel
{
    /// <summary>
    /// At most <paramref name="take"/> submitted sessions of a household-assessment instrument answered by these members,
    /// newest first, each with its latest score result. Family-voices modules are never included: the two instruments
    /// are never combined.
    /// </summary>
    Task<IReadOnlyList<ResilienceCheckIn>> ListCheckInsAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int take, CancellationToken cancellationToken = default);
}
