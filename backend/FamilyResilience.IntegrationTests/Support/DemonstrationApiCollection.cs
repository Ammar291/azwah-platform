namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Tests against <see cref="DemonstrationApi"/>: one built demonstration database, shared in order.</summary>
[CollectionDefinition(Name)]
public sealed class DemonstrationApiCollection : ICollectionFixture<DemonstrationApi>
{
    public const string Name = "demonstration_api";
}
