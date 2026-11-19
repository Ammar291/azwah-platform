using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Application.Households;

/// <summary>
/// The retention job (D-88), run by the maintenance service. With <see cref="RetentionSettings.Enabled"/> off it does
/// nothing; otherwise each configured period removes what is past it: processed outbox messages older than their
/// period, failed or expired export jobs older than theirs, and households withdrawn longer than the household period,
/// erased through <see cref="EraseHousehold"/> (each in its own scope and transaction, at most
/// <see cref="MaxHouseholdsPerRun"/> a run, only when <c>Erasure:ConsentRecords</c> is set). A household that cannot be
/// erased, whether refused or faulted, is rolled back and counted in <see cref="RetentionReport.Failures"/> (a fault as
/// <c>exception:&lt;type&gt;</c>), and the run goes on; one another run has just erased is skipped. Audit events are
/// never touched.
/// </summary>
public sealed class ApplyRetention(
    RetentionSettings settings,
    ErasureSettings erasure,
    IHouseholdRepository households,
    IOutboxMaintenance outbox,
    IExportJobRepository exportJobs,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider)
{
    public const int MaxHouseholdsPerRun = 50;

    public async Task<Result<RetentionReport>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Success(RetentionReport.Nothing);
        }

        var now = timeProvider.GetUtcNow();
        var messages = settings.ProcessedOutboxDays is { } outboxDays
            ? await outbox.DeleteProcessedBeforeAsync(now.AddDays(-outboxDays), cancellationToken)
            : 0;
        var jobs = settings.FinishedExportJobsDays is { } jobDays
            ? await exportJobs.DeleteFinishedBeforeAsync(now.AddDays(-jobDays), cancellationToken)
            : 0;
        var erased = 0;
        var failures = new List<string>();
        var erasureNotConfigured = settings.WithdrawnHouseholdDays is not null && !erasure.DeletesConsentRecords;
        if (settings.WithdrawnHouseholdDays is { } householdDays && erasure.DeletesConsentRecords)
        {
            var due = await households.ListWithdrawnBeforeAsync(now.AddDays(-householdDays), MaxHouseholdsPerRun, cancellationToken);
            foreach (var household in due)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<EraseHousehold>()
                        .HandleAsync(household.Id, household.Reference, withdrawFirst: false, cancellationToken);
                    if (result.IsSuccess)
                    {
                        erased++;
                    }
                    else if (result.Error != HouseholdErrors.HouseholdNotFound)
                    {
                        failures.Add(result.Error!.Code);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Only the type: a database message can quote row values.
                    failures.Add($"exception:{exception.GetType().Name}");
                }
            }
        }

        return Result.Success(new RetentionReport(erased, messages, jobs, erasureNotConfigured, failures));
    }
}
