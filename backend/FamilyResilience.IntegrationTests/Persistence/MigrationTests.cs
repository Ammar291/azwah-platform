using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
public sealed class MigrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_migration_is_applied_to_an_empty_database_on_startup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(cancellationToken));
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        Assert.Contains(applied, name => name.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddCoreDomainModel", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddDatabaseGuards", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddAssignmentMemberIndex", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddContentDraftsAndOptionKinds", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddScoreResults", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddAssessmentEngineGuards", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddSupportReferrals", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddSupportReferralGuards", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddParticipantIdentity", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddIdempotencyKeysAndSupportRecommendations", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddOutboxScheduling", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddOutboxClaimIndex", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddExportJobs", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddPartnerOrganisations", StringComparison.Ordinal));
        Assert.Contains(applied, name => name.EndsWith("_AddPartnerHandOffGuards", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_outbox_claim_query_has_a_partial_index_of_pending_messages_by_type_and_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var definition = await factory.QueryAsync(db => db.Database
            .SqlQuery<string>(
                $"""
                 SELECT indexdef AS "Value" FROM pg_indexes
                 WHERE schemaname = current_schema() AND indexname = 'ix_outbox_messages_pending_type_occurred_at'
                 """)
            .SingleOrDefaultAsync(cancellationToken));

        Assert.NotNull(definition);
        Assert.EndsWith(
            "ON public.outbox_messages USING btree (type, occurred_at) WHERE ((status)::text = 'Pending'::text)", definition, StringComparison.Ordinal);
    }
}
