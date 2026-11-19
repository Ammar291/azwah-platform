using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class IdentityPersistenceTests(DatabaseFixture fixture)
{
    private async Task<PlatformUser> SavedUserAsync(CancellationToken cancellationToken)
    {
        var user = TestData.Ok(PlatformUser.Register(ApiSubject(), TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                return Task.CompletedTask;
            },
            cancellationToken);
        return user;
    }

    private static string ApiSubject() => $"subject-{Guid.NewGuid():N}";

    private static Household Enrolled(PlatformUser user) =>
        TestData.Ok(Household.Enrol(HouseholdReference.Generate(), [TestData.Head(), TestData.Spouse()], 0, user.Id, TestData.Now));

    [Fact]
    public async Task An_enrolled_household_round_trips_with_its_manager_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var household = Enrolled(user);
        TestData.Ok(household.ConfirmRoster(TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(TestData.Now, loaded.RosterConfirmedAt);
        var membership = Assert.Single(loaded.Memberships);
        Assert.Equal(
            (user.Id, household.Members[0].Id, HouseholdRole.Manager, MembershipStatus.Active),
            (membership.PlatformUserId, membership.FamilyMemberId, membership.Role, membership.Status));
        Assert.True(loaded.IsManagedBy(user.Id));
    }

    [Fact]
    public async Task The_access_read_model_finds_the_caller_and_their_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var household = Enrolled(user);
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);

        await using var scope = fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAccessQueries>();
        var caller = await queries.FindCallerAsync(user.Subject, cancellationToken);
        var unknown = await queries.FindCallerAsync(ApiSubject(), cancellationToken);

        Assert.NotNull(caller);
        Assert.Equal((user.Id, household.Id), (caller.UserId, caller.Household?.Id));
        Assert.Equal(2, caller.Household!.MembersActableBy(user.Id).Count);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task A_user_without_a_household_is_found_without_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);

        await using var scope = fixture.CreateScope();
        var caller = await scope.ServiceProvider.GetRequiredService<IAccessQueries>().FindCallerAsync(user.Subject, cancellationToken);

        Assert.NotNull(caller);
        Assert.Null(caller.Household);
    }

    [Fact]
    public async Task A_second_active_membership_for_the_same_user_returns_already_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(Enrolled(user));
                return Task.CompletedTask;
            },
            cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHouseholdRepository>().Add(Enrolled(user));
        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(HouseholdErrors.AlreadyMember, committed.Error);
    }

    [Fact]
    public async Task A_subject_is_registered_once_and_a_second_registration_reads_as_already_a_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
            .Add(TestData.Ok(PlatformUser.Register(user.Subject, TestData.Now)));
        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(HouseholdErrors.AlreadyMember, committed.Error);
    }

    [Fact]
    public async Task The_manager_stays_on_the_roster_and_member_changes_clear_the_confirmation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var household = Enrolled(user);
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);

        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken);
                Assert.Equal(HouseholdErrors.MemberIsManager, tracked!.DeactivateMember(household.Members[0].Id, TestData.Now).Error);
                TestData.Ok(tracked.UpdateMember(
                    household.Members[1].Id,
                    new FamilyMemberDetails(TestData.BornYearsAgo(40), HouseholdRelationship.Spouse, EmploymentStatus.Retired, false, false),
                    TestData.Now));
            },
            cancellationToken);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken),
            cancellationToken);
        Assert.Equal(EmploymentStatus.Retired, loaded!.FindMember(household.Members[1].Id)!.EmploymentStatus);
        Assert.Null(loaded.RosterConfirmedAt);
    }
}
