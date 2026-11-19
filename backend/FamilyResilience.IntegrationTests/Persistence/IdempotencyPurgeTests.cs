using FamilyResilience.Application.Abstractions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class IdempotencyPurgeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Purging_deletes_expired_keys_and_keeps_live_ones()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expired = IdempotencyRecord.Create("subject-a", Guid.NewGuid(), "sessions.submit", Guid.NewGuid(), RequestHash.Of("a"), TestData.Now.AddDays(-2));
        var live = IdempotencyRecord.Create("subject-a", Guid.NewGuid(), "sessions.submit", Guid.NewGuid(), RequestHash.Of("b"), TestData.Now.AddHours(-1));
        await fixture.InScopeAsync(
            services =>
            {
                var store = services.GetRequiredService<IIdempotencyStore>();
                store.Add(expired);
                store.Add(live);
                return Task.CompletedTask;
            },
            cancellationToken);

        await using var scope = fixture.CreateScope();
        var purged = await scope.ServiceProvider.GetRequiredService<PurgeExpiredIdempotencyKeys>().HandleAsync(cancellationToken);

        Assert.True(purged.Value >= 1);
        var remaining = await fixture.QueryAsync(db => db.Set<IdempotencyRecord>()
            .Where(record => record.Id == expired.Id || record.Id == live.Id)
            .Select(record => record.Id)
            .ToListAsync(cancellationToken));
        Assert.Equal([live.Id], remaining);
    }

    [Fact]
    public async Task An_expired_key_is_not_found_even_before_it_is_purged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid();
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IIdempotencyStore>().Add(
                    IdempotencyRecord.Create("subject-b", key, "sessions.submit", Guid.NewGuid(), RequestHash.Of("c"), TestData.Now.AddDays(-2)));
                return Task.CompletedTask;
            },
            cancellationToken);

        await using var scope = fixture.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().FindAsync("subject-b", key, TestData.Now, cancellationToken);

        Assert.Null(found);
    }
}
