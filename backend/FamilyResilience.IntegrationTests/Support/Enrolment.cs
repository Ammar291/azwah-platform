using FamilyResilience.Application.Households;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>A participant signed in as the manager of a household created through the API, with its roster.</summary>
internal sealed class Enrolment(HttpClient client, string subject, HouseholdResponse household, IReadOnlyList<MemberResponse> members) : IDisposable
{
    public HttpClient Client { get; } = client;

    public string Subject { get; } = subject;

    public HouseholdResponse Household { get; } = household;

    public IReadOnlyList<MemberResponse> Members { get; } = members;

    public Guid Self => Members.Single(member => member.IsCaller).Id;

    /// <summary>The member of that age (ages in a test roster are unique).</summary>
    public Guid Aged(int age) => Members.Single(member => member.Age == age).Id;

    public static async Task<Enrolment> CreateAsync(ApiFactory factory, CancellationToken cancellationToken, params object[] roster)
    {
        var subject = ApiClients.NewSubject("participant");
        var client = factory.AsParticipant(subject);
        var household = await client.CreateHouseholdAsync(cancellationToken, roster);
        return new Enrolment(client, subject, household, await client.MembersAsync(household.Id, cancellationToken));
    }

    public void Dispose() => Client.Dispose();
}
