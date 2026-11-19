using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>A session that passed <see cref="ScoreReportPreparation"/>, with its sections in order and its scoring content.</summary>
public sealed record PreparedSession(AssessmentSession Session, IReadOnlyList<InstrumentSection> Sections, ScoringContent Content);
