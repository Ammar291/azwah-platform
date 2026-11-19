namespace FamilyResilience.Domain.Common;

/// <summary>Lets persistence collect and clear domain events without knowing the aggregate's ID type.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> GetDomainEvents();

    void ClearDomainEvents();
}
