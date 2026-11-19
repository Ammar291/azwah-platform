namespace FamilyResilience.Domain.Common;

/// <summary>
/// The stable outbox type code of a domain event, for example <c>households.household_created.v1</c>. Renaming the
/// C# type never changes the code; a breaking payload change gets a new version suffix.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DomainEventTypeAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}
