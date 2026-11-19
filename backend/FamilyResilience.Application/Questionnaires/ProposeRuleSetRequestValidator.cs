using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ProposeRuleSetRequestValidator : AbstractValidator<ProposeRuleSetRequest>
{
    public ProposeRuleSetRequestValidator()
    {
        RuleFor(request => request.Kind).IsInEnum();
        RuleFor(request => request.Code).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(request => request.VersionNumber).GreaterThan(0);
        RuleFor(request => request.Definition)
            .Must(definition => definition.ValueKind == JsonValueKind.Object)
            .WithMessage("The definition must be a JSON object.")
            .Must(definition => definition.ValueKind != JsonValueKind.Object || definition.GetRawText().Length <= Domain.Questionnaires.RuleSet.MaxDefinitionLength)
            .WithMessage($"The definition may be at most {Domain.Questionnaires.RuleSet.MaxDefinitionLength} characters.");
    }
}
