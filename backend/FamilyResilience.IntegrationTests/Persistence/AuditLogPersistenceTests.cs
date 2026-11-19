using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
public sealed class AuditLogPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Recorded_event_is_stored_in_audit_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var result = await auditLog.RecordAsync(
                new AuditRecord("system.integration_test.ran", "integration_test", resourceId, AuditOutcome.Succeeded),
                cancellationToken);
            Assert.True(result.IsSuccess);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();
            var stored = await dbContext.AuditEvents.SingleAsync(e => e.ResourceId == resourceId, cancellationToken);
            Assert.Equal("system.integration_test.ran", stored.Action);
            Assert.Equal(AuditActorType.System, stored.ActorType); // no HTTP request: the platform itself is acting
            Assert.Null(stored.ActorId);
            Assert.Equal(TimeSpan.Zero, stored.OccurredAt.Offset);
        }
    }
}
