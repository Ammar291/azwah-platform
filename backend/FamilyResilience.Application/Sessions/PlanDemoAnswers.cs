using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// DEMONSTRATIONS ONLY (D-130). Answers that would put an in-progress session's score in the lowest, middle or highest
/// approved band, for a demonstration's "low / medium / high" buttons. Switched off (404) unless
/// <see cref="DemoToolsSettings.Enabled"/>; another family's session is 404 as everywhere. It saves nothing: the app
/// saves the answers through the answers endpoint, and submission scores them with the approved rules. Every plan is
/// audited, so a session answered this way can be recognised.
/// </summary>
public sealed class PlanDemoAnswers(
    DemoToolsSettings settings,
    IAssessmentSessionRepository sessions,
    IParticipantAccess access,
    DemoAnswerPlanner planner,
    IAuditLog auditLog)
{
    public async Task<Result<DemoAnswersResponse>> HandleAsync(
        Guid sessionId, DemoAnswersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.Enabled)
        {
            return Result.Failure<DemoAnswersResponse>(DemoToolsErrors.Disabled);
        }

        var session = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (session.IsFailure)
        {
            return Result.Failure<DemoAnswersResponse>(session.Error!);
        }

        if (session.Value.Status != SessionStatus.InProgress)
        {
            return Result.Failure<DemoAnswersResponse>(SessionErrors.NotInProgress);
        }

        var plan = await planner.PlanAsync(session.Value, DemoAnswersRequestValidator.LevelOf(request), cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<DemoAnswersResponse>(plan.Error!);
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("sessions.demo_answers.planned", "assessment_session", sessionId.ToString(), AuditOutcome.Succeeded),
            cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<DemoAnswersResponse>(recorded.Error!)
            : Result.Success(new DemoAnswersResponse(plan.Value.Answers
                .Select(answer => new AnswerRequest(answer.ItemId.Value, answer.OptionId.Value))
                .ToList()));
    }
}
