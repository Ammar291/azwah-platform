using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Application.Scoring;

/// <summary>Query string for the household results list: optional wave, instrument code and household reference.</summary>
public sealed class HouseholdResultsRequest : IPageRequest
{
    public Guid? WaveId { get; init; }

    public string? InstrumentCode { get; init; }

    public string? Reference { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
