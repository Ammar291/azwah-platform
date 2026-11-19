namespace FamilyResilience.IntegrationTests.Support;

/// <summary>An API host with its own database for the assignment endpoints (see <see cref="EngineDatabaseCollection"/>).</summary>
[CollectionDefinition(Name)]
public sealed class EngineApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api_assessment_engine";
}
