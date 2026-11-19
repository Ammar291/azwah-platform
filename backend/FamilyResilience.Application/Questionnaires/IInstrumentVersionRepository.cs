using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public interface IInstrumentVersionRepository
{
    void Add(InstrumentVersion version);

    /// <summary>The version with all sections, items and options.</summary>
    Task<InstrumentVersion?> GetAsync(InstrumentVersionId id, CancellationToken cancellationToken = default);

    /// <summary>The approved version of the instrument whose effective window contains <paramref name="at"/>, if any.</summary>
    Task<InstrumentVersion?> GetEffectiveApprovedAsync(
        InstrumentId instrumentId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>The instrument's version with that number, with all its content, if any.</summary>
    Task<InstrumentVersion?> GetByNumberAsync(InstrumentId instrumentId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>A page of the instrument's versions without content, newest version first, with their content counts.</summary>
    Task<IReadOnlyList<InstrumentVersionSummaryResponse>> ListSummariesAsync(
        InstrumentId instrumentId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(InstrumentVersionId id, CancellationToken cancellationToken = default);

    /// <summary>The highest version number of the instrument, or 0 when it has none.</summary>
    Task<int> GetHighestVersionNumberAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default);
}
