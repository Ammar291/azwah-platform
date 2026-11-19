using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Records or replaces several answers in one commit. Every item must belong to the session's version (and module) and
/// every option to its item; the first one that does not refuses the whole request and nothing is saved. Answers are
/// not audited one by one; the submission is. The session row is locked first, as submission locks it, so answers that
/// arrive while the session is being submitted wait and then find it no longer in progress. Once the assignment's time
/// has passed (the wave closed) nothing more is saved, however long ago the session started: the answer is
/// <c>assignments.assignment_expired</c>, as for starting a session then.
/// </summary>
public sealed class SaveResponses(
    IAssessmentSessionRepository sessions,
    IAssessmentAssignmentRepository assignments,
    IInstrumentVersionRepository versions,
    IHouseholdRepository households,
    IConsentRecordRepository consents,
    IParticipantAccess access,
    ISessionLock sessionLock,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public Task<Result<SessionAnswersResponse>> HandleAsync(
        Guid sessionId, SaveResponsesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Responses);
        var answers = request.Responses;

        return unitOfWork.ExecuteInTransactionAsync(token => SaveAsync(sessionId, answers, token), cancellationToken);
    }

    private async Task<Result<SessionAnswersResponse>> SaveAsync(
        Guid sessionId, IReadOnlyList<AnswerRequest> answers, CancellationToken cancellationToken)
    {
        await sessionLock.LockAsync(AssessmentSessionId.From(sessionId), cancellationToken);
        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<SessionAnswersResponse>(loaded.Error!);
        }

        var session = loaded.Value;
        var allowed = await ParticipationChecks.EnsureMayAnswerAsync(session.FamilyMemberId, households, consents, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<SessionAnswersResponse>(allowed.Error!);
        }

        var now = timeProvider.GetUtcNow();
        if (session.Status is not (SessionStatus.Submitted or SessionStatus.Scored))
        {
            // A submitted session keeps answering "no longer in progress"; any other one is told its time has passed.
            var assignment = await assignments.GetAsync(session.AssessmentAssignmentId, cancellationToken)
                ?? throw new InvalidOperationException("A session's assignment must exist.");
            if (assignment.HasExpiredAt(now))
            {
                return Result.Failure<SessionAnswersResponse>(AssignmentErrors.AssignmentExpired);
            }
        }

        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        foreach (var answer in answers)
        {
            var answered = session.Answer(version, InstrumentItemId.From(answer.ItemId), ResponseOptionId.From(answer.OptionId), now);
            if (answered.IsFailure)
            {
                return Result.Failure<SessionAnswersResponse>(answered.Error!);
            }
        }

        var committed = await unitOfWork.CommitAsync(cancellationToken);
        return committed.IsFailure
            ? Result.Failure<SessionAnswersResponse>(committed.Error!)
            : Result.Success(session.ToAnswers(version));
    }
}
