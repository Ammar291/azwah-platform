using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// A participant enrolled straight into the database, managing a one-member household, with participation consent and
/// a whole-version assignment of a new approved synthetic version, so a test can attach whatever rule sets it needs to
/// that version without touching anyone else's. Only throwaway test databases hold approved synthetic content.
/// </summary>
internal sealed record SeededParticipant(
    HttpClient Client, string Subject, Guid AssignmentId, InstrumentVersion Version, FamilyMember Member) : IDisposable
{
    public void Dispose() => Client.Dispose();

    public static async Task<SeededParticipant> CreateAsync(ApiFactory factory, CancellationToken cancellationToken)
    {
        var subject = ApiClients.NewSubject("participant");
        var user = TestData.Ok(PlatformUser.Register(subject, TestData.Now));
        var household = TestData.Ok(Household.Enrol(HouseholdReference.Generate(), [TestData.Head()], 0, user.Id, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);
        var member = household.Members[0];
        await factory.GrantedConsentAsync(member, await factory.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken), cancellationToken);
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        var assignment = await factory.AssignmentAsync(member, version, cancellationToken);
        return new SeededParticipant(factory.AsParticipant(subject), subject, assignment.Id.Value, version, member);
    }

    /// <summary>Every item answered with its lowest option.</summary>
    public object Answers() => new
    {
        responses = Version.GetItems()
            .Select(item => new { itemId = item.Id.Value, optionId = item.Options.OrderBy(option => option.Value).First().Id.Value })
            .ToArray(),
    };
}
