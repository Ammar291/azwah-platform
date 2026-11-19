using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Sessions;
using Microsoft.AspNetCore.Authorization;

namespace FamilyResilience.Api.Authorization;

/// <summary>Reads a GUID route value from the request being authorised.</summary>
internal static class RouteResource
{
    public static Guid? Id(AuthorizationHandlerContext context, string routeKey) =>
        context.Resource is HttpContext http && Guid.TryParse(http.GetRouteValue(routeKey)?.ToString(), out var id) ? id : null;

    public static CancellationToken Aborted(AuthorizationHandlerContext context) =>
        context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;

    /// <summary>Whether the caller has a household at all; without one no resource can be theirs, so none is looked up.</summary>
    public static async Task<bool> CallerHasHouseholdAsync(ICallerAccess access, CancellationToken cancellationToken) =>
        (await access.GetCallerAsync(cancellationToken))?.Household is not null;
}

/// <summary>Succeeds when the caller manages the route's household, from their membership record.</summary>
public sealed class HouseholdManagerHandler(ICallerAccess access) : AuthorizationHandler<HouseholdManagerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HouseholdManagerRequirement requirement)
    {
        if (RouteResource.Id(context, "householdId") is { } id
            && await access.CanManageHouseholdAsync(HouseholdId.From(id), RouteResource.Aborted(context)))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Succeeds when the caller may act for the member the route's assignment belongs to.</summary>
public sealed class AssignmentParticipantHandler(ICallerAccess access, IAccessQueries queries)
    : AuthorizationHandler<AssignmentParticipantRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AssignmentParticipantRequirement requirement)
    {
        var cancellationToken = RouteResource.Aborted(context);
        if (RouteResource.Id(context, "assignmentId") is { } id
            && await RouteResource.CallerHasHouseholdAsync(access, cancellationToken)
            && await queries.AssignmentMemberAsync(AssessmentAssignmentId.From(id), cancellationToken) is { } memberId
            && await access.CanActForMemberAsync(memberId, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Succeeds when the caller may act for the member the route's session belongs to.</summary>
public sealed class SessionParticipantHandler(ICallerAccess access, IAccessQueries queries)
    : AuthorizationHandler<SessionParticipantRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SessionParticipantRequirement requirement)
    {
        var cancellationToken = RouteResource.Aborted(context);
        if (RouteResource.Id(context, "sessionId") is { } id
            && await RouteResource.CallerHasHouseholdAsync(access, cancellationToken)
            && await queries.SessionMemberAsync(AssessmentSessionId.From(id), cancellationToken) is { } memberId
            && await access.CanActForMemberAsync(memberId, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Succeeds when the caller's token subject requested the route's export.</summary>
public sealed class ExportRequesterHandler(ICurrentUser currentUser, IAccessQueries queries) : AuthorizationHandler<ExportRequesterRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ExportRequesterRequirement requirement)
    {
        if (RouteResource.Id(context, "exportId") is { } id
            && currentUser.SubjectId is { } subject
            && await queries.ExportRequesterAsync(ExportJobId.From(id), RouteResource.Aborted(context)) is { } requester
            && string.Equals(requester, subject, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }
    }
}
