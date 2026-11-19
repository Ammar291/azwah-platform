namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Assignment-engine tests get their own database: the engine reads every approved eligibility rule set, so it must not
/// see the synthetic, unparseable rule sets other persistence tests create.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EngineDatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "assessment_engine";
}
