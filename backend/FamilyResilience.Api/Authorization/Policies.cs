namespace FamilyResilience.Api.Authorization;

/// <summary>
/// Authorization policy names. Each requires a signed-in caller with a token subject and the listed roles; the
/// participant policies add resource requirements that are checked against the platform's own records.
/// </summary>
public static class Policies
{
    /// <summary>Role <c>participant</c>.</summary>
    public const string Participant = "Participant";

    /// <summary><see cref="Participant"/> and the active manager of the route's household.</summary>
    public const string HouseholdManager = "HouseholdManager";

    /// <summary>Participant who may act for the member of the route's assignment.</summary>
    public const string AssignmentParticipant = "AssignmentParticipant";

    /// <summary>Participant who may act for the member of the route's session.</summary>
    public const string SessionParticipant = "SessionParticipant";

    /// <summary>Role <c>researcher</c>.</summary>
    public const string Researcher = "Researcher";

    /// <summary><see cref="Researcher"/> who requested the route's export.</summary>
    public const string ExportRequester = "ExportRequester";

    /// <summary>Role <c>program_administrator</c>.</summary>
    public const string ProgramAdministrator = "ProgramAdministrator";

    /// <summary>Role <c>system_administrator</c>: content, rule sets, eligibility runs and scoring runs.</summary>
    public const string SystemAdministrator = "SystemAdministrator";

    /// <summary>Role <c>support_coordinator</c>: the referral queue and manual hand-offs to partners (D-61).</summary>
    public const string SupportCoordinator = "SupportCoordinator";

    /// <summary>Role <c>system_administrator</c> or <c>support_coordinator</c>: the partner organisation list.</summary>
    public const string PartnerReader = "PartnerReader";

    /// <summary>Role <c>participant</c>, <c>support_coordinator</c> or <c>system_administrator</c>: the approved support-resource directory.</summary>
    public const string SupportResourceReader = "SupportResourceReader";

    /// <summary>Role <c>researcher</c> or <c>program_administrator</c>: suppressed aggregates only.</summary>
    public const string AnalyticsReader = "AnalyticsReader";

    /// <summary>
    /// Role <c>researcher</c>, <c>program_administrator</c> or <c>system_administrator</c>: the wave list, which holds no
    /// personal data and filters the analytics and household results pages (D-104).
    /// </summary>
    public const string WaveReader = "WaveReader";

    /// <summary>Any platform role: published questionnaire content, which holds no personal data.</summary>
    public const string QuestionnaireReader = "QuestionnaireReader";
}
