namespace FamilyResilience.Domain.Assignments;

/// <summary>An assignment already in the wave, with the instrument and module codes its version and section resolve to.</summary>
public sealed record ExistingAssignment(AssessmentAssignment Assignment, string InstrumentCode, string? SectionCode);
