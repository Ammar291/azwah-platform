using FluentValidation;

namespace FamilyResilience.Application.Sessions;

public sealed class SaveResponsesRequestValidator : AbstractValidator<SaveResponsesRequest>
{
    /// <summary>Above the largest instrument (39 items), far below what would make a request costly.</summary>
    public const int MaxResponses = 200;

    public SaveResponsesRequestValidator()
    {
        RuleFor(request => request.Responses).NotEmpty();
        RuleFor(request => request.Responses!.Count)
            .LessThanOrEqualTo(MaxResponses)
            .OverridePropertyName("responses")
            .When(request => request.Responses is not null);
        RuleFor(request => request.Responses)
            .Must(responses => responses!.Select(response => response.ItemId).Distinct().Count() == responses!.Count)
            .WithMessage("Each item may be answered once per request.")
            .When(request => request.Responses is { Count: > 0 });
        RuleForEach(request => request.Responses).ChildRules(response =>
        {
            response.RuleFor(answer => answer.ItemId).NotEmpty();
            response.RuleFor(answer => answer.OptionId).NotEmpty();
        });
    }
}
