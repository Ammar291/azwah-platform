using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Erasing a household (an approved request or the end of an approved period, D-88): every row about it goes in one
/// transaction, in an order the database guards accept; other households and the audit trail stay.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class HouseholdErasureTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private WebApplicationFactory<Program> Configured(string? consentRecords = "delete") =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Erasure:ConsentRecords"] = consentRecords })));

    private static async Task<HouseholdErasureResult> EraseAsync(
        WebApplicationFactory<Program> host, Guid householdId, string reference, bool withdrawFirst = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<EraseHousehold>()
            .HandleAsync(householdId, reference, withdrawFirst, TestContext.Current.CancellationToken);
        return new HouseholdErasureResult(result.IsSuccess ? result.Value : null, result.Error?.Code);
    }

    private sealed record HouseholdErasureResult(IReadOnlyDictionary<string, int>? Counts, string? Error);

    /// <summary>A household with a scored, routed session (recommendation and referral), consents and a second member.</summary>
    private async Task<(Enrolment Family, Guid SessionId)> ScoredFamilyAsync(CancellationToken cancellationToken)
    {
        var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        await family.Client.GrantConsentAsync(family.Self, ConsentType.Referral, cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(family, api, family.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(family.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return (family, sessionId);
    }

    private static async Task WithdrawAsync(Enrolment family, CancellationToken cancellationToken)
    {
        using var withdrawn = await family.Client.PatchJsonAsync($"/api/v1/households/{family.Household.Id}", new { status = "withdrawn" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
    }

    /// <summary>
    /// Rows that still mention the household, its members, users or sessions, per table. <paramref name="user"/> is the
    /// manager's account ID, read before the erasure removes the account.
    /// </summary>
    private async Task<Dictionary<string, long>> RemainingAsync(Enrolment family, Guid sessionId, PlatformUserId user)
    {
        var connection = Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        var members = family.Members.Select(member => member.Id).ToArray();
        var checks = new Dictionary<string, string>
        {
            ["households"] = "SELECT count(*) FROM households WHERE id = @h",
            ["family_members"] = "SELECT count(*) FROM family_members WHERE household_id = @h",
            ["household_memberships"] = "SELECT count(*) FROM household_memberships WHERE household_id = @h",
            ["platform_users"] = "SELECT count(*) FROM platform_users WHERE subject = @s",
            ["role_snapshots"] = "SELECT count(*) FROM role_snapshots WHERE subject = @s",
            ["consent_records"] = "SELECT count(*) FROM consent_records WHERE household_id = @h",
            ["assessment_assignments"] = "SELECT count(*) FROM assessment_assignments WHERE family_member_id = ANY(@m)",
            ["assessment_sessions"] = "SELECT count(*) FROM assessment_sessions WHERE family_member_id = ANY(@m)",
            ["assessment_responses"] = "SELECT count(*) FROM assessment_responses WHERE assessment_session_id = @session",
            ["score_results"] = "SELECT count(*) FROM score_results WHERE assessment_session_id = @session",
            ["support_recommendations"] = "SELECT count(*) FROM support_recommendations WHERE family_member_id = ANY(@m)",
            ["support_referrals"] = "SELECT count(*) FROM support_referrals WHERE family_member_id = ANY(@m)",
            ["safety_alerts"] = "SELECT count(*) FROM safety_alerts WHERE household_id = @h OR family_member_id = ANY(@m) OR assessment_session_id = @session OR platform_user_id = @user",
            ["idempotency_keys"] = "SELECT count(*) FROM idempotency_keys WHERE subject = @s OR resource_id = @session",
            ["outbox_messages"] = "SELECT count(*) FROM outbox_messages WHERE payload::text LIKE '%' || @h::text || '%' OR payload::text LIKE '%' || @session::text || '%'",
        };
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(TestContext.Current.CancellationToken);
        var remaining = new Dictionary<string, long>();
        foreach (var (table, sql) in checks)
        {
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("h", family.Household.Id);
            command.Parameters.AddWithValue("s", family.Subject);
            command.Parameters.AddWithValue("m", members);
            command.Parameters.AddWithValue("session", sessionId);
            command.Parameters.AddWithValue("user", user.Value);
            remaining[table] = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        return remaining;
    }

    [Fact]
    public async Task Erasing_a_withdrawn_household_removes_every_row_about_it_and_keeps_the_audit_trail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (family, sessionId) = await ScoredFamilyAsync(cancellationToken);
        using var _ = family;
        await WithdrawAsync(family, cancellationToken);
        var user = await UserIdAsync(family, cancellationToken);
        var before = await RemainingAsync(family, sessionId, user);
        var auditBefore = await Factory.QueryAsync(db => db.AuditEvents.CountAsync(audit => audit.ActorId == family.Subject, cancellationToken));
        await using var host = Configured();

        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference);

        Assert.Null(erased.Error);
        Assert.True(before["support_referrals"] == 1 && before["score_results"] == 1 && before["assessment_responses"] > 0);
        Assert.All(await RemainingAsync(family, sessionId, user), table => Assert.True(table.Value == 0, $"{table.Key} still has {table.Value} rows"));
        Assert.Equal(before["assessment_responses"], erased.Counts!["assessment_responses"]);
        Assert.Equal(auditBefore, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(audit => audit.ActorId == family.Subject, cancellationToken)));
        var recorded = await Factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .SingleAsync(audit => audit.Action == "households.household.erased" && audit.ResourceId == family.Household.Id.ToString(), cancellationToken));
        Assert.Equal((AuditActorType.System, AuditOutcome.Succeeded), (recorded.ActorType, recorded.Outcome));
    }

    private async Task<PlatformUserId> UserIdAsync(Enrolment family, CancellationToken cancellationToken) =>
        await Factory.QueryAsync(db => db.PlatformUsers.Where(user => user.Subject == family.Subject).Select(user => user.Id).SingleAsync(cancellationToken));

    private async Task<SafetyAlert> SavedAlertAsync(SafetyAlert alert, CancellationToken cancellationToken)
    {
        await Factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISafetyAlertRepository>().Add(alert);
                return Task.CompletedTask;
            },
            cancellationToken);
        return alert;
    }

    [Fact]
    public async Task Erasing_a_household_removes_its_safety_alerts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (family, sessionId) = await ScoredFamilyAsync(cancellationToken);
        using var _ = family;
        var householdId = HouseholdId.From(family.Household.Id);
        var self = FamilyMemberId.From(family.Self);
        var user = await UserIdAsync(family, cancellationToken);
        await SavedAlertAsync(
            SafetyAlert.Raise(
                new SafetyAlertSubject(user, householdId, self), SafetyAlertSource.AssistantChat,
                SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", "en", null, TestData.Now),
            cancellationToken);
        await SavedAlertAsync(
            SafetyAlert.Raise(
                new SafetyAlertSubject(null, householdId, self), SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore,
                SafetyDetector.ScoreBand, "vulnerable", null, AssessmentSessionId.From(sessionId), TestData.Now),
            cancellationToken);
        await WithdrawAsync(family, cancellationToken);
        var before = await RemainingAsync(family, sessionId, user);
        await using var host = Configured();

        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference);

        Assert.Null(erased.Error);
        Assert.Equal(2, before["safety_alerts"]);
        Assert.Equal(2, erased.Counts!["safety_alerts"]);
        Assert.All(await RemainingAsync(family, sessionId, user), table => Assert.True(table.Value == 0, $"{table.Key} still has {table.Value} rows"));
    }

    /// <summary>An alert raised while the person belonged to no household names only their account; erasing the account erases it.</summary>
    [Fact]
    public async Task Erasure_removes_alerts_that_name_only_the_erased_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var user = await UserIdAsync(family, cancellationToken);
        var alert = await SavedAlertAsync(
            SafetyAlert.Raise(
                new SafetyAlertSubject(user, null, null), SafetyAlertSource.SupportSearch, SafetyConcern.AbuseOrDanger,
                SafetyDetector.PhraseScreen, "danger.threatened", "ar", null, TestData.Now),
            cancellationToken);
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured();

        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference);

        Assert.Null(erased.Error);
        Assert.Equal((1, 1, 1), (erased.Counts!["safety_alerts"], erased.Counts["platform_users"], erased.Counts["households"]));
        Assert.False(await Factory.QueryAsync(db => db.SafetyAlerts.AnyAsync(row => row.Id == alert.Id, cancellationToken)));
        Assert.False(await Factory.QueryAsync(db => db.PlatformUsers.AnyAsync(account => account.Id == user, cancellationToken)));
    }

    /// <summary>
    /// A person whose account was linked to a member of the erased household, and who now manages a household of their
    /// own, keeps the account. Their alerts outside the erased household stay; the one about the erased household goes.
    /// </summary>
    [Fact]
    public async Task Erasure_keeps_an_account_and_its_alerts_when_the_user_belongs_to_another_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var erasedHousehold = HouseholdId.From(family.Household.Id);
        var parent = FamilyMemberId.From(family.Aged(68));
        var other = TestData.Ok(PlatformUser.Register(ApiClients.NewSubject("other"), TestData.Now));
        await Factory.InScopeAsync(
            async services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(other);
                var household = await services.GetRequiredService<IHouseholdRepository>().GetAsync(erasedHousehold, cancellationToken);
                Assert.NotNull(household);
                TestData.Ok(household.LinkAccount(other.Id, parent, TestData.Now));
            },
            cancellationToken);
        // The parent leaves (their membership ends) and enrols a household of their own.
        await Factory.InScopeAsync(
            async services =>
            {
                var household = await services.GetRequiredService<IHouseholdRepository>().GetAsync(erasedHousehold, cancellationToken);
                Assert.NotNull(household);
                TestData.Ok(household.DeactivateMember(parent, TestData.Now));
            },
            cancellationToken);
        var own = TestData.Ok(Household.Enrol(HouseholdReference.Generate(), [TestData.Head()], 0, other.Id, TestData.Now));
        await Factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(own);
                return Task.CompletedTask;
            },
            cancellationToken);
        var aboutErased = await SavedAlertAsync(Alert(new SafetyAlertSubject(other.Id, erasedHousehold, parent), SafetyAlertSource.AssistantChat), cancellationToken);
        var accountOnly = await SavedAlertAsync(Alert(new SafetyAlertSubject(other.Id, null, null), SafetyAlertSource.SupportSearch), cancellationToken);
        var aboutOwn = await SavedAlertAsync(Alert(new SafetyAlertSubject(other.Id, own.Id, own.Members[0].Id), SafetyAlertSource.ConsultantVoice), cancellationToken);
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured();

        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference);

        Assert.Null(erased.Error);
        Assert.Equal((1, 1), (erased.Counts!["safety_alerts"], erased.Counts["platform_users"]));
        var remaining = (await Factory.QueryAsync(db => db.SafetyAlerts
            .Where(row => row.UserId == other.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken))).Select(id => id.Value).ToHashSet();
        Assert.Equal(new HashSet<Guid> { accountOnly.Id.Value, aboutOwn.Id.Value }, remaining);
        Assert.DoesNotContain(aboutErased.Id.Value, remaining);
        Assert.True(await Factory.QueryAsync(db => db.PlatformUsers.AnyAsync(account => account.Id == other.Id, cancellationToken)));
    }

    private static SafetyAlert Alert(SafetyAlertSubject subject, SafetyAlertSource source) =>
        SafetyAlert.Raise(subject, source, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", "en", null, TestData.Now);

    [Fact]
    public async Task Erasing_one_household_leaves_another_untouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (erased, erasedSession) = await ScoredFamilyAsync(cancellationToken);
        var (kept, keptSession) = await ScoredFamilyAsync(cancellationToken);
        using var _ = erased;
        using var __ = kept;
        await WithdrawAsync(erased, cancellationToken);
        var keptUser = await UserIdAsync(kept, cancellationToken);
        var keptBefore = await RemainingAsync(kept, keptSession, keptUser);
        await using var host = Configured();

        Assert.Null((await EraseAsync(host, erased.Household.Id, erased.Household.Reference)).Error);

        Assert.Equal(keptBefore, await RemainingAsync(kept, keptSession, keptUser));
        using var stillThere = await kept.Client.GetAsync($"/api/v1/sessions/{keptSession}/result", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task An_active_household_is_erased_only_when_the_operator_asks_to_withdraw_it_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        await using var host = Configured();

        var refused = await EraseAsync(host, family.Household.Id, family.Household.Reference);
        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference, withdrawFirst: true);

        Assert.Equal("households.erasure_needs_withdrawal", refused.Error);
        Assert.Null(erased.Error);
        Assert.False(await Factory.QueryAsync(db => db.Households.AnyAsync(household => household.Id == HouseholdId.From(family.Household.Id), cancellationToken)));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "households.household.withdrawn" && audit.ResourceId == family.Household.Id.ToString(), cancellationToken)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("retain_evidence")]
    [InlineData("keep")]
    public async Task Erasure_refuses_to_run_until_the_consent_record_rule_is_set_to_a_built_option(string? setting)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured(setting);

        var refused = await EraseAsync(host, family.Household.Id, family.Household.Reference);

        Assert.Equal("households.erasure_not_configured", refused.Error);
        Assert.True(await Factory.QueryAsync(db => db.Households.AnyAsync(household => household.Id == HouseholdId.From(family.Household.Id), cancellationToken)));
    }

    [Fact]
    public async Task Erasure_needs_the_households_own_reference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        using var other = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured();

        var refused = await EraseAsync(host, family.Household.Id, other.Household.Reference);
        var missing = await EraseAsync(host, Guid.NewGuid(), family.Household.Reference);

        Assert.Equal("households.erasure_reference_mismatch", refused.Error);
        Assert.Equal("households.household_not_found", missing.Error);
    }

    [Fact]
    public async Task Erasing_a_household_removes_the_account_of_a_member_who_signs_in_on_their_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        using var seen = await linked.Parent.GetAsync("/api/v1/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        await WithdrawAsync(linked.Manager, cancellationToken);
        string[] subjects = [linked.Manager.Subject, linked.ParentSubject];
        await using var host = Configured();

        var erased = await EraseAsync(host, linked.Manager.Household.Id, linked.Manager.Household.Reference);

        Assert.Null(erased.Error);
        Assert.Equal(2, erased.Counts!["household_memberships"]);
        Assert.Equal(2, erased.Counts["platform_users"]);
        Assert.False(await Factory.QueryAsync(db => db.PlatformUsers.AnyAsync(user => subjects.Contains(user.Subject), cancellationToken)));
        Assert.False(await Factory.QueryAsync(db => db.RoleSnapshots.AnyAsync(snapshot => subjects.Contains(snapshot.Subject), cancellationToken)));
    }

    [Fact]
    public async Task Two_erasures_of_one_household_at_once_erase_it_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured();

        var results = await Task.WhenAll(
            EraseAsync(host, family.Household.Id, family.Household.Reference),
            EraseAsync(host, family.Household.Id, family.Household.Reference));

        Assert.Single(results, result => result.Error is null);
        Assert.Single(results, result => result.Error == "households.household_not_found");
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "households.household.erased" && audit.ResourceId == family.Household.Id.ToString(), cancellationToken)));
    }

    /// <summary>
    /// Every table with a foreign key into the erased rows (households and platform users, and whatever points at those,
    /// transitively) is either deleted by the erasure or removed by an ON DELETE CASCADE. A new table that is neither
    /// makes erasure fail on the foreign key in production, so this test fails first.
    /// </summary>
    [Fact]
    public async Task Every_table_that_points_at_erased_rows_is_part_of_the_erasure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        await WithdrawAsync(family, cancellationToken);
        await using var host = Configured();
        var erased = await EraseAsync(host, family.Household.Id, family.Household.Reference);
        Assert.Null(erased.Error);

        var keys = new List<(string Child, string Parent, bool Cascades)>();
        var connection = Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT conrelid::regclass::text, confrelid::regclass::text, confdeltype = 'c' FROM pg_constraint WHERE contype = 'f'", db);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
            }
        }

        var reached = new HashSet<string>(StringComparer.Ordinal) { "households", "platform_users" };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var key in keys.Where(key => reached.Contains(key.Parent)).ToList())
            {
                grew |= reached.Add(key.Child);
            }
        }

        var missing = reached
            .Where(table => !erased.Counts!.ContainsKey(table))
            .Where(table => keys.Any(key => key.Child == table && reached.Contains(key.Parent) && !key.Cascades))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, $"Add these tables to HouseholdErasure: {string.Join(", ", missing)}");
        Assert.Contains("score_contributions", reached);
    }

    [Fact]
    public async Task A_failure_part_way_erases_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (family, sessionId) = await ScoredFamilyAsync(cancellationToken);
        using var _ = family;
        await WithdrawAsync(family, cancellationToken);
        var user = await UserIdAsync(family, cancellationToken);
        var before = await RemainingAsync(family, sessionId, user);
        await using var host = Configured().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IHouseholdErasure>(provider => new FailingAfterDeletes(
                ActivatorUtilities.CreateInstance<Infrastructure.Persistence.Erasure.HouseholdErasure>(provider)))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => EraseAsync(host, family.Household.Id, family.Household.Reference));

        Assert.Equal(before, await RemainingAsync(family, sessionId, user));
    }

    /// <summary>Does every delete, then fails, as an infrastructure fault would.</summary>
    private sealed class FailingAfterDeletes(IHouseholdErasure inner) : IHouseholdErasure
    {
        public Task<bool> LockAsync(HouseholdId householdId, CancellationToken cancellationToken) => inner.LockAsync(householdId, cancellationToken);

        public async Task<IReadOnlyDictionary<string, int>> EraseAsync(HouseholdId householdId, CancellationToken cancellationToken)
        {
            await inner.EraseAsync(householdId, cancellationToken);
            throw new InvalidOperationException("Simulated fault after the deletes.");
        }
    }
}
