using FamilyResilience.Application.Consent;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class ConsentRecordRepository(FamilyResilienceDbContext dbContext) : IConsentRecordRepository
{
    public void Add(ConsentRecord consentRecord) => dbContext.ConsentRecords.Add(consentRecord);

    public Task<ConsentRecord?> GetAsync(ConsentRecordId id, CancellationToken cancellationToken = default) =>
        dbContext.ConsentRecords.SingleOrDefaultAsync(consent => consent.Id == id, cancellationToken);

    public Task<ConsentRecord?> GetActiveAsync(
        FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default) =>
        dbContext.ConsentRecords.SingleOrDefaultAsync(
            consent => consent.FamilyMemberId == memberId && consent.ConsentType == consentType && consent.WithdrawnAt == null,
            cancellationToken);

    public async Task<ConsentRecord?> GetActiveWithShareLockAsync(
        FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A share lock needs an explicit transaction: call this inside IUnitOfWork.ExecuteInTransactionAsync.");
        }

        var member = memberId.Value;
        var type = consentType.ToString();

        // xmin is a system column, so SELECT * does not return it; EF needs it for the concurrency token.
        var rows = await dbContext.ConsentRecords
            .FromSql(
                $"""
                 SELECT consent_records.*, consent_records.xmin
                 FROM consent_records
                 WHERE family_member_id = {member} AND consent_type = {type} AND withdrawn_at IS NULL
                 FOR SHARE
                 """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.ConsentRecords
            .Where(consent => memberIds.Contains(consent.FamilyMemberId))
            .OrderByDescending(consent => consent.GrantedAt)
            .ThenByDescending(consent => consent.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        dbContext.ConsentRecords.CountAsync(consent => memberIds.Contains(consent.FamilyMemberId), cancellationToken);

    public async Task<IReadOnlyList<ConsentRecord>> ListForMemberAsync(
        FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        await dbContext.ConsentRecords
            .Where(consent => consent.FamilyMemberId == memberId)
            .OrderBy(consent => consent.GrantedAt)
            .ToListAsync(cancellationToken);
}
