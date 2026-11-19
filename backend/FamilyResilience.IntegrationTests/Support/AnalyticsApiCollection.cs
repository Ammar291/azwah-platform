namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Aggregate tests against <see cref="AnalyticsApi"/>'s own database with a known population.</summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsApiCollection : ICollectionFixture<AnalyticsApi>
{
    public const string Name = "analytics_api";
}
