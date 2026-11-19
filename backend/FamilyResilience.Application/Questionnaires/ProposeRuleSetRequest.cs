using System.Text.Json;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>A rule set to propose; <see cref="Definition"/> is the JSON object its kind's schema defines.</summary>
public sealed record ProposeRuleSetRequest(RuleSetKind Kind, string Code, int VersionNumber, JsonElement Definition);
