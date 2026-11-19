using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Application.Identity;

public interface IPlatformUserRepository
{
    void Add(PlatformUser user);

    Task<PlatformUser?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>When the subject's platform user was created, or null when there is none. Read only, untracked.</summary>
    Task<DateTimeOffset?> FindRegisteredAtAsync(string subject, CancellationToken cancellationToken = default);
}
