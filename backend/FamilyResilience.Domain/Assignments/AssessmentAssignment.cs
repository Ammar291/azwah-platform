using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Assignments;

/// <summary>
/// A member asked to answer one approved instrument version (or one of its sections) in a wave, because an approved
/// eligibility rule set said so. The eligibility evaluation itself happens in a later phase; this type only refuses
/// anything that is not backed by approved content and rules.
/// </summary>
public sealed class AssessmentAssignment : AggregateRoot<AssessmentAssignmentId>
{
    private AssessmentAssignment(AssessmentAssignmentId id)
        : base(id)
    {
    }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    /// <summary>Set for a family-voices module; null when the whole version is assigned.</summary>
    public InstrumentSectionId? InstrumentSectionId { get; private set; }

    public WaveId WaveId { get; private set; }

    public AssignmentReason Reason { get; private set; }

    public RuleSetId EligibilityRuleSetId { get; private set; }

    public AssignmentStatus Status { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<AssessmentAssignment> Assign(
        FamilyMember member,
        InstrumentVersion version,
        InstrumentSectionId? sectionId,
        Wave wave,
        RuleSet eligibilityRules,
        AssignmentReason reason,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(wave);
        ArgumentNullException.ThrowIfNull(eligibilityRules);

        if (!UtcTime.IsUtc(now) || !UtcTime.IsUtc(expiresAt))
        {
            return Result.Failure<AssessmentAssignment>(CommonErrors.TimeNotUtc);
        }

        if (!Enum.IsDefined(reason))
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.ReasonInvalid);
        }

        if (expiresAt <= now)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.ExpiryInvalid);
        }

        if (!member.IsActive)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.MemberNotActive);
        }

        if (!member.IsCertainlyAdultOn(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.MemberUnder18);
        }

        if (!version.IsEffectiveAt(now))
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.VersionNotEffective);
        }

        if (sectionId is { } section && version.FindSection(section) is null)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.SectionNotInVersion);
        }

        if (!wave.AcceptsAssignments)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.WaveClosed);
        }

        if (eligibilityRules.Kind != RuleSetKind.Eligibility)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.RuleSetWrongKind);
        }

        if (eligibilityRules.Status != RuleSetStatus.Approved)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.RuleSetNotApproved);
        }

        if (eligibilityRules.InstrumentVersionId != version.Id)
        {
            return Result.Failure<AssessmentAssignment>(AssignmentErrors.RuleSetVersionMismatch);
        }

        var assignment = new AssessmentAssignment(AssessmentAssignmentId.New())
        {
            FamilyMemberId = member.Id,
            InstrumentVersionId = version.Id,
            InstrumentSectionId = sectionId,
            WaveId = wave.Id,
            Reason = reason,
            EligibilityRuleSetId = eligibilityRules.Id,
            Status = AssignmentStatus.Assigned,
            AssignedAt = now,
            ExpiresAt = expiresAt,
            UpdatedAt = now,
        };
        assignment.Raise(new AssessmentAssigned(assignment.Id, member.Id, version.Id, wave.Id, now));
        return Result.Success(assignment);
    }

    /// <summary>
    /// Whether the time to answer has passed at <paramref name="now"/>: the assignment is marked Expired, or its
    /// <see cref="ExpiresAt"/> (the wave's close) has been reached. From then on no session starts, no answer is saved
    /// and nothing is submitted, whether or not maintenance has marked the row yet.
    /// </summary>
    public bool HasExpiredAt(DateTimeOffset now) => Status == AssignmentStatus.Expired || ExpiresAt <= now;

    /// <summary>Whether maintenance should close it: still waiting or in progress although <see cref="ExpiresAt"/> has passed.</summary>
    public bool IsOverdueAt(DateTimeOffset now) =>
        Status is AssignmentStatus.Assigned or AssignmentStatus.InProgress && ExpiresAt <= now;

    public Result Expire(DateTimeOffset now) => Finish(AssignmentStatus.Expired, now);

    public Result Cancel(DateTimeOffset now) => Finish(AssignmentStatus.Cancelled, now);

    /// <summary>
    /// Called when a session starts. An assignment past its expiry, whether or not maintenance has marked it Expired yet,
    /// answers <see cref="AssignmentErrors.AssignmentExpired"/>, as saving answers and submitting do.
    /// </summary>
    internal Result MarkInProgress(DateTimeOffset now)
    {
        if (!AssignmentTransitions.IsAllowed(Status, AssignmentStatus.InProgress))
        {
            return Result.Failure(Status == AssignmentStatus.Expired ? AssignmentErrors.AssignmentExpired : AssignmentErrors.NotAssigned);
        }

        if (ExpiresAt <= now)
        {
            return Result.Failure(AssignmentErrors.AssignmentExpired);
        }

        Status = AssignmentStatus.InProgress;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Called when the participant abandons the session: an in-progress assignment goes back to Assigned so a new
    /// session can start. A cancelled or expired assignment is left as it is.
    /// </summary>
    internal void ReturnToAssigned(DateTimeOffset now)
    {
        if (!AssignmentTransitions.IsAllowed(Status, AssignmentStatus.Assigned))
        {
            return;
        }

        Status = AssignmentStatus.Assigned;
        UpdatedAt = now;
    }

    internal Result Complete(DateTimeOffset now)
    {
        if (!AssignmentTransitions.IsAllowed(Status, AssignmentStatus.Completed))
        {
            return Result.Failure(Status == AssignmentStatus.Expired ? AssignmentErrors.AssignmentExpired : AssignmentErrors.NotInProgress);
        }

        if (ExpiresAt <= now)
        {
            return Result.Failure(AssignmentErrors.AssignmentExpired);
        }

        Status = AssignmentStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private Result Finish(AssignmentStatus status, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (!AssignmentTransitions.IsAllowed(Status, status))
        {
            return Result.Failure(AssignmentErrors.AlreadyFinished);
        }

        Status = status;
        UpdatedAt = now;
        return Result.Success();
    }
}
