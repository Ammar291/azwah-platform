using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// The scale of the total a scoring rule set records (<see cref="ScoringParameters.TotalScale"/>), read from its own
/// definition and its version's content the same way scoring reads them. For a recorded result, the rule set that
/// produced it, so the scale shown is always the one the score was computed on, even after a later version; for a session
/// still being answered, the one approved rule set covering it. Null when the rule set records no total or can no longer
/// be read; nothing is assumed in its place.
/// </summary>
public sealed class ScoreScaleReader(IRuleSetRepository ruleSets, IInstrumentVersionRepository versions, ScoringStrategyCatalog catalog)
{
    /// <summary>The scale of a scoring rule set's total, with or without bands (the trend chart, D-132).</summary>
    public async Task<ScoreScale?> ReadAsync(RuleSetId ruleSetId, CancellationToken cancellationToken)
    {
        if (await ruleSets.GetAsync(ruleSetId, cancellationToken) is not { Kind: RuleSetKind.Scoring } ruleSet
            || await versions.GetAsync(ruleSet.InstrumentVersionId, cancellationToken) is not { } version)
        {
            return null;
        }

        return Read(ruleSet, version, requiredTarget: null, anyTarget: true);
    }

    /// <summary>
    /// The grading scale of a recorded result (D-111): null when its rule set has no bands, because the total and band are
    /// recorded facts and stay shown without a scale.
    /// </summary>
    public async Task<ScoreScaleResponse?> ReadAsync(ScoreResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Graded(await ReadAsync(result.RuleSetId, cancellationToken));
    }

    /// <summary>The grading scale of the single approved rule set for this scope; null when none or several cover it.</summary>
    public async Task<ScoreScaleResponse?> ReadApprovedAsync(InstrumentVersion version, string? sectionCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        var scales = (await ruleSets.ListApprovedAsync(RuleSetKind.Scoring, version.Id, cancellationToken))
            .Select(ruleSet => Graded(Read(ruleSet, version, sectionCode, anyTarget: false)))
            .OfType<ScoreScaleResponse>()
            .ToList();
        return scales.Count == 1 ? scales[0] : null;
    }

    private static ScoreScaleResponse? Graded(ScoreScale? scale) =>
        scale is { Bands.Count: > 0 } ? ScoreScaleResponse.From(scale) : null;

    private ScoreScale? Read(RuleSet ruleSet, InstrumentVersion version, string? requiredTarget, bool anyTarget)
    {
        var definition = ScoringDefinitionParser.Parse(ruleSet.Definition);
        if (definition.IsFailure || (!anyTarget && definition.Value.TargetSectionCode != requiredTarget))
        {
            return null;
        }

        var strategy = catalog.Resolve(definition.Value.PolicyCode, definition.Value.AlgorithmVersion);
        var content = ScoringContent.From(version, definition.Value.TargetSectionCode);
        if (strategy.IsFailure || content.IsFailure)
        {
            return null;
        }

        var parameters = strategy.Value.ValidateParameters(definition.Value.Parameters, content.Value);
        return parameters.IsSuccess ? parameters.Value.TotalScale(content.Value) : null;
    }
}
