using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assignments;

public interface IWaveRepository
{
    void Add(Wave wave);

    Task<Wave?> GetAsync(WaveId id, CancellationToken cancellationToken = default);

    Task<Wave?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Open waves whose window contains <paramref name="at"/>, by opening time. Normally one at most.</summary>
    Task<IReadOnlyList<Wave>> ListOpenAtAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>A page of waves, newest opening first, read without tracking.</summary>
    Task<IReadOnlyList<Wave>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
