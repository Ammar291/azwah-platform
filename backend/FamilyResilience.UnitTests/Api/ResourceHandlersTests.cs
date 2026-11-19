using System.Security.Claims;
using FamilyResilience.Api.Authorization;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Api;

public sealed class ResourceHandlersTests
{
    private static readonly PlatformUserId Manager = PlatformUserId.New();

    private static Household Enrolled() =>
        Ok(Household.Enrol(HouseholdReference.Generate(), [Head()], 0, Manager, Now));

    private static AuthorizationHandlerContext Context(IAuthorizationRequirement requirement, string routeKey) =>
        new(
            [requirement],
            new ClaimsPrincipal(),
            new DefaultHttpContext { Request = { RouteValues = new RouteValueDictionary { [routeKey] = Guid.NewGuid().ToString() } } });

    public static TheoryData<bool> CallersWithoutAHousehold => [false, true];

    [Theory]
    [MemberData(nameof(CallersWithoutAHousehold))]
    public async Task An_assignment_is_not_looked_up_for_a_caller_without_a_household(bool registered)
    {
        var queries = new CountingAccessQueries(Enrolled().Members[0].Id);
        var handler = new AssignmentParticipantHandler(new RecordAccess(registered ? new CallerRecord(Manager, null) : null), queries);
        var context = Context(new AssignmentParticipantRequirement(), "assignmentId");

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, queries.Lookups);
    }

    [Theory]
    [MemberData(nameof(CallersWithoutAHousehold))]
    public async Task A_session_is_not_looked_up_for_a_caller_without_a_household(bool registered)
    {
        var queries = new CountingAccessQueries(Enrolled().Members[0].Id);
        var handler = new SessionParticipantHandler(new RecordAccess(registered ? new CallerRecord(Manager, null) : null), queries);
        var context = Context(new SessionParticipantRequirement(), "sessionId");

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, queries.Lookups);
    }

    [Fact]
    public async Task A_member_of_the_callers_household_is_looked_up_and_passes()
    {
        var household = Enrolled();
        var access = new RecordAccess(new CallerRecord(Manager, household));
        var assignments = new CountingAccessQueries(household.Members[0].Id);
        var sessions = new CountingAccessQueries(household.Members[0].Id);
        var assignment = Context(new AssignmentParticipantRequirement(), "assignmentId");
        var session = Context(new SessionParticipantRequirement(), "sessionId");

        await new AssignmentParticipantHandler(access, assignments).HandleAsync(assignment);
        await new SessionParticipantHandler(access, sessions).HandleAsync(session);

        Assert.Equal((true, 1), (assignment.HasSucceeded, assignments.Lookups));
        Assert.Equal((true, 1), (session.HasSucceeded, sessions.Lookups));
    }

    private sealed class RecordAccess(CallerRecord? record) : ICallerAccess
    {
        public Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default) => Task.FromResult(record);

        public Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
            Task.FromResult(record?.Household is { } household && household.Id == householdId && household.IsManagedBy(record.UserId));

        public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(record?.Household is { } household && household.MayActFor(record.UserId, memberId));

        public Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(record?.Household is { } household ? household.MembersActableBy(record.UserId) : (IReadOnlyList<FamilyMemberId>)[]);
    }

    /// <summary>Answers every assignment and session lookup with one member and counts the lookups.</summary>
    private sealed class CountingAccessQueries(FamilyMemberId memberId) : IAccessQueries
    {
        public int Lookups { get; private set; }

        public Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handlers resolve the caller through ICallerAccess.");

        public Task<FamilyMemberId?> AssignmentMemberAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult<FamilyMemberId?>(memberId);
        }

        public Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult<FamilyMemberId?>(memberId);
        }
    }
}
