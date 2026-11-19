namespace FamilyResilience.Api.Authorization;

/// <summary>
/// The permission codes clients may show or hide features by, each granted by exactly one policy. The policies stay the
/// only source of truth: <c>GET /api/v1/me/permissions</c> evaluates them for the caller.
/// </summary>
public static class PermissionMap
{
    public static IReadOnlyList<(string Permission, string Policy)> Global { get; } =
    [
        ("households.create", Policies.Participant),
        ("consent.manage", Policies.Participant),
        ("assessments.answer", Policies.Participant),
        ("support.read", Policies.Participant),
        ("privacy.request", Policies.Participant),
        ("assistant.use", Policies.Participant),
        ("questionnaires.read", Policies.QuestionnaireReader),
        ("participation.read", Policies.ProgramAdministrator),
        ("assessments.monitor", Policies.ProgramAdministrator),
        ("privacy.requests.manage", Policies.ProgramAdministrator),
        ("analytics.read", Policies.AnalyticsReader),
        ("analytics.scores.read", Policies.Researcher),
        ("exports.manage", Policies.Researcher),
        ("content.manage", Policies.SystemAdministrator),
        ("rules.manage", Policies.SystemAdministrator),
        ("eligibility.run", Policies.SystemAdministrator),
        ("scoring.run", Policies.SystemAdministrator),
        ("results.read", Policies.SystemAdministrator),
        ("partners.manage", Policies.SystemAdministrator),
        ("support_resources.manage", Policies.SystemAdministrator),
        // System administrators only: a score alert reveals a score, which programme administrators never see (D-62, D-137).
        ("safety_alerts.read", Policies.SystemAdministrator),
        ("safety_alerts.manage", Policies.SystemAdministrator),
        ("support_resources.read", Policies.SupportResourceReader),
        ("referrals.manage", Policies.SupportCoordinator),
    ];

    /// <summary>What the active manager of a household may do with it.</summary>
    public static IReadOnlyList<string> HouseholdManager { get; } =
        ["household.read", "household.update", "members.manage", "roster.confirm"];
}
