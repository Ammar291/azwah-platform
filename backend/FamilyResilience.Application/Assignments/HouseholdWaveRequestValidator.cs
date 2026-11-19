using FluentValidation;

namespace FamilyResilience.Application.Assignments;

public sealed class HouseholdWaveRequestValidator : AbstractValidator<HouseholdWaveRequest>
{
    public HouseholdWaveRequestValidator() => RuleFor(request => request.WaveId).NotEmpty();
}
