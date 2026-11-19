using System.Net;
using FamilyResilience.Api.Identity;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence.Repositories;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Safeguarding;

/// <summary>
/// The staff safety-alerts API (D-135, D-137) and the signal recorder against PostgreSQL. Alerts are seeded through the
/// repository or the real recorder for a participant enrolled straight into the database. The database is shared, so
/// list checks look only at the alerts each test made, and summary checks at the change a test made to the counts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SafetyAlertsEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/admin/safety-alerts";
    private const string Staff = "staff-subject";

    private static DateTimeOffset Now => TestData.Now;

    private sealed record Family(PlatformUser User, Household Household)
    {
        public FamilyMember Head => Household.Members[0];

        public SafetyAlertSubject Subject => new(User.Id, Household.Id, Head.Id);

        /// <summary>A score alert names the household and the member who answered, never a user.</summary>
        public SafetyAlertSubject ScoreSubject => new(null, Household.Id, Head.Id);

        public CallerRecord Caller => new(User.Id, Household);
    }

    private async Task<Family> EnrolledAsync(CancellationToken cancellationToken)
    {
        var user = TestData.Ok(PlatformUser.Register(ApiClients.NewSubject("participant"), Now));
        var household = TestData.Ok(Household.Enrol(HouseholdReference.Generate(), [TestData.Head()], 0, user.Id, Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);
        return new Family(user, household);
    }

    private static string CodeOf(SafetyConcern concern) => concern switch
    {
        SafetyConcern.SuicideOrSelfHarm => "suicide.intent",
        SafetyConcern.HarmToOthers => "harm_others.intent",
        SafetyConcern.AbuseOrDanger => "abuse.being_hurt",
        _ => "vulnerable",
    };

    /// <summary>Saves an alert through the repository, then applies <paramref name="then"/> in a second commit.</summary>
    private async Task<SafetyAlert> RaisedAsync(
        SafetyAlertSubject subject, SafetyConcern concern, SafetyAlertSource source, DateTimeOffset at, Func<SafetyAlert, Result>? then = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alert = concern == SafetyConcern.LowResilienceScore
            ? SafetyAlert.Raise(subject, source, concern, SafetyDetector.ScoreBand, CodeOf(concern), null, null, at)
            : SafetyAlert.Raise(subject, source, concern, SafetyDetector.PhraseScreen, CodeOf(concern), "en", null, at);
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISafetyAlertRepository>().Add(alert);
                return Task.CompletedTask;
            },
            cancellationToken);
        if (then is not null)
        {
            await factory.InScopeAsync(
                async services =>
                {
                    var tracked = await services.GetRequiredService<ISafetyAlertRepository>().GetAsync(alert.Id, cancellationToken);
                    TestData.Ok(then(tracked!));
                },
                cancellationToken);
        }

        return alert;
    }

    private static Result Acknowledged(SafetyAlert alert) => alert.Acknowledge(Staff, Now);

    private static Result Resolved(SafetyAlert alert) => alert.Resolve(SafetyAlertResolution.FalseAlarm, Staff, Now);

    private static async Task<List<AdminSafetyAlertResponse>> ListAllAsync(HttpClient client, string query)
    {
        var items = new List<AdminSafetyAlertResponse>();
        for (var page = 1; ; page++)
        {
            using var response = await client.GetAsync($"{Route}?{query}&page={page}&pageSize=100", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.ReadAsync<PagedResponse<AdminSafetyAlertResponse>>(TestContext.Current.CancellationToken);
            items.AddRange(body.Items);
            if (items.Count >= body.TotalCount)
            {
                return items;
            }
        }
    }

    private static async Task<SafetyAlertSummaryResponse> SummaryAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"{Route}/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SafetyAlertSummaryResponse>(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> AcknowledgeAsync(HttpClient client, Guid alertId) =>
        client.PostAsync($"{Route}/{alertId}/acknowledge", null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient client, Guid alertId, string resolution) =>
        client.PostJsonAsync($"{Route}/{alertId}/resolve", new { resolution }, TestContext.Current.CancellationToken);

    private Task<int> AuditCountAsync(string action, string? actor = null, string? resourceId = null) =>
        factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && (actor == null || audit.ActorId == actor) && (resourceId == null || audit.ResourceId == resourceId),
            TestContext.Current.CancellationToken));

    private Task<SafetyAlert> StoredAsync(SafetyAlertId id) =>
        factory.QueryAsync(db => db.SafetyAlerts.AsNoTracking().SingleAsync(alert => alert.Id == id, TestContext.Current.CancellationToken));

    [Fact]
    public async Task List_returns_alerts_for_a_system_administrator_in_queue_order()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var newestCritical = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now.AddMinutes(-1));
        var olderCritical = await RaisedAsync(family.Subject, SafetyConcern.AbuseOrDanger, SafetyAlertSource.SupportSearch, Now.AddHours(-2));
        var high = await RaisedAsync(family.ScoreSubject, SafetyConcern.LowResilienceScore, SafetyAlertSource.HouseholdAssessment, Now);
        var acknowledged = await RaisedAsync(
            family.Subject, SafetyConcern.HarmToOthers, SafetyAlertSource.ConsultantVoice, Now.AddMinutes(-30), Acknowledged);
        var resolved = await RaisedAsync(
            family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.ConsultantVoice, Now.AddMinutes(-5), Resolved);
        var ours = new[] { newestCritical, olderCritical, high, acknowledged, resolved }.Select(alert => alert.Id.Value).ToHashSet();
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);

        var listed = await ListAllAsync(admin, "status=");

        Assert.Equal(
            [newestCritical.Id.Value, olderCritical.Id.Value, high.Id.Value, acknowledged.Id.Value, resolved.Id.Value],
            listed.Where(item => ours.Contains(item.Id)).Select(item => item.Id));
        Assert.Equal(
            new AdminSafetyAlertResponse(
                newestCritical.Id.Value, family.Household.Reference.Value, AdminSafetyAlertResponse.HeadOfHousehold,
                SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical, SafetyDetector.PhraseScreen,
                "suicide.intent", "en", 1, Now.AddMinutes(-1), Now.AddMinutes(-1), SafetyAlertStatus.Open, null, null, null),
            listed.Single(item => item.Id == newestCritical.Id.Value));
        var closed = listed.Single(item => item.Id == resolved.Id.Value);
        Assert.Equal((SafetyAlertStatus.Resolved, SafetyAlertResolution.FalseAlarm), (closed.Status, closed.Resolution));
        Assert.True(await AuditCountAsync("safeguarding.alerts.read", subject) >= 1);
    }

    [Fact]
    public async Task List_filters_by_unresolved_status_and_category()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var open = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now);
        var acknowledged = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.ConsultantVoice, Now, Acknowledged);
        var resolved = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.SupportSearch, Now, Resolved);
        var otherConcern = await RaisedAsync(family.Subject, SafetyConcern.HarmToOthers, SafetyAlertSource.AssistantChat, Now);
        var ours = new[] { open, acknowledged, resolved, otherConcern }.Select(alert => alert.Id.Value).ToHashSet();
        using var client = factory.AsSystemAdministrator();

        var unresolved = await ListAllAsync(client, "status=unresolved&category=suicide_or_self_harm");
        var resolvedOnly = await ListAllAsync(client, "status=resolved");

        Assert.All(unresolved, item => Assert.Equal(SafetyConcern.SuicideOrSelfHarm, item.Category));
        Assert.All(unresolved, item => Assert.NotEqual(SafetyAlertStatus.Resolved, item.Status));
        Assert.Equal(
            new[] { open.Id.Value, acknowledged.Id.Value }.Order(),
            unresolved.Where(item => ours.Contains(item.Id)).Select(item => item.Id).Order());
        Assert.All(resolvedOnly, item => Assert.Equal(SafetyAlertStatus.Resolved, item.Status));
        Assert.Equal([resolved.Id.Value], resolvedOnly.Where(item => ours.Contains(item.Id)).Select(item => item.Id));
    }

    [Theory]
    [InlineData("status=bogus")]
    [InlineData("status=1")]
    [InlineData("status=Unresolved_")]
    public async Task List_rejects_an_unknown_status_with_400(string query)
    {
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);

        using var response = await admin.GetAsync($"{Route}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["status"], await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await AuditCountAsync("safeguarding.alerts.read", subject));
    }

    [Theory]
    [InlineData("category=sadness", "category")]
    [InlineData("category=critical", "category")]
    [InlineData("pageSize=1000", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task List_rejects_an_unknown_category_or_an_unbounded_page_with_400(string query, string key)
    {
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"{Route}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([key], await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_requires_sign_in_401()
    {
        using var anonymous = factory.As(null);

        using var list = await anonymous.GetAsync(Route, TestContext.Current.CancellationToken);
        using var summary = await anonymous.GetAsync($"{Route}/summary", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (list.StatusCode, summary.StatusCode));
    }

    [Fact]
    public async Task List_refuses_a_programme_administrator_403()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now);
        using var programme = factory.AsProgramAdministrator();

        using var list = await programme.GetAsync(Route, TestContext.Current.CancellationToken);
        using var summary = await programme.GetAsync($"{Route}/summary", TestContext.Current.CancellationToken);
        using var acknowledge = await AcknowledgeAsync(programme, alert.Id.Value);
        using var resolve = await ResolveAsync(programme, alert.Id.Value, "false_alarm");

        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden],
            new[] { list, summary, acknowledge, resolve }.Select(response => response.StatusCode));
        Assert.Equal(SafetyAlertStatus.Open, (await StoredAsync(alert.Id)).Status);
    }

    [Fact]
    public async Task Summary_counts_open_and_critical()
    {
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);
        var before = await SummaryAsync(admin);
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        // Later than anything else in the shared database, so it is the newest unresolved detection.
        var latest = Now.AddYears(1);
        await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now);
        await RaisedAsync(family.ScoreSubject, SafetyConcern.LowResilienceScore, SafetyAlertSource.HouseholdAssessment, latest);
        await RaisedAsync(family.Subject, SafetyConcern.HarmToOthers, SafetyAlertSource.ConsultantVoice, Now, Acknowledged);
        await RaisedAsync(family.Subject, SafetyConcern.AbuseOrDanger, SafetyAlertSource.SupportSearch, latest.AddDays(1), Resolved);

        var after = await SummaryAsync(admin);

        Assert.Equal(
            (before.Open + 2, before.Acknowledged + 1, before.OpenCritical + 1, (DateTimeOffset?)latest),
            (after.Open, after.Acknowledged, after.OpenCritical, after.LatestDetectedAt));
        // Counts only, polled every 30 seconds: never audited.
        Assert.Equal(0, await AuditCountAsync("safeguarding.alerts.read", subject));
    }

    [Fact]
    public async Task Acknowledge_moves_an_open_alert_and_audits_it()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now.AddHours(-1));
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);

        using var response = await AcknowledgeAsync(admin, alert.Id.Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<AdminSafetyAlertResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            (alert.Id.Value, SafetyAlertStatus.Acknowledged, (DateTimeOffset?)Now, (DateTimeOffset?)null, family.Household.Reference.Value),
            (body.Id, body.Status, body.AcknowledgedAt, body.ResolvedAt, body.HouseholdReference));
        Assert.Equal(subject, (await StoredAsync(alert.Id)).AcknowledgedBy);
        Assert.Equal(1, await AuditCountAsync("safeguarding.alert.acknowledged", subject, alert.Id.Value.ToString()));
    }

    [Fact]
    public async Task Acknowledge_twice_is_409_alert_not_open()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now);
        var resolved = await RaisedAsync(family.Subject, SafetyConcern.HarmToOthers, SafetyAlertSource.AssistantChat, Now, Resolved);
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);

        using var first = await AcknowledgeAsync(admin, alert.Id.Value);
        using var twice = await AcknowledgeAsync(admin, alert.Id.Value);
        using var afterResolving = await AcknowledgeAsync(admin, resolved.Id.Value);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("safeguarding.alert_not_open", await twice.ProblemCodeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, afterResolving.StatusCode);
        Assert.Equal("safeguarding.alert_not_open", await afterResolving.ProblemCodeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await AuditCountAsync("safeguarding.alert.acknowledged", subject, alert.Id.Value.ToString()));
    }

    [Fact]
    public async Task Acknowledge_unknown_id_is_404()
    {
        using var client = factory.AsSystemAdministrator();

        using var acknowledge = await AcknowledgeAsync(client, Guid.CreateVersion7());
        using var resolve = await ResolveAsync(client, Guid.CreateVersion7(), "false_alarm");

        Assert.Equal(HttpStatusCode.NotFound, acknowledge.StatusCode);
        Assert.Equal("safeguarding.alert_not_found", await acknowledge.ProblemCodeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
        Assert.Equal("safeguarding.alert_not_found", await resolve.ProblemCodeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resolve_records_the_outcome()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var open = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now.AddHours(-1));
        var acknowledged = await RaisedAsync(
            family.Subject, SafetyConcern.AbuseOrDanger, SafetyAlertSource.AssistantChat, Now.AddHours(-1), Acknowledged);
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = factory.As(subject, Roles.SystemAdministrator);

        using var fromOpen = await ResolveAsync(admin, open.Id.Value, "referred_to_service");
        using var fromAcknowledged = await ResolveAsync(admin, acknowledged.Id.Value, "emergency_services_contacted");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (fromOpen.StatusCode, fromAcknowledged.StatusCode));
        var resolved = await fromOpen.ReadAsync<AdminSafetyAlertResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            (SafetyAlertStatus.Resolved, (SafetyAlertResolution?)SafetyAlertResolution.ReferredToService, (DateTimeOffset?)Now, (DateTimeOffset?)null),
            (resolved.Status, resolved.Resolution, resolved.ResolvedAt, resolved.AcknowledgedAt));
        var afterAcknowledging = await fromAcknowledged.ReadAsync<AdminSafetyAlertResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            (SafetyAlertStatus.Resolved, (SafetyAlertResolution?)SafetyAlertResolution.EmergencyServicesContacted, (DateTimeOffset?)Now),
            (afterAcknowledging.Status, afterAcknowledging.Resolution, afterAcknowledging.AcknowledgedAt));
        Assert.Equal(subject, (await StoredAsync(open.Id)).ResolvedBy);
        Assert.Equal(1, await AuditCountAsync("safeguarding.alert.resolved", subject, open.Id.Value.ToString()));
        Assert.Equal(1, await AuditCountAsync("safeguarding.alert.resolved", subject, acknowledged.Id.Value.ToString()));
    }

    [Fact]
    public async Task Resolve_without_resolution_is_400_with_camelCase_key()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now);
        using var client = factory.AsSystemAdministrator();

        using var missing = await client.PostRawJsonAsync($"{Route}/{alert.Id.Value}/resolve", "{}", TestContext.Current.CancellationToken);
        using var unknown = await ResolveAsync(client, alert.Id.Value, "ignored");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(["resolution"], await missing.ErrorKeysAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(SafetyAlertStatus.Open, (await StoredAsync(alert.Id)).Status);
    }

    [Fact]
    public async Task Resolve_a_resolved_alert_is_409()
    {
        var family = await EnrolledAsync(TestContext.Current.CancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, Now, Resolved);
        using var client = factory.AsSystemAdministrator();

        using var response = await ResolveAsync(client, alert.Id.Value, "family_contacted");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("safeguarding.alert_resolved", await response.ProblemCodeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SafetyAlertResolution.FalseAlarm, (await StoredAsync(alert.Id)).Resolution);
    }

    [Fact]
    public async Task Me_permissions_include_safety_alerts_for_system_administrators()
    {
        using var client = factory.AsSystemAdministrator();
        using var programme = factory.AsProgramAdministrator();

        using var forAdmin = await client.GetAsync("/api/v1/me/permissions", TestContext.Current.CancellationToken);
        using var forProgramme = await programme.GetAsync("/api/v1/me/permissions", TestContext.Current.CancellationToken);

        var granted = (await forAdmin.ReadAsync<PermissionsResponse>(TestContext.Current.CancellationToken)).Permissions;
        Assert.Contains("safety_alerts.read", granted);
        Assert.Contains("safety_alerts.manage", granted);
        Assert.DoesNotContain(
            (await forProgramme.ReadAsync<PermissionsResponse>(TestContext.Current.CancellationToken)).Permissions,
            permission => permission.StartsWith("safety_alerts.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recorder_raises_once_then_repeats_with_an_audit_each_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var family = await EnrolledAsync(cancellationToken);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICallerAccess>(_ => new CallerOf(family.Caller))));
        var recorder = ActivatorUtilities.CreateInstance<RecordSafetySignal>(host.Services);
        var signal = new SafetySignal(
            SafetyAlertSource.SupportSearch, SafetyConcern.AbuseOrDanger, SafetyDetector.PhraseScreen, "abuse.being_hurt", null);

        var first = await recorder.RecordForCallerAsync(signal, cancellationToken);
        var second = await recorder.RecordForCallerAsync(signal with { SignalCode = "danger.threatened" }, cancellationToken);

        Assert.Equal((SafetyAlertOutcome.Raised, SafetyAlertOutcome.Repeated), (first, second));
        var alert = await factory.QueryAsync(db => db.SafetyAlerts.AsNoTracking().SingleAsync(item => item.UserId == family.User.Id, cancellationToken));
        Assert.Equal((family.Household.Id, family.Head.Id), (alert.HouseholdId, alert.MemberId));
        Assert.Equal((2, "danger.threatened", SafetyAlertStatus.Open), (alert.OccurrenceCount, alert.SignalCode, alert.Status));
        Assert.Equal(1, await AuditCountAsync(RecordSafetySignal.Raised, resourceId: alert.Id.Value.ToString()));
        Assert.Equal(1, await AuditCountAsync(RecordSafetySignal.Repeated, resourceId: alert.Id.Value.ToString()));
    }

    [Fact]
    public async Task A_repeat_reopens_an_acknowledged_alert_for_the_queue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var family = await EnrolledAsync(cancellationToken);
        var alert = await RaisedAsync(family.Subject, SafetyConcern.AbuseOrDanger, SafetyAlertSource.SupportSearch, Now, Acknowledged);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICallerAccess>(_ => new CallerOf(family.Caller))));
        var recorder = ActivatorUtilities.CreateInstance<RecordSafetySignal>(host.Services);

        var outcome = await recorder.RecordForCallerAsync(
            new SafetySignal(SafetyAlertSource.SupportSearch, SafetyConcern.AbuseOrDanger, SafetyDetector.PhraseScreen, "abuse.being_hurt", null),
            cancellationToken);

        Assert.Equal(SafetyAlertOutcome.Repeated, outcome);
        var stored = await StoredAsync(alert.Id);
        Assert.Equal(
            (SafetyAlertStatus.Open, (string?)null, (DateTimeOffset?)null, 2),
            (stored.Status, stored.AcknowledgedBy, stored.AcknowledgedAt, stored.OccurrenceCount));
        Assert.Equal(1, await AuditCountAsync(RecordSafetySignal.Repeated, resourceId: alert.Id.Value.ToString()));
    }

    [Fact]
    public async Task Recorder_from_the_container_skips_without_a_signed_in_caller()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ISafetySignalRecorder>();

        var outcome = await recorder.RecordForCallerAsync(
            new SafetySignal(SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", "en"),
            TestContext.Current.CancellationToken);

        Assert.IsType<RecordSafetySignal>(recorder);
        Assert.Equal(SafetyAlertOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task Recorder_folds_a_lost_race_into_the_winning_alert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var family = await EnrolledAsync(cancellationToken);
        var winner = await RaisedAsync(family.Subject, SafetyConcern.HarmToOthers, SafetyAlertSource.ConsultantVoice, Now.AddMinutes(-1));
        var missOnce = new MissOnce();
        // The first lookup misses the winner, as if it had not committed yet, so the recorder's insert hits the index.
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(missOnce);
            services.AddScoped<SafetyAlertRepository>();
            services.AddScoped<ISafetyAlertRepository>(provider => new MissingOnceRepository(
                provider.GetRequiredService<SafetyAlertRepository>(), provider.GetRequiredService<MissOnce>()));
            services.AddScoped<ICallerAccess>(_ => new CallerOf(family.Caller));
        }));
        var recorder = ActivatorUtilities.CreateInstance<RecordSafetySignal>(host.Services);
        var raisedBefore = await AuditCountAsync(RecordSafetySignal.Raised);

        var outcome = await recorder.RecordForCallerAsync(
            new SafetySignal(SafetyAlertSource.ConsultantVoice, SafetyConcern.HarmToOthers, SafetyDetector.AiModeration, null, "ar"),
            cancellationToken);

        Assert.Equal(SafetyAlertOutcome.Repeated, outcome);
        Assert.True(missOnce.Missed);
        var stored = await factory.QueryAsync(db => db.SafetyAlerts.AsNoTracking().Where(item => item.UserId == family.User.Id).ToListAsync(cancellationToken));
        var alert = Assert.Single(stored);
        Assert.Equal((winner.Id, 2, SafetyDetector.AiModeration), (alert.Id, alert.OccurrenceCount, alert.Detector));
        Assert.Equal(1, await AuditCountAsync(RecordSafetySignal.Repeated, resourceId: winner.Id.Value.ToString()));
        Assert.Equal(raisedBefore, await AuditCountAsync(RecordSafetySignal.Raised));
    }

    /// <summary>The caller as the server would resolve them during a request; the recorder resolves it in its own scope.</summary>
    private sealed class CallerOf(CallerRecord caller) : ICallerAccess
    {
        public Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default) => Task.FromResult<CallerRecord?>(caller);

        public Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MissOnce
    {
        private int _missed;

        public bool Missed => _missed == 1;

        public bool TryMiss() => Interlocked.Exchange(ref _missed, 1) == 0;
    }

    private sealed class MissingOnceRepository(SafetyAlertRepository inner, MissOnce missOnce) : ISafetyAlertRepository
    {
        public Task<SafetyAlert?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default) =>
            inner.GetAsync(id, cancellationToken);

        public Task<SafetyAlert?> FindUnresolvedAsync(
            PlatformUserId userId, SafetyConcern concern, SafetyAlertSource source, CancellationToken cancellationToken = default) =>
            missOnce.TryMiss() ? Task.FromResult<SafetyAlert?>(null) : inner.FindUnresolvedAsync(userId, concern, source, cancellationToken);

        public Task<bool> ExistsForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
            inner.ExistsForSessionAsync(sessionId, cancellationToken);

        public void Add(SafetyAlert alert) => inner.Add(alert);
    }
}
