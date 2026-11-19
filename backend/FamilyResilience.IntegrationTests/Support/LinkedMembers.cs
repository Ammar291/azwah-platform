using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>A household whose manager answers for the head, with a parent of 68 who signs in with an account of their own.</summary>
internal sealed record LinkedParent(Enrolment Manager, HttpClient Parent, string ParentSubject, Guid ParentId, Guid ParentAssignment) : IDisposable
{
    public void Dispose()
    {
        Manager.Dispose();
        Parent.Dispose();
    }
}

internal static class LinkedMembers
{
    /// <summary>
    /// Enrols a head and a parent of 68, confirms the roster (the parent gets a section B assignment) and links an account
    /// to the parent straight in the database, because joining a household has no endpoint yet (D-49).
    /// </summary>
    public static async Task<LinkedParent> LinkedParentAsync(this ApiFactory factory, CancellationToken cancellationToken)
    {
        var manager = await Enrolment.CreateAsync(factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var parentId = manager.Aged(68);
        var parentAssignment = await Journeys.AssignmentAsync(manager, parentId, "B", cancellationToken);
        var subject = ApiClients.NewSubject("parent");
        await factory.InScopeAsync(
            async services =>
            {
                var user = TestData.Ok(PlatformUser.Register(subject, TestData.Now));
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                var household = await services.GetRequiredService<IHouseholdRepository>()
                    .GetAsync(HouseholdId.From(manager.Household.Id), cancellationToken);
                TestData.Ok(household!.LinkAccount(user.Id, FamilyMemberId.From(parentId), TestData.Now));
            },
            cancellationToken);
        return new LinkedParent(manager, factory.AsParticipant(subject), subject, parentId, parentAssignment);
    }
}
