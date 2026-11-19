using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Application.Identity;

public interface IRoleSnapshotRepository
{
    void Add(RoleSnapshot snapshot);

    /// <summary>The subject's snapshot, tracked so a change is saved with its concurrency check.</summary>
    Task<RoleSnapshot?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default);
}
