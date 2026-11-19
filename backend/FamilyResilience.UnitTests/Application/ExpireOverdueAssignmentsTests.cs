using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application;

/// <summary>
/// The maintenance run over overdue assignments, with in-memory storage: the listing is ordered as the database orders
/// it (longest overdue first), so rows that fail every time sit at the front of every run.
/// </summary>
public sealed class ExpireOverdueAssignmentsTests
{
    private static readonly DateTimeOffset RunAt = Now.AddDays(2);

    [Fact]
    public async Task Assignments_that_fail_every_time_do_not_hold_back_the_ones_behind_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A full run's worth of rows that fault whenever they are loaded, all longer overdue than the healthy one.
        var failing = Enumerable.Range(0, ExpireOverdueAssignments.MaxPerRun)
            .Select(index => (AssessmentAssignmentId.New(), Now.AddHours(-index - 1)))
            .ToList();
        var healthy = Healthy();
        var assignments = new OverdueAssignments(failing, healthy);

        var report = Ok(await Job(assignments).HandleAsync(cancellationToken));

        Assert.Equal((1, 0), (report.AssignmentsExpired, report.SessionsAbandoned));
        Assert.Equal(AssignmentStatus.Expired, healthy.Status);
        Assert.Equal(ExpireOverdueAssignments.MaxPerRun, report.Failures.Count);
        Assert.All(report.Failures, failure => Assert.Equal("exception:InvalidOperationException", failure));
        // Each failing row was tried once in the run, not again and again.
        Assert.Equal(ExpireOverdueAssignments.MaxPerRun + 1, assignments.Loaded.Count);
        Assert.Equal(assignments.Loaded.Count, assignments.Loaded.Distinct().Count());
    }

    [Fact]
    public async Task A_run_closes_at_most_its_limit_and_the_next_run_takes_the_rest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var healthy = Enumerable.Range(0, ExpireOverdueAssignments.MaxPerRun + 3).Select(_ => Healthy()).ToList();
        var assignments = new OverdueAssignments([], [.. healthy]);

        var first = Ok(await Job(assignments).HandleAsync(cancellationToken));
        var second = Ok(await Job(assignments).HandleAsync(cancellationToken));

        Assert.Equal((ExpireOverdueAssignments.MaxPerRun, 3), (first.AssignmentsExpired, second.AssignmentsExpired));
        Assert.Empty(first.Failures);
        Assert.All(healthy, assignment => Assert.Equal(AssignmentStatus.Expired, assignment.Status));
    }

    /// <summary>A real assignment, assigned now and due to expire tomorrow, so it is overdue when the run happens.</summary>
    private static AssessmentAssignment Healthy()
    {
        var version = ApprovedVersion();
        return Ok(AssessmentAssignment.Assign(
            NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, Now.AddDays(1), Now));
    }

    private static ExpireOverdueAssignments Job(OverdueAssignments assignments)
    {
        var clock = new FixedTimeProvider(RunAt);
        var unitOfWork = new FakeUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IAssessmentAssignmentRepository>(assignments)
            .AddSingleton<IAssessmentSessionRepository>(new NoSessions())
            .AddSingleton<ISessionLock>(new NoLock())
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .AddSingleton<IAuditLog>(new AuditLog(new FakeAuditEventRepository(), unitOfWork, new FakeCurrentUser(false, null) { IsSystem = true }, clock))
            .AddSingleton<TimeProvider>(clock)
            .AddScoped<ExpireOverdueAssignment>()
            .BuildServiceProvider();
        return new ExpireOverdueAssignments(assignments, services.GetRequiredService<IServiceScopeFactory>(), clock);
    }

    /// <summary>Overdue rows in the database's order. A failing row has only an identifier and faults whenever it is loaded.</summary>
    private sealed class OverdueAssignments(
        IReadOnlyList<(AssessmentAssignmentId Id, DateTimeOffset ExpiresAt)> failing,
        params AssessmentAssignment[] healthy) : IAssessmentAssignmentRepository
    {
        public List<AssessmentAssignmentId> Loaded { get; } = [];

        public Task<AssessmentAssignment?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default)
        {
            Loaded.Add(id);
            return failing.Any(row => row.Id == id)
                ? throw new InvalidOperationException("This row faults every time it is loaded.")
                : Task.FromResult(healthy.SingleOrDefault(assignment => assignment.Id == id));
        }

        public Task<IReadOnlyList<AssessmentAssignmentId>> ListOverdueIdsAsync(
            DateTimeOffset now, int limit, IReadOnlyCollection<AssessmentAssignmentId> excluding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssessmentAssignmentId>>(failing
                .Concat(healthy.Where(assignment => assignment.IsOverdueAt(now))
                    .Select(assignment => (Id: assignment.Id, ExpiresAt: assignment.ExpiresAt ?? now)))
                .Where(row => row.ExpiresAt <= now && !excluding.Contains(row.Id))
                .OrderBy(row => row.ExpiresAt)
                .ThenBy(row => row.Id.Value)
                .Select(row => row.Id)
                .Take(limit)
                .ToList());

        public void Add(AssessmentAssignment assignment) => throw new NotSupportedException();

        public Task<bool> ExistsLiveAsync(
            FamilyMemberId memberId, InstrumentVersionId versionId, InstrumentSectionId? sectionId, WaveId waveId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AssessmentAssignment>> ListForMemberAsync(
            FamilyMemberId memberId, WaveId waveId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AssessmentAssignment>> ListOpenForMemberAsync(
            FamilyMemberId memberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoSessions : IAssessmentSessionRepository
    {
        public Task<AssessmentSessionId?> FindLiveIdForAssignmentAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AssessmentSessionId?>(null);

        public Task<AssessmentSession?> GetLiveForAssignmentAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AssessmentSession?>(null);

        public void Add(AssessmentSession session) => throw new NotSupportedException();

        public Task<AssessmentSession?> GetAsync(AssessmentSessionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AssessmentSessionId>> ListInProgressIdsForMemberAsync(
            FamilyMemberId memberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AssessmentSession>> ListForMembersAsync(
            IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoLock : ISessionLock
    {
        public Task LockAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
