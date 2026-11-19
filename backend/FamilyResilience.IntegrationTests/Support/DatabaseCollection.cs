namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Persistence tests share one PostgreSQL container, migrated once. Tests must not assume empty tables.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
