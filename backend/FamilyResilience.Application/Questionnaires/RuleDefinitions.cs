using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

internal static class RuleDefinitions
{
    /// <summary>Validates with the owning module's validator; a kind without one (referral) cannot be proposed yet.</summary>
    public static Result Validate(
        IEnumerable<IRuleDefinitionValidator> validators, RuleSetKind kind, string definition, Instrument instrument, InstrumentVersion version) =>
        validators.FirstOrDefault(validator => validator.Kind == kind) is { } owner
            ? owner.Validate(definition, instrument, version)
            : Result.Failure(QuestionnaireErrors.RuleSetKindNotSupported);
}
