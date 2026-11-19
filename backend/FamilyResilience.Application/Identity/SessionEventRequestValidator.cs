using FluentValidation;

namespace FamilyResilience.Application.Identity;

public sealed class SessionEventRequestValidator : AbstractValidator<SessionEventRequest>
{
    public SessionEventRequestValidator() =>
        RuleFor(request => request.Client)
            .NotEmpty()
            .Must(client => AuthClients.All.Contains(client, StringComparer.Ordinal))
            .WithMessage("The client must be admin or mobile.");
}
