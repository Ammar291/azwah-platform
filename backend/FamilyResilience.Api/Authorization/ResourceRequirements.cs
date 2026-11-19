using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Api.Authorization;

/// <summary>The caller is the active manager of the household in the route's <c>householdId</c>.</summary>
public sealed class HouseholdManagerRequirement : IHiddenResourceRequirement
{
    public Error NotFound => HouseholdErrors.HouseholdNotFound;

    public string RouteKey => "householdId";

    public string ResourceType => "household";
}

/// <summary>The caller may act for the member of the assignment in the route's <c>assignmentId</c>.</summary>
public sealed class AssignmentParticipantRequirement : IHiddenResourceRequirement
{
    public Error NotFound => AssignmentErrors.AssignmentNotFound;

    public string RouteKey => "assignmentId";

    public string ResourceType => "assessment_assignment";
}

/// <summary>The caller may act for the member of the session in the route's <c>sessionId</c>.</summary>
public sealed class SessionParticipantRequirement : IHiddenResourceRequirement
{
    public Error NotFound => SessionErrors.SessionNotFound;

    public string RouteKey => "sessionId";

    public string ResourceType => "assessment_session";
}

/// <summary>The caller requested the export in the route's <c>exportId</c>.</summary>
public sealed class ExportRequesterRequirement : IHiddenResourceRequirement
{
    public Error NotFound => AnalyticsErrors.ExportNotFound;

    public string RouteKey => "exportId";

    public string ResourceType => "export_job";
}
