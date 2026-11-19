using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

/// <summary>One approved eligibility rule set in effect: its instrument, effective version and parsed rule.</summary>
public sealed record AssignmentTarget(Instrument Instrument, InstrumentVersion Version, RuleSet RuleSet, EligibilityRule Rule);
