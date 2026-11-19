using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Safeguarding;

public readonly record struct SafetyAlertId(Guid Value) : IEntityId<SafetyAlertId>
{
    public static SafetyAlertId New() => new(Guid.CreateVersion7());

    public static SafetyAlertId From(Guid value) => new(value);
}
