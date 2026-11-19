using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>
/// The evaluation tests' own API host and database, holding only the synthetic directory, so resources other tests
/// create can never change the measured numbers.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SupportResourceEvaluationCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "support-resource-evaluation";
}
