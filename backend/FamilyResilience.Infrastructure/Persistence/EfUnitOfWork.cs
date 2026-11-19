using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence;

public sealed class EfUnitOfWork(FamilyResilienceDbContext dbContext) : IUnitOfWork
{
    public async Task<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PersistenceErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (ConstraintErrorMap.TryMap(exception, out var error))
        {
            return Result.Failure(error);
        }
    }

    public async Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already running; explicit transactions do not nest.");
        }

        // Running through the execution strategy keeps this correct if a retrying strategy is enabled later.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async token =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
                var result = await work(token);
                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(token);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                }

                return result;
            },
            cancellationToken);
    }
}
