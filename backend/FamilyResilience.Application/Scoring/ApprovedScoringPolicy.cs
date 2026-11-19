using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>The single approved scoring rule set whose target is a session's scope, with its parsed definition.</summary>
internal static class ApprovedScoringPolicy
{
    public static async Task<Result<(RuleSet RuleSet, ScoringDefinition Definition)>> FindAsync(
        IRuleSetRepository ruleSets, InstrumentVersion version, string? sectionCode, CancellationToken cancellationToken)
    {
        var matching = new List<(RuleSet, ScoringDefinition)>();
        foreach (var ruleSet in await ruleSets.ListApprovedAsync(RuleSetKind.Scoring, version.Id, cancellationToken))
        {
            var definition = ScoringDefinitionParser.Parse(ruleSet.Definition);
            if (definition.IsFailure)
            {
                return Result.Failure<(RuleSet, ScoringDefinition)>(definition.Error!);
            }

            if (definition.Value.TargetSectionCode == sectionCode)
            {
                matching.Add((ruleSet, definition.Value));
            }
        }

        return matching.Count switch
        {
            0 => Result.Failure<(RuleSet, ScoringDefinition)>(ScoringErrors.PolicyNotApproved),
            1 => Result.Success(matching[0]),
            _ => Result.Failure<(RuleSet, ScoringDefinition)>(ScoringErrors.AmbiguousRules),
        };
    }

    /// <summary>The code of the section a session answers, or null when it answers the whole version.</summary>
    public static string? SectionCodeOf(AssessmentSession session, InstrumentVersion version) =>
        session.InstrumentSectionId is { } sectionId
            ? (version.FindSection(sectionId) ?? throw new InvalidOperationException("A session's section must exist in its version.")).Code
            : null;
}
