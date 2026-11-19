namespace FamilyResilience.Domain.Members;

/// <summary>
/// A member's relationship to the head of household. Proposed list, not yet approved (docs/decisions.md). The head
/// has no relationship value.
/// </summary>
public enum HouseholdRelationship
{
    Spouse = 1,
    Child = 2,
    Parent = 3,
    Sibling = 4,
    Grandchild = 5,
    Grandparent = 6,
    OtherRelative = 7,
    NonRelative = 8,
}
