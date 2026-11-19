using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// One submitted or scored session of a member with research-use consent, as the export query reads it. The IDs and the
/// birth month stay inside the platform: <see cref="ExportCatalog"/> turns them into pseudonyms and an age band.
/// <see cref="NonSubstantiveCount"/> is how many of the <see cref="AnsweredCount"/> answers chose a non-substantive
/// option ("prefer not to answer", 99); which items they were is not read.
/// </summary>
public sealed record ExportSessionRow(
    FamilyMemberId MemberId,
    HouseholdId HouseholdId,
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string WaveCode,
    SessionStatus Status,
    DateTimeOffset SubmittedAt,
    int BirthYear,
    int BirthMonth,
    EmploymentStatus? EmploymentStatus,
    bool IsHeadOfHousehold,
    int AnsweredCount,
    int NonSubstantiveCount);
