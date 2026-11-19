using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public interface IInstrumentRepository
{
    void Add(Instrument instrument);

    Task<Instrument?> GetAsync(InstrumentId id, CancellationToken cancellationToken = default);

    Task<Instrument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Instrument>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Every active instrument, by code.</summary>
    Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken cancellationToken = default);
}
