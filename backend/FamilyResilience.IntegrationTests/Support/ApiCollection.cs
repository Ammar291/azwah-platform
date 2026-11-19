namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Tests in this collection share one API host and one database container.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
