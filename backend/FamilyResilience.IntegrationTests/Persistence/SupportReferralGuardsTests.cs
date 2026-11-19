using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// Raw SQL that bypasses the domain, to prove PostgreSQL itself refuses a referral without a scored session, an
/// approved referral rule set of the session's version, and the same member's active referral consent.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportReferralGuardsTests(DatabaseFixture fixture)
{
    private static FormattableString InsertSql(
        Guid sessionId, Guid memberId, Guid versionId, Guid ruleSetId, Guid consentId) =>
        $"""
         INSERT INTO support_referrals
             (id, assessment_session_id, family_member_id, instrument_version_id, category_code, referral_rule_set_id,
              consent_record_id, status, created_at, updated_at)
         VALUES (gen_random_uuid(), {sessionId}, {memberId}, {versionId}, 'raw_sql_category', {ruleSetId},
                 {consentId}, 'Open', {TestData.Now.AddMinutes(2)}, {TestData.Now.AddMinutes(2)})
         """;

    private static FormattableString InsertSql(ReferralScenario scenario, Guid? ruleSetId = null, Guid? consentId = null) =>
        InsertSql(
            scenario.Session.Id.Value,
            scenario.Member.Id.Value,
            scenario.Version.Id.Value,
            ruleSetId ?? scenario.ReferralRules.Id.Value,
            consentId ?? scenario.Consent.Id.Value);

    [Fact]
    public async Task A_valid_referral_can_be_inserted_by_raw_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);

        Assert.Equal(1, await fixture.ExecuteSqlAsync(InsertSql(scenario), cancellationToken));
    }

    [Fact]
    public async Task Database_rejects_a_referral_whose_member_is_not_the_sessions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var other = await fixture.ReferralScenarioAsync(cancellationToken);

        // The other member's own consent, so only the session's member copy is wrong.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                InsertSql(scenario.Session.Id.Value, other.Member.Id.Value, scenario.Version.Id.Value, scenario.ReferralRules.Id.Value, other.Consent.Id.Value),
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_support_referrals_session_member");
    }

    [Fact]
    public async Task Database_rejects_a_referral_whose_version_is_not_the_sessions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var other = await fixture.ReferralScenarioAsync(cancellationToken);

        // The other version's own referral rules, so only the session's version copy is wrong.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                InsertSql(scenario.Session.Id.Value, scenario.Member.Id.Value, other.Version.Id.Value, other.ReferralRules.Id.Value, scenario.Consent.Id.Value),
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_support_referrals_session_version");
    }

    [Fact]
    public async Task Database_rejects_a_referral_rule_set_of_another_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var otherRules = (await fixture.ReferralScenarioAsync(cancellationToken)).ReferralRules;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, ruleSetId: otherRules.Id.Value), cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_support_referrals_rule_set_version");
    }

    [Fact]
    public async Task Database_rejects_a_referral_consent_of_another_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var otherConsent = (await fixture.ReferralScenarioAsync(cancellationToken)).Consent;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, consentId: otherConsent.Id.Value), cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_support_referrals_consent_member");
    }

    [Fact]
    public async Task A_missing_consent_is_reported_by_the_consent_foreign_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, consentId: Guid.CreateVersion7()), cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_support_referrals_consent_member");
    }

    [Fact]
    public async Task A_resolved_referral_must_record_when_and_how()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = (await fixture.SavedReferralAsync(cancellationToken)).Referral.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET status = 'Resolved' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_support_referrals_status_shape");
    }

    [Fact]
    public async Task A_referral_needs_a_scored_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (submitted, version, _, member) = await fixture.SubmittedSessionAsync(cancellationToken);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Referral, version, cancellationToken);
        var consent = await fixture.GrantedConsentAsync(
            member, await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                InsertSql(submitted.Id.Value, member.Id.Value, version.Id.Value, rules.Id.Value, consent.Id.Value), cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_needs_a_referral_rule_set_not_a_scoring_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var scoringRules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, scenario.Version, cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, ruleSetId: scoringRules.Id.Value), cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_needs_an_approved_rule_set_not_a_proposed_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var proposed = TestData.Ok(RuleSet.Propose(
            RuleSetKind.Referral, TestData.UniqueCode("proposed_referral"), 1, scenario.Version, """{"synthetic": true}""", TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(proposed);
                return Task.CompletedTask;
            },
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, ruleSetId: proposed.Id.Value), cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_needs_referral_consent_not_participation_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var participation = await fixture.GrantedConsentAsync(
            scenario.Member, await fixture.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken), cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario, consentId: participation.Id.Value), cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_needs_consent_that_has_not_been_withdrawn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var consent = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(scenario.Consent.Id, cancellationToken);
                Assert.NotNull(consent);
                TestData.Ok(consent.Withdraw(FixtureCurrentUser.Subject, TestData.Now.AddMinutes(1)));
            },
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(InsertSql(scenario), cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_starts_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var (sessionId, memberId, versionId) = (scenario.Session.Id.Value, scenario.Member.Id.Value, scenario.Version.Id.Value);
        var (rulesId, consentId) = (scenario.ReferralRules.Id.Value, scenario.Consent.Id.Value);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO support_referrals
                     (id, assessment_session_id, family_member_id, instrument_version_id, category_code, referral_rule_set_id,
                      consent_record_id, status, assigned_service_code, created_at, updated_at)
                 VALUES (gen_random_uuid(), {sessionId}, {memberId}, {versionId}, 'raw_sql_category', {rulesId},
                         {consentId}, 'InProgress', 'raw_sql_service', {TestData.Now}, {TestData.Now})
                 """,
                cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_keeps_its_category_rules_and_creation_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, scenario) = await fixture.SavedReferralAsync(cancellationToken);
        var scoringRulesId = (await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, scenario.Version, cancellationToken)).Id.Value;
        var id = referral.Id.Value;

        // A rule set of the same version passes the foreign key, so only the trigger refuses the swap.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET referral_rule_set_id = {scoringRulesId} WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET category_code = 'other_category' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET created_at = created_at - interval '1 day' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_keeps_its_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, scenario) = await fixture.SavedReferralAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var consent = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(scenario.Consent.Id, cancellationToken);
                Assert.NotNull(consent);
                TestData.Ok(consent.Withdraw(FixtureCurrentUser.Subject, TestData.Now.AddMinutes(3)));
            },
            cancellationToken);
        var renewed = await fixture.GrantedConsentAsync(
            scenario.Member, await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken);
        var (id, renewedId) = (referral.Id.Value, renewed.Id.Value);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET consent_record_id = {renewedId} WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task An_assigned_service_cannot_be_swapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var referral = (await fixture.SavedReferralAsync(cancellationToken)).Referral;
        await ChangeAsync(referral.Id, tracked => tracked.AssignService("synthetic_service", TestData.Now.AddHours(1)), cancellationToken);
        var id = referral.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET assigned_service_code = 'other_service' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task An_assigned_referral_cannot_reopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var referral = (await fixture.SavedReferralAsync(cancellationToken)).Referral;
        await ChangeAsync(referral.Id, tracked => tracked.AssignService("synthetic_service", TestData.Now.AddHours(1)), cancellationToken);
        var id = referral.Id.Value;

        // The trigger runs before the status-shape check, so this isolates the forward-only rule.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET status = 'Open' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_service_is_recorded_only_by_assigning_an_open_referral()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = (await fixture.SavedReferralAsync(cancellationToken)).Referral.Id.Value;
        var closedAt = TestData.Now.AddHours(1);

        // The status-shape check lets a closed referral carry a service, so only the trigger refuses this.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 UPDATE support_referrals
                 SET status = 'Cancelled', resolution_code = 'raised_in_error', resolved_at = {closedAt},
                     assigned_service_code = 'raw_sql_service'
                 WHERE id = {id}
                 """,
                cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_referral_cannot_be_assigned_once_its_consent_is_withdrawn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, scenario) = await fixture.SavedReferralAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var consent = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(scenario.Consent.Id, cancellationToken);
                Assert.NotNull(consent);
                TestData.Ok(consent.Withdraw(FixtureCurrentUser.Subject, TestData.Now.AddMinutes(3)));
            },
            cancellationToken);
        var id = referral.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE support_referrals SET status = 'InProgress', assigned_service_code = 'raw_sql_service' WHERE id = {id}",
                cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_closed_referral_cannot_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var referral = (await fixture.SavedReferralAsync(cancellationToken)).Referral;
        await ChangeAsync(referral.Id, tracked => tracked.Cancel("raised_in_error", null, TestData.Now.AddHours(1)), cancellationToken);
        var id = referral.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE support_referrals SET resolution_code = 'support_provided' WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task Referrals_can_be_deleted_only_by_an_erasure_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = (await fixture.SavedReferralAsync(cancellationToken)).Referral.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM support_referrals WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var erasure = new NpgsqlCommand("SET LOCAL azwah.erasure = 'on'", connection, transaction))
        {
            await erasure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = new NpgsqlCommand("DELETE FROM support_referrals WHERE id = @id", connection, transaction);
        delete.Parameters.AddWithValue("id", id);

        Assert.Equal(1, await delete.ExecuteNonQueryAsync(cancellationToken));
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task An_uncommitted_referral_makes_a_concurrent_consent_withdrawal_wait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // The consent is read without a share lock: the referral trigger takes one itself.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var consent = await scope.ServiceProvider.GetRequiredService<IConsentRecordRepository>().GetAsync(scenario.Consent.Id, token);
                Assert.NotNull(consent);
                scope.ServiceProvider.GetRequiredService<ISupportReferralRepository>().Add(TestData.Ok(SupportReferral.Create(
                    scenario.Session, scenario.ReferralRules, consent, "synthetic_category", TestData.Now.AddMinutes(2))));
                Assert.True((await unitOfWork.CommitAsync(token)).IsSuccess);

                await using var other = new NpgsqlConnection(fixture.ConnectionString);
                await other.OpenAsync(token);
                await using (var timeout = new NpgsqlCommand("SET lock_timeout = '200ms'", other))
                {
                    await timeout.ExecuteNonQueryAsync(token);
                }

                await using var withdraw = new NpgsqlCommand(
                    "UPDATE consent_records SET withdrawn_at = granted_at WHERE id = @id", other);
                withdraw.Parameters.AddWithValue("id", scenario.Consent.Id.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => withdraw.ExecuteNonQueryAsync(token));
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                return Result.Failure<bool>(new Error("tests.rollback", "Leave nothing behind."));
            },
            cancellationToken);

        Assert.Equal("tests.rollback", result.Error?.Code);
    }

    private Task ChangeAsync(SupportReferralId id, Func<SupportReferral, Result> change, CancellationToken cancellationToken) =>
        fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(change(tracked));
            },
            cancellationToken);
}
