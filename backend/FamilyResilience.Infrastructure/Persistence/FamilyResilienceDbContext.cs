using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Privacy;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>
/// The platform's database. Every save also writes the domain events of the tracked aggregates to
/// <c>outbox_messages</c>, in the same transaction, and clears them only when the save succeeds.
/// </summary>
public sealed class FamilyResilienceDbContext(DbContextOptions<FamilyResilienceDbContext> options) : DbContext(options)
{
    public DbSet<Wave> Waves => Set<Wave>();

    public DbSet<AssessmentAssignment> AssessmentAssignments => Set<AssessmentAssignment>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ConsentPolicyVersion> ConsentPolicyVersions => Set<ConsentPolicyVersion>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    public DbSet<Household> Households => Set<Household>();

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<InstrumentVersion> InstrumentVersions => Set<InstrumentVersion>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<PartnerOrganisation> PartnerOrganisations => Set<PartnerOrganisation>();

    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    public DbSet<PrivacyRequest> PrivacyRequests => Set<PrivacyRequest>();

    public DbSet<RoleSnapshot> RoleSnapshots => Set<RoleSnapshot>();

    public DbSet<RuleSet> RuleSets => Set<RuleSet>();

    public DbSet<ScoreResult> ScoreResults => Set<ScoreResult>();

    public DbSet<AssessmentSession> AssessmentSessions => Set<AssessmentSession>();

    public DbSet<SafetyAlert> SafetyAlerts => Set<SafetyAlert>();

    public DbSet<SupportReferral> SupportReferrals => Set<SupportReferral>();

    public DbSet<SupportResource> SupportResources => Set<SupportResource>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = StageOutboxMessages();
        try
        {
            var written = base.SaveChanges(acceptAllChangesOnSuccess);
            pending.Complete();
            return written;
        }
        catch
        {
            pending.Abandon();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = StageOutboxMessages();
        try
        {
            var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            pending.Complete();
            return written;
        }
        catch
        {
            pending.Abandon();
            throw;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        EntityIdConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FamilyResilienceDbContext).Assembly);
        ModelConventions.Apply(modelBuilder);
    }

    private PendingOutbox StageOutboxMessages()
    {
        var sources = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.GetDomainEvents().Count > 0)
            .ToList();
        var messages = sources
            .SelectMany(source => source.GetDomainEvents())
            .Select(DomainEventSerializer.ToOutboxMessage)
            .ToList();
        OutboxMessages.AddRange(messages);
        return new PendingOutbox(this, sources, messages);
    }

    private sealed class PendingOutbox(
        FamilyResilienceDbContext context, List<IHasDomainEvents> sources, List<OutboxMessage> messages)
    {
        public void Complete()
        {
            foreach (var source in sources)
            {
                source.ClearDomainEvents();
            }
        }

        // The events stay on the aggregates, so a retried save writes them again exactly once.
        public void Abandon()
        {
            foreach (var message in messages)
            {
                context.Entry(message).State = EntityState.Detached;
            }
        }
    }
}
