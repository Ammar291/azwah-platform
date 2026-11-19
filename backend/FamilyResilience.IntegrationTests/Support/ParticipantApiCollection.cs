namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Participant journeys against <see cref="ParticipantApi"/>'s own seeded database.</summary>
[CollectionDefinition(Name)]
public sealed class ParticipantApiCollection : ICollectionFixture<ParticipantApi>
{
    public const string Name = "participant_api";
}
