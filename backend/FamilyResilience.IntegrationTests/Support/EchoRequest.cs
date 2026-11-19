using FluentValidation;

namespace FamilyResilience.IntegrationTests.Support;

public sealed record EchoRequest(string? Name, int Age);

public sealed class EchoRequestValidator : AbstractValidator<EchoRequest>
{
    public EchoRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(50);
        RuleFor(request => request.Age).InclusiveBetween(0, 120);
    }
}
