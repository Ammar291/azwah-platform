using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Consent;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ConsentPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Approved_policy_and_granted_consent_round_trip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var policy = await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken);
        var consent = await fixture.GrantedConsentAsync(household.Members[0], policy, cancellationToken);

        var (loadedPolicy, loadedConsent) = await fixture.InScopeAsync(
            async services => (
                await services.GetRequiredService<IConsentPolicyVersionRepository>().GetAsync(policy.Id, cancellationToken),
                await services.GetRequiredService<IConsentRecordRepository>().GetAsync(consent.Id, cancellationToken)),
            cancellationToken);

        Assert.NotNull(loadedPolicy);
        Assert.Equal(ConsentPolicyStatus.Approved, loadedPolicy.Status);
        Assert.Equal("approver-subject", loadedPolicy.ApprovedBy);
        Assert.Equal("consent/test/ar", loadedPolicy.DocumentReferenceAr);
        Assert.NotNull(loadedConsent);
        Assert.Equal(household.Id, loadedConsent.HouseholdId);
        Assert.Equal(household.Members[0].Id, loadedConsent.FamilyMemberId);
        Assert.Equal(ConsentType.Referral, loadedConsent.ConsentType);
        Assert.Equal(ConsentCaptureMethod.InApp, loadedConsent.CaptureMethod);
        Assert.Equal(FixtureCurrentUser.Subject, loadedConsent.GrantedBy);
        Assert.True(loadedConsent.IsActive);
    }

    [Fact]
    public async Task A_second_active_consent_of_the_same_type_returns_already_active()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var policy = await fixture.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken);
        await fixture.GrantedConsentAsync(member, policy, cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IConsentRecordRepository>()
            .Add(TestData.Ok(ConsentRecord.Grant(member, policy, ConsentCaptureMethod.PaperForm, null, TestData.Now)));
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(ConsentErrors.AlreadyActive, result.Error);
    }

    [Fact]
    public async Task A_withdrawn_consent_can_be_granted_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var policy = await fixture.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken);
        var consent = await fixture.GrantedConsentAsync(member, policy, cancellationToken);

        await fixture.InScopeAsync(
            async services =>
            {
                var loaded = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(consent.Id, cancellationToken);
                Assert.NotNull(loaded);
                TestData.Ok(loaded.Withdraw(FixtureCurrentUser.Subject, TestData.Now.AddMinutes(1)));
            },
            cancellationToken);
        await fixture.GrantedConsentAsync(member, policy, cancellationToken);

        var all = await fixture.InScopeAsync(
            services => services.GetRequiredService<IConsentRecordRepository>().ListForMemberAsync(member.Id, cancellationToken),
            cancellationToken);
        var active = await fixture.InScopeAsync(
            services => services.GetRequiredService<IConsentRecordRepository>().GetActiveAsync(member.Id, ConsentType.Participation, cancellationToken),
            cancellationToken);

        Assert.Equal(2, all.Count);
        Assert.NotNull(active);
        Assert.NotEqual(consent.Id, active.Id);
    }

    [Fact]
    public async Task Database_rejects_consent_whose_household_is_not_the_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var otherHouseholdId = (await fixture.SavedHouseholdAsync(cancellationToken)).Id.Value;
        var consentId = (await fixture.GrantedConsentAsync(
            household.Members[0], await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE consent_records SET household_id = {otherHouseholdId} WHERE id = {consentId}", cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_rejects_consent_whose_type_differs_from_its_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var consentId = (await fixture.GrantedConsentAsync(
            household.Members[0], await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE consent_records SET consent_type = 'ResearchUse' WHERE id = {consentId}", cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_rejects_a_withdrawal_before_the_grant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var consentId = (await fixture.GrantedConsentAsync(
            household.Members[0], await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE consent_records SET withdrawn_at = granted_at - interval '1 day' WHERE id = {consentId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_consent_records_withdrawn_after_granted");
    }

    [Fact]
    public async Task Share_lock_makes_a_concurrent_withdrawal_wait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var consent = await fixture.GrantedConsentAsync(
            member, await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken);
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var consents = scope.ServiceProvider.GetRequiredService<IConsentRecordRepository>();

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var locked = await consents.GetActiveWithShareLockAsync(member.Id, ConsentType.Referral, token);
                Assert.NotNull(locked);

                await using var other = new NpgsqlConnection(fixture.ConnectionString);
                await other.OpenAsync(token);
                await using (var timeout = new NpgsqlCommand("SET lock_timeout = '200ms'", other))
                {
                    await timeout.ExecuteNonQueryAsync(token);
                }

                await using var withdraw = new NpgsqlCommand(
                    "UPDATE consent_records SET withdrawn_at = granted_at WHERE id = @id", other);
                withdraw.Parameters.AddWithValue("id", consent.Id.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => withdraw.ExecuteNonQueryAsync(token));
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                return Result.Success(true);
            },
            cancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Share_lock_needs_an_explicit_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IConsentRecordRepository>()
            .GetActiveWithShareLockAsync(FamilyResilience.Domain.Members.FamilyMemberId.New(), ConsentType.Referral, cancellationToken));
    }
}
