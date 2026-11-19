using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class UnitOfWorkTests(DatabaseFixture fixture)
{
    private static readonly Error Failed = new("tests.failed", "The test asked for a failure.");

    [Fact]
    public async Task Commit_saves_staged_audit_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();

        await using (var scope = fixture.CreateScope())
        {
            Assert.True(scope.ServiceProvider.GetRequiredService<IAuditLog>().Stage(Record(resourceId)).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        }

        Assert.Equal(1, await CountAsync(resourceId, cancellationToken));
    }

    [Fact]
    public async Task A_successful_result_commits_the_explicit_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();

        await using (var scope = fixture.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    auditLog.Stage(Record(resourceId));
                    var committed = await unitOfWork.CommitAsync(token);
                    return committed.IsSuccess ? Result.Success(42) : Result.Failure<int>(committed.Error!);
                },
                cancellationToken);

            Assert.Equal(42, result.Value);
        }

        Assert.Equal(1, await CountAsync(resourceId, cancellationToken));
    }

    [Fact]
    public async Task A_failed_result_rolls_back_changes_already_saved_in_the_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();

        await using (var scope = fixture.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    auditLog.Stage(Record(resourceId));
                    Assert.True((await unitOfWork.CommitAsync(token)).IsSuccess);
                    return Result.Failure<int>(Failed);
                },
                cancellationToken);

            Assert.Equal(Failed, result.Error);
        }

        Assert.Equal(0, await CountAsync(resourceId, cancellationToken));
    }

    [Fact]
    public async Task An_exception_rolls_back_and_propagates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();

        await using (var scope = fixture.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync<int>(
                async token =>
                {
                    auditLog.Stage(Record(resourceId));
                    await unitOfWork.CommitAsync(token);
                    throw new InvalidOperationException("Simulated bug.");
                },
                cancellationToken));
        }

        Assert.Equal(0, await CountAsync(resourceId, cancellationToken));
    }

    [Fact]
    public async Task Nested_explicit_transactions_are_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(
            token => unitOfWork.ExecuteInTransactionAsync(_ => Task.FromResult(Result.Success(1)), token),
            cancellationToken));
    }

    private static AuditRecord Record(string resourceId) =>
        new("system.unit_of_work.tested", "integration_test", resourceId, AuditOutcome.Succeeded);

    private Task<int> CountAsync(string resourceId, CancellationToken cancellationToken) =>
        fixture.QueryAsync(db => db.AuditEvents.CountAsync(e => e.ResourceId == resourceId, cancellationToken));
}
