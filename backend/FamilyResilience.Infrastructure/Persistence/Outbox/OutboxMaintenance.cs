using FamilyResilience.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Outbox;

public sealed class OutboxMaintenance(FamilyResilienceDbContext dbContext) : IOutboxMaintenance
{
    public Task<int> DeleteProcessedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
        dbContext.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Processed && message.ProcessedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
}
