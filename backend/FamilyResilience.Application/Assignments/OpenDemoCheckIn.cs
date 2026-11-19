using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// DEMONSTRATIONS ONLY (D-144). "New check-in": the caller's household gets a round of its own at once, so a demonstration
/// can answer again and show a second point on the trend chart. The round is a wave made for this tap and left
/// <see cref="WaveStatus.Planned"/>: roster confirmation and everything else that looks for the open wave never see it,
/// and no other household is assigned in it. Who answers is decided as at roster confirmation, by
/// <see cref="IHouseholdAssignmentService"/> with the approved eligibility rules; nothing is assigned here by hand. The
/// check-in is the household assessment's, the questionnaire the trend chart draws (D-132): while one the caller may answer
/// is still waiting or in progress it is returned instead, so a retry or a second tap opens the same one. Household
/// managers only; switched off (404) unless <see cref="DemoToolsSettings.Enabled"/>.
/// </summary>
public sealed class OpenDemoCheckIn(
    DemoToolsSettings settings,
    ICallerAccess access,
    IHouseholdRepository households,
    IHouseholdRunLock runLock,
    IWaveRepository waves,
    IAssignmentReadModel assignments,
    IInstrumentRepository instruments,
    IHouseholdAssignmentService assignmentService,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const string AuditAction = "assignments.demo_check_in.opened";

    /// <summary>Far more open check-ins than one household ever has.</summary>
    private const int MaxOpenLooked = 50;

    /// <summary>Long enough to finish a demonstration; afterwards the round expires like any other.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The demonstration database's waves are <c>demo_wave_1</c> and <c>demo_wave_2</c>; rounds continue the series.</summary>
    public static string WaveCodeFor(int number) => $"demo_wave_{number}";

    public async Task<Result<DemoCheckInResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Failure<DemoCheckInResponse>(DemoToolsErrors.Disabled);
        }

        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is not { } snapshot || !await access.CanManageHouseholdAsync(snapshot.Id, cancellationToken))
        {
            return Result.Failure<DemoCheckInResponse>(HouseholdErrors.HouseholdNotFound);
        }

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        return await unitOfWork.ExecuteInTransactionAsync(token => OpenAsync(snapshot.Id, members, token), cancellationToken);
    }

    private async Task<Result<DemoCheckInResponse>> OpenAsync(
        HouseholdId householdId, IReadOnlyList<FamilyMemberId> members, CancellationToken cancellationToken)
    {
        // Taps for one household queue here, so the second finds the round the first made.
        await runLock.LockAsync(householdId, cancellationToken);
        var household = await households.GetAsync(householdId, cancellationToken);
        if (household is null)
        {
            return Result.Failure<DemoCheckInResponse>(HouseholdErrors.HouseholdNotFound);
        }

        if (household.RosterConfirmedAt is null)
        {
            return Result.Failure<DemoCheckInResponse>(DemoToolsErrors.RosterNotConfirmed);
        }

        var now = timeProvider.GetUtcNow();
        var assessments = (await instruments.ListActiveAsync(cancellationToken))
            .Where(instrument => instrument.Kind == InstrumentKind.HouseholdAssessment)
            .Select(instrument => instrument.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (await OpenCheckInAsync(members, assessments, now, cancellationToken) is { } open)
        {
            return Result.Success(new DemoCheckInResponse(open.Id, open.ActiveSessionId, open.WaveCode, Opened: false));
        }

        var number = await waves.CountAsync(cancellationToken) + 1;
        while (await waves.GetByCodeAsync(WaveCodeFor(number), cancellationToken) is not null)
        {
            number++;
        }

        var wave = Wave.Plan(WaveCodeFor(number), now, now + Window, now);
        if (wave.IsFailure)
        {
            return Result.Failure<DemoCheckInResponse>(wave.Error!);
        }

        waves.Add(wave.Value);
        var made = await unitOfWork.CommitAsync(cancellationToken);
        if (made.IsFailure)
        {
            return Result.Failure<DemoCheckInResponse>(made.Error!);
        }

        var run = await assignmentService.AssignAsync(householdId, wave.Value.Id, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<DemoCheckInResponse>(run.Error!);
        }

        // Nothing for the caller to answer: the transaction rolls back, the round with it.
        var mine = run.Value.Created.FirstOrDefault(assignment =>
            assessments.Contains(assignment.InstrumentCode) && members.Any(member => member.Value == assignment.MemberId));
        if (mine is null)
        {
            return Result.Failure<DemoCheckInResponse>(DemoToolsErrors.NothingToAssign);
        }

        var committed = await AuditedCommit.RunAsync(auditLog, unitOfWork, AuditAction, "household", householdId.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<DemoCheckInResponse>(committed.Error!)
            : Result.Success(new DemoCheckInResponse(mine.AssignmentId, null, wave.Value.Code, Opened: true));
    }

    /// <summary>
    /// The household assessment still to finish: one in progress first, then one waiting, newest first. One whose time has
    /// passed (its wave closed, maintenance has not marked it yet) can take no answers, so it does not count.
    /// </summary>
    private async Task<ParticipantAssignmentResponse?> OpenCheckInAsync(
        IReadOnlyList<FamilyMemberId> members, IReadOnlySet<string> assessments, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var status in new[] { AssignmentStatus.InProgress, AssignmentStatus.Assigned })
        {
            var live = await assignments.ListAsync(members, status, 0, MaxOpenLooked, cancellationToken);
            if (live.FirstOrDefault(assignment => assessments.Contains(assignment.InstrumentCode)
                    && (assignment.ExpiresAt is not { } expires || expires > now)) is { } open)
            {
                return open;
            }
        }

        return null;
    }
}
