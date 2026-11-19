using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Identity;

public sealed class CallerAccessTests
{
    private const string Subject = "manager-subject";
    private static readonly PlatformUserId ManagerId = PlatformUserId.New();

    private static Household Enrolled() =>
        Ok(Household.Enrol(
            HouseholdReference.Generate(),
            [Head(), Adult(39, relationship: HouseholdRelationship.Spouse), Child(9)],
            0,
            ManagerId,
            Now));

    private static CallerAccess Access(CallerRecord? record, string? subject = Subject) =>
        new(new FakeCurrentUser(subject is not null, subject), new FakeAccessQueries(record));

    [Fact]
    public async Task The_manager_manages_their_household_and_acts_for_every_member_without_an_account()
    {
        var household = Enrolled();
        var access = Access(new CallerRecord(ManagerId, household));
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await access.CanManageHouseholdAsync(household.Id, cancellationToken));
        Assert.Equal(household.Members.Select(member => member.Id), await access.ActableMemberIdsAsync(cancellationToken));
        foreach (var member in household.Members)
        {
            Assert.True(await access.CanActForMemberAsync(member.Id, cancellationToken));
        }
    }

    [Fact]
    public async Task A_member_who_signs_in_themselves_is_out_of_the_managers_reach()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        Ok(household.LinkAccount(PlatformUserId.New(), spouse, Now));
        var access = Access(new CallerRecord(ManagerId, household));
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await access.CanActForMemberAsync(spouse, cancellationToken));
        Assert.DoesNotContain(spouse, await access.ActableMemberIdsAsync(cancellationToken));
        Assert.True(await access.CanManageHouseholdAsync(household.Id, cancellationToken));
    }

    [Fact]
    public async Task Another_household_and_its_members_are_out_of_reach()
    {
        var mine = Enrolled();
        var theirs = Enrolled();
        var access = Access(new CallerRecord(ManagerId, mine));
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await access.CanManageHouseholdAsync(theirs.Id, cancellationToken));
        Assert.False(await access.CanActForMemberAsync(theirs.Members[0].Id, cancellationToken));
    }

    [Fact]
    public async Task A_caller_who_has_not_enrolled_reaches_nothing()
    {
        var household = Enrolled();
        var access = Access(new CallerRecord(PlatformUserId.New(), null));
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await access.CanManageHouseholdAsync(household.Id, cancellationToken));
        Assert.False(await access.CanActForMemberAsync(household.Members[0].Id, cancellationToken));
        Assert.Empty(await access.ActableMemberIdsAsync(cancellationToken));
    }

    [Fact]
    public async Task A_caller_without_a_subject_is_never_looked_up()
    {
        var queries = new FakeAccessQueries(new CallerRecord(ManagerId, Enrolled()));
        var access = new CallerAccess(new FakeCurrentUser(false, null), queries);

        Assert.Null(await access.GetCallerAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, queries.Lookups);
    }

    [Fact]
    public async Task The_caller_is_looked_up_once_per_request()
    {
        var household = Enrolled();
        var queries = new FakeAccessQueries(new CallerRecord(ManagerId, household));
        var access = new CallerAccess(new FakeCurrentUser(true, Subject), queries);
        var cancellationToken = TestContext.Current.CancellationToken;

        await access.CanManageHouseholdAsync(household.Id, cancellationToken);
        await access.CanActForMemberAsync(household.Members[1].Id, cancellationToken);
        await access.ActableMemberIdsAsync(cancellationToken);

        Assert.Equal(1, queries.Lookups);
    }

    private sealed class FakeAccessQueries(CallerRecord? record) : IAccessQueries
    {
        public int Lookups { get; private set; }

        public Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(subject == Subject ? record : null);
        }

        public Task<FamilyMemberId?> AssignmentMemberAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FamilyMemberId?>(null);

        public Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FamilyMemberId?>(null);
    }
}
