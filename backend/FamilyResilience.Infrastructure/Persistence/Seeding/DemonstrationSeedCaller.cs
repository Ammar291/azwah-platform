using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// Who the demonstration seed acts as: a subject no identity provider issues, so every approval and audit event it
/// records names it (D-140). It holds no role and is never a participant.
/// </summary>
internal sealed class DemonstrationSeedCaller : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string? SubjectId => DemonstrationDataSeeder.SeedSubject;

    public IReadOnlyCollection<string> Roles { get; } = [];

    public bool IsSystem => false;
}
