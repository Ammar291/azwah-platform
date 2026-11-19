using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Sessions;

/// <summary>
/// One sitting of answers for an assignment. Answers are validated against the exact instrument version (and, for a
/// module, its section). Every answer touches the session row, so its concurrency token covers all responses.
/// </summary>
public sealed class AssessmentSession : AggregateRoot<AssessmentSessionId>
{
    private readonly List<AssessmentResponse> _responses = [];

    private AssessmentSession(AssessmentSessionId id)
        : base(id)
    {
    }

    public AssessmentAssignmentId AssessmentAssignmentId { get; private set; }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public InstrumentSectionId? InstrumentSectionId { get; private set; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ScoredAt { get; private set; }

    public DateTimeOffset? AbandonedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AssessmentResponse> Responses => _responses.AsReadOnly();

    public static Result<AssessmentSession> Start(AssessmentAssignment assignment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<AssessmentSession>(CommonErrors.TimeNotUtc);
        }

        var started = assignment.MarkInProgress(now);
        if (started.IsFailure)
        {
            return Result.Failure<AssessmentSession>(started.Error!);
        }

        return Result.Success(new AssessmentSession(AssessmentSessionId.New())
        {
            AssessmentAssignmentId = assignment.Id,
            FamilyMemberId = assignment.FamilyMemberId,
            InstrumentVersionId = assignment.InstrumentVersionId,
            InstrumentSectionId = assignment.InstrumentSectionId,
            Status = SessionStatus.InProgress,
            StartedAt = now,
            UpdatedAt = now,
        });
    }

    public Result Answer(InstrumentVersion version, InstrumentItemId itemId, ResponseOptionId optionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(version);

        var open = EnsureInProgress(now);
        if (open.IsFailure)
        {
            return open;
        }

        if (version.Id != InstrumentVersionId)
        {
            return Result.Failure(SessionErrors.VersionMismatch);
        }

        var item = version.FindItem(itemId);
        if (item is null)
        {
            return Result.Failure(SessionErrors.ItemNotInVersion);
        }

        if (!InScope(item))
        {
            return Result.Failure(SessionErrors.ItemNotInSection);
        }

        var option = item.FindOption(optionId);
        if (option is null)
        {
            return Result.Failure(SessionErrors.OptionNotInItem);
        }

        var existing = FindResponse(itemId);
        if (existing is null)
        {
            _responses.Add(AssessmentResponse.Create(Id, InstrumentVersionId, item.Id, option, now));
        }
        else
        {
            existing.Change(option, now);
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Submits the answers and completes the assignment. Submitting a session that is already submitted (or scored)
    /// changes nothing and returns <see cref="SubmissionOutcome.AlreadySubmitted"/>, so a retried request is harmless.
    /// Once the assignment's time has passed nothing more is submitted, however long ago the session started.
    /// </summary>
    public Result<SubmissionOutcome> Submit(InstrumentVersion version, AssessmentAssignment assignment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(assignment);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<SubmissionOutcome>(CommonErrors.TimeNotUtc);
        }

        if (assignment.Id != AssessmentAssignmentId)
        {
            return Result.Failure<SubmissionOutcome>(SessionErrors.AssignmentMismatch);
        }

        if (version.Id != InstrumentVersionId)
        {
            return Result.Failure<SubmissionOutcome>(SessionErrors.VersionMismatch);
        }

        if (Status is SessionStatus.Submitted or SessionStatus.Scored)
        {
            return Result.Success(SubmissionOutcome.AlreadySubmitted);
        }

        if (assignment.HasExpiredAt(now))
        {
            return Result.Failure<SubmissionOutcome>(AssignmentErrors.AssignmentExpired);
        }

        if (!SessionTransitions.IsAllowed(Status, SessionStatus.Submitted))
        {
            return Result.Failure<SubmissionOutcome>(SessionErrors.NotInProgress);
        }

        if (now < UpdatedAt)
        {
            return Result.Failure<SubmissionOutcome>(SessionErrors.TimeBeforeLastChange);
        }

        if (version.GetItems().Any(item => item.IsRequired && InScope(item) && FindResponse(item.Id) is null))
        {
            return Result.Failure<SubmissionOutcome>(SessionErrors.RequiredItemsUnanswered);
        }

        var completed = assignment.Complete(now);
        if (completed.IsFailure)
        {
            return Result.Failure<SubmissionOutcome>(completed.Error!);
        }

        Status = SessionStatus.Submitted;
        SubmittedAt = now;
        UpdatedAt = now;
        Raise(new AssessmentSessionSubmitted(Id, AssessmentAssignmentId, now));
        return Result.Success(SubmissionOutcome.Submitted);
    }

    public Result Abandon(AssessmentAssignment assignment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (!SessionTransitions.IsAllowed(Status, SessionStatus.Abandoned))
        {
            return Result.Failure(SessionErrors.NotInProgress);
        }

        if (now < UpdatedAt)
        {
            return Result.Failure(SessionErrors.TimeBeforeLastChange);
        }

        if (assignment.Id != AssessmentAssignmentId)
        {
            return Result.Failure(SessionErrors.AssignmentMismatch);
        }

        assignment.ReturnToAssigned(now);
        Status = SessionStatus.Abandoned;
        AbandonedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public AssessmentResponse? FindResponse(InstrumentItemId itemId) =>
        _responses.Find(response => response.InstrumentItemId == itemId);

    /// <summary>Called by <c>ScoreResult.Record</c>; re-scoring a scored session moves <see cref="ScoredAt"/>.</summary>
    internal Result MarkScored(DateTimeOffset now)
    {
        if (!SessionTransitions.IsAllowed(Status, SessionStatus.Scored))
        {
            return Result.Failure(SessionErrors.NotSubmitted);
        }

        if (now < UpdatedAt)
        {
            return Result.Failure(SessionErrors.TimeBeforeLastChange);
        }

        Status = SessionStatus.Scored;
        ScoredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private bool InScope(InstrumentItem item) =>
        InstrumentSectionId is null || item.InstrumentSectionId == InstrumentSectionId;

    private Result EnsureInProgress(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != SessionStatus.InProgress)
        {
            return Result.Failure(SessionErrors.NotInProgress);
        }

        return now < UpdatedAt ? Result.Failure(SessionErrors.TimeBeforeLastChange) : Result.Success();
    }
}
