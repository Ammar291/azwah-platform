using FamilyResilience.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace FamilyResilience.Api.Authorization;

/// <summary>
/// A resource-level requirement whose failure must not reveal that the resource exists: the caller gets 404 with
/// <see cref="NotFound"/>'s code, exactly as for an ID that does not exist (docs/api-conventions.md). The refusal is
/// audited against <see cref="ResourceType"/> and the route value named <see cref="RouteKey"/>.
/// </summary>
public interface IHiddenResourceRequirement : IAuthorizationRequirement
{
    Error NotFound { get; }

    /// <summary>The route value that holds the resource's ID.</summary>
    string RouteKey { get; }

    /// <summary>The audit resource type of the resource, as its own module records it.</summary>
    string ResourceType { get; }
}
