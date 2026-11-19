using FamilyResilience.Application.Assignments;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Closes what the end of a wave left open, run by the maintenance service: every assignment still waiting or in
/// progress past its expiry is marked Expired, and a session still in progress on it is abandoned first
/// (<see cref="ExpireOverdueAssignment"/>, each in its own scope and transaction, the longest overdue first, at most
/// <see cref="MaxPerRun"/> closed a run). One that cannot be closed, whether refused or faulted, is rolled back and
/// counted in <see cref="OverdueAssignmentsReport.Failures"/>, and the run goes on past it: each page leaves out what
/// the run has already tried, so rows that fail every time cannot hold back the ones behind them, and each is tried
/// again at the next run. Saving answers and submitting are refused from the expiry itself, so nothing is accepted in
/// the time before this job runs.
/// </summary>
public sealed class ExpireOverdueAssignments(
    IAssessmentAssignmentRepository assignments,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider)
{
    public const int MaxPerRun = 500;

    public async Task<Result<OverdueAssignmentsReport>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tried = new List<AssessmentAssignmentId>();
        var expired = 0;
        var abandoned = 0;
        var failures = new List<string>();
        while (expired < MaxPerRun)
        {
            // A page never holds more than the run may still close, and never a row this run has tried: the run ends
            // when nothing untried is overdue.
            var due = await assignments.ListOverdueIdsAsync(now, MaxPerRun - expired, tried, cancellationToken);
            if (due.Count == 0)
            {
                break;
            }

            foreach (var assignmentId in due)
            {
                tried.Add(assignmentId);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var closed = await scope.ServiceProvider.GetRequiredService<ExpireOverdueAssignment>()
                        .HandleAsync(assignmentId, cancellationToken);
                    if (closed.IsFailure)
                    {
                        failures.Add(closed.Error!.Code);
                        continue;
                    }

                    expired += closed.Value == OverdueAssignmentOutcome.Unchanged ? 0 : 1;
                    abandoned += closed.Value == OverdueAssignmentOutcome.SessionAbandonedAndExpired ? 1 : 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Only the type: a database message can quote row values.
                    failures.Add($"exception:{exception.GetType().Name}");
                }
            }
        }

        return Result.Success(new OverdueAssignmentsReport(expired, abandoned, failures));
    }
}
