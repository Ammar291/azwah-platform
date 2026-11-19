using System.Text.Json;
using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Infrastructure.Content;

/// <summary>
/// Source-derived content shipped with the API as embedded resources: the codebook draft and the proposed eligibility
/// definitions. Nothing here is approved; importing it creates drafts and proposals only.
/// </summary>
public static class ContentFiles
{
    public const string AbuDhabiFamilySurvey = "abu-dhabi-family-survey.v1.json";
    public const string HouseholdAssessment = "household-assessment.v1.json";
    public const string HouseholdAssessmentScoring = "household-assessment.scoring.json";

    public const string FamilyVoicesModuleA = "family-voices-module-a.eligibility.json";
    public const string FamilyVoicesModuleB = "family-voices-module-b.eligibility.json";
    public const string FamilyVoicesModuleC = "family-voices-module-c.eligibility.json";

    /// <summary>The approved authorities and procedures for a report's next steps (D-114). Only people add to it.</summary>
    public const string SupportKnowledgeGraph = "support-knowledge-graph.v1.json";

    /// <summary>The proposed eligibility rule sets: the rule-set code and the file holding its definition.</summary>
    public static IReadOnlyList<(string Code, string FileName)> ProposedEligibilityRules { get; } =
    [
        ("family_voices_module_a", FamilyVoicesModuleA),
        ("family_voices_module_b", FamilyVoicesModuleB),
        ("family_voices_module_c", FamilyVoicesModuleC),
    ];

    /// <summary>The proposed scoring rule sets: the rule-set code and the file holding its <c>scoring.v1</c> definition.</summary>
    public static IReadOnlyList<(string Code, string FileName)> ProposedScoringRules { get; } =
    [
        ("household_assessment_total", HouseholdAssessmentScoring),
    ];

    public static string Read(string fileName)
    {
        var name = $"FamilyResilience.Infrastructure.Content.{fileName}";
        using var stream = typeof(ContentFiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded content '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static InstrumentContentPackage ReadPackage(string fileName) =>
        JsonSerializer.Deserialize<InstrumentContentPackage>(Read(fileName), ContentJson.Options)
        ?? throw new InvalidOperationException($"Embedded content '{fileName}' is empty.");
}
