using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// Safety alerts in PostgreSQL: the round trip, the partial unique indexes behind the dedupe, the guard trigger, and the
/// staff read model's order, filters and summary. The database is shared, so list and summary checks look only at the
/// alerts each test made, or at the change it made to the counts.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SafetyAlertPersistenceTests(DatabaseFixture fixture)
{
    private const string RestrictViolation = "23001";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string Staff = "staff-subject";

    private async Task<PlatformUser> SavedUserAsync(CancellationToken cancellationToken)
    {
        var user = TestData.Ok(PlatformUser.Register($"subject-{Guid.NewGuid():N}", TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                return Task.CompletedTask;
            },
            cancellationToken);
        return user;
    }

    /// <summary>A user managing a saved household of a head (the user) and a spouse.</summary>
    private async Task<(PlatformUser User, Household Household)> EnrolledAsync(CancellationToken cancellationToken)
    {
        var user = await SavedUserAsync(cancellationToken);
        var household = TestData.Ok(Household.Enrol(
            HouseholdReference.Generate(), [TestData.Head(), TestData.Spouse()], 0, user.Id, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);
        return (user, household);
    }

    private static SafetyAlert Alert(
        SafetyAlertSubject subject,
        SafetyConcern concern = SafetyConcern.SuicideOrSelfHarm,
        SafetyAlertSource source = SafetyAlertSource.AssistantChat,
        DateTimeOffset? at = null) =>
        concern == SafetyConcern.LowResilienceScore
            ? SafetyAlert.Raise(subject, source, concern, SafetyDetector.ScoreBand, "vulnerable", null, null, at ?? TestData.Now)
            : SafetyAlert.Raise(subject, source, concern, SafetyDetector.PhraseScreen, "suicide.intent", "en", null, at ?? TestData.Now);

    private async Task<SafetyAlert> SavedAsync(SafetyAlert alert, CancellationToken cancellationToken)
    {
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISafetyAlertRepository>().Add(alert);
                return Task.CompletedTask;
            },
            cancellationToken);
        return alert;
    }

    private async Task ChangeAsync(SafetyAlertId id, Func<SafetyAlert, Result> change, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISafetyAlertRepository>().GetAsync(id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(change(tracked));
            },
            cancellationToken);

    /// <summary>Adds the alert and saves straight through the context, so the database's own refusal surfaces.</summary>
    private async Task<PostgresException> RefusedAsync(SafetyAlert alert, CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISafetyAlertRepository>().Add(alert);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>().SaveChangesAsync(cancellationToken));
        return Assert.IsType<PostgresException>(failure.InnerException);
    }

    private async Task<T> ReadAsync<T>(Func<ISafetyAlertReadModel, Task<T>> read)
    {
        await using var scope = fixture.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ISafetyAlertReadModel>());
    }

    [Fact]
    public async Task Alert_round_trips_with_codes_as_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, household) = await EnrolledAsync(cancellationToken);
        var spouse = household.Members[1];
        var alert = await SavedAsync(
            SafetyAlert.Raise(
                new SafetyAlertSubject(user.Id, household.Id, spouse.Id), SafetyAlertSource.ConsultantVoice,
                SafetyConcern.HarmToOthers, SafetyDetector.AiModeration, "harm_others.intent", "ar", null, TestData.Now),
            cancellationToken);
        var id = alert.Id.Value;

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISafetyAlertRepository>().GetAsync(alert.Id, cancellationToken),
            cancellationToken);
        var codes = await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT concat_ws(',', source, concern, severity, detector, status) AS \"Value\" FROM safety_alerts WHERE id = {id}")
            .SingleAsync(cancellationToken));

        Assert.NotNull(loaded);
        Assert.Equal((user.Id, household.Id, spouse.Id), (loaded.UserId, loaded.HouseholdId, loaded.MemberId));
        Assert.Equal(
            (SafetyAlertSource.ConsultantVoice, SafetyConcern.HarmToOthers, SafetySeverity.Critical, SafetyDetector.AiModeration),
            (loaded.Source, loaded.Concern, loaded.Severity, loaded.Detector));
        Assert.Equal(("harm_others.intent", "ar", (AssessmentSessionId?)null), (loaded.SignalCode, loaded.Language, loaded.AssessmentSessionId));
        Assert.Equal((TestData.Now, TestData.Now, 1), (loaded.FirstDetectedAt, loaded.LastDetectedAt, loaded.OccurrenceCount));
        Assert.Equal(SafetyAlertStatus.Open, loaded.Status);
        Assert.NotEqual(0u, loaded.Version);
        Assert.Equal("ConsultantVoice,HarmToOthers,Critical,AiModeration,Open", codes);
    }

    [Fact]
    public async Task Second_unresolved_alert_for_the_same_user_concern_and_source_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var subject = new SafetyAlertSubject(user.Id, null, null);
        var first = await SavedAsync(Alert(subject), cancellationToken);
        await ChangeAsync(first.Id, alert => alert.Acknowledge(Staff, TestData.Now.AddMinutes(1)), cancellationToken);

        var refused = await RefusedAsync(Alert(subject, at: TestData.Now.AddMinutes(2)), cancellationToken);
        await SavedAsync(Alert(subject, source: SafetyAlertSource.SupportSearch), cancellationToken);
        await SavedAsync(Alert(subject, SafetyConcern.AbuseOrDanger), cancellationToken);

        Assert.Equal((UniqueViolation, "ux_safety_alerts_unresolved_subject"), (refused.SqlState, refused.ConstraintName));
    }

    [Fact]
    public async Task Resolved_alert_allows_a_new_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var subject = new SafetyAlertSubject(user.Id, null, null);
        var first = await SavedAsync(Alert(subject), cancellationToken);
        await ChangeAsync(first.Id, alert => alert.Resolve(SafetyAlertResolution.FalseAlarm, Staff, TestData.Now.AddMinutes(5)), cancellationToken);

        async Task<SafetyAlertId?> UnresolvedAsync() => (await fixture.InScopeAsync(
            services => services.GetRequiredService<ISafetyAlertRepository>().FindUnresolvedAsync(
                user.Id, SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, cancellationToken),
            cancellationToken))?.Id;

        var afterResolving = await UnresolvedAsync();
        var second = await SavedAsync(Alert(subject, at: TestData.Now.AddMinutes(10)), cancellationToken);
        var afterRaising = await UnresolvedAsync();
        await ChangeAsync(second.Id, alert => alert.Acknowledge(Staff, TestData.Now.AddMinutes(11)), cancellationToken);
        var afterAcknowledging = await UnresolvedAsync();

        Assert.Null(afterResolving);
        Assert.Equal(second.Id, afterRaising);
        Assert.Equal(second.Id, afterAcknowledging);
    }

    [Fact]
    public async Task Changing_the_concern_is_refused_by_the_guard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var alert = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, null, null)), cancellationToken);
        var id = alert.Id.Value;

        // What the domain changes (the count, the latest detection and its detector and code) is allowed.
        await ChangeAsync(alert.Id, tracked => tracked.Recur(SafetyDetector.AiModeration, null, TestData.Now.AddMinutes(3)), cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE safety_alerts SET concern = 'AbuseOrDanger' WHERE id = {id}", cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE safety_alerts SET first_detected_at = first_detected_at - interval '1 day' WHERE id = {id}", cancellationToken),
            RestrictViolation);
        var count = await fixture.QueryAsync(db => db.Database
            .SqlQuery<int>($"SELECT occurrence_count AS \"Value\" FROM safety_alerts WHERE id = {id}")
            .SingleAsync(cancellationToken));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Clearing_the_household_is_refused_by_the_guard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, household) = await EnrolledAsync(cancellationToken);
        var alert = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, household.Id, null)), cancellationToken);
        var id = alert.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE safety_alerts SET household_id = NULL WHERE id = {id}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Reopening_an_acknowledged_alert_is_refused_by_the_status_check()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var alert = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, null, null)), cancellationToken);
        await ChangeAsync(alert.Id, tracked => tracked.Acknowledge(Staff, TestData.Now.AddMinutes(1)), cancellationToken);
        var id = alert.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE safety_alerts SET status = 'Open' WHERE id = {id}", cancellationToken),
            CheckViolation,
            "ck_safety_alerts_status_shape");
    }

    [Fact]
    public async Task A_repeat_reopens_an_acknowledged_alert_past_the_guard_and_the_status_check()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var alert = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, null, null)), cancellationToken);
        await ChangeAsync(alert.Id, tracked => tracked.Acknowledge(Staff, TestData.Now.AddMinutes(1)), cancellationToken);
        var before = await ReadAsync(model => model.SummaryAsync(cancellationToken));

        await ChangeAsync(alert.Id, tracked => tracked.Recur(SafetyDetector.AiModeration, null, TestData.Now.AddMinutes(2)), cancellationToken);

        var id = alert.Id.Value;
        var stored = await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT concat_ws(',', status, coalesce(acknowledged_by, '-'), coalesce(acknowledged_at::text, '-'), occurrence_count) AS \"Value\" FROM safety_alerts WHERE id = {id}")
            .SingleAsync(cancellationToken));
        Assert.Equal("Open,-,-,2", stored);
        var after = await ReadAsync(model => model.SummaryAsync(cancellationToken));
        Assert.Equal((1, -1), (after.Open - before.Open, after.Acknowledged - before.Acknowledged));
    }

    [Fact]
    public async Task Deleting_an_alert_outside_erasure_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await SavedUserAsync(cancellationToken);
        var alert = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, null, null)), cancellationToken);
        var id = alert.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM safety_alerts WHERE id = {id}", cancellationToken),
            RestrictViolation);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SET LOCAL azwah.erasure = 'on'", cancellationToken);
        var erased = await db.Database.ExecuteSqlAsync($"DELETE FROM safety_alerts WHERE id = {id}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Assert.Equal(1, erased);
    }

    [Fact]
    public async Task A_session_has_at_most_one_alert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, member) = await fixture.StartedSessionAsync(cancellationToken);
        var subject = new SafetyAlertSubject(null, member.HouseholdId, member.Id);
        SafetyAlert ScoreAlert() => SafetyAlert.Raise(
            subject, SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand,
            "vulnerable", null, session.Id, TestData.Now);
        Task<bool> ExistsAsync() => fixture.InScopeAsync(
            services => services.GetRequiredService<ISafetyAlertRepository>().ExistsForSessionAsync(session.Id, cancellationToken),
            cancellationToken);

        var before = await ExistsAsync();
        var first = await SavedAsync(ScoreAlert(), cancellationToken);
        await ChangeAsync(first.Id, alert => alert.Resolve(SafetyAlertResolution.FamilyContacted, Staff, TestData.Now.AddHours(1)), cancellationToken);
        var afterResolving = await ExistsAsync();
        var refused = await RefusedAsync(ScoreAlert(), cancellationToken);

        Assert.False(before);
        Assert.True(afterResolving);
        Assert.Equal((UniqueViolation, "ux_safety_alerts_session"), (refused.SqlState, refused.ConstraintName));
    }

    [Fact]
    public async Task Read_model_names_the_household_reference_and_the_members_relationship()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, household) = await EnrolledAsync(cancellationToken);
        var (head, spouse) = (household.Members[0], household.Members[1]);
        var ofHead = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, household.Id, head.Id)), cancellationToken);
        var ofSpouse = await SavedAsync(
            Alert(new SafetyAlertSubject(null, household.Id, spouse.Id), SafetyConcern.LowResilienceScore, SafetyAlertSource.HouseholdAssessment),
            cancellationToken);
        var ofUser = await SavedAsync(Alert(new SafetyAlertSubject(user.Id, null, null), source: SafetyAlertSource.SupportSearch), cancellationToken);

        var headView = await ReadAsync(model => model.GetAsync(ofHead.Id, cancellationToken));
        var spouseView = await ReadAsync(model => model.GetAsync(ofSpouse.Id, cancellationToken));
        var userView = await ReadAsync(model => model.GetAsync(ofUser.Id, cancellationToken));
        var missing = await ReadAsync(model => model.GetAsync(SafetyAlertId.New(), cancellationToken));

        Assert.NotNull(headView);
        Assert.NotNull(spouseView);
        Assert.NotNull(userView);
        Assert.Null(missing);
        Assert.Equal((household.Reference.Value, AdminSafetyAlertResponse.HeadOfHousehold), (headView.HouseholdReference, headView.MemberRelationship));
        Assert.Equal((household.Reference.Value, "spouse"), (spouseView.HouseholdReference, spouseView.MemberRelationship));
        Assert.Equal(
            (SafetyConcern.LowResilienceScore, SafetySeverity.High, SafetyDetector.ScoreBand, "vulnerable", (string?)null),
            (spouseView.Category, spouseView.Severity, spouseView.Detector, spouseView.SignalCode, spouseView.Language));
        Assert.Equal(((string?)null, (string?)null), (userView.HouseholdReference, userView.MemberRelationship));
        Assert.Equal((ofUser.Id.Value, SafetyAlertSource.SupportSearch, SafetyAlertStatus.Open), (userView.Id, userView.Source, userView.Status));
    }

    [Fact]
    public async Task List_orders_open_critical_newest_first_and_filters_unresolved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var subject = new SafetyAlertSubject(null, household.Id, household.Members[0].Id);
        var at = TestData.Now.AddDays(30);
        var olderOpenCritical = await SavedAsync(Alert(subject, SafetyConcern.SuicideOrSelfHarm, at: at.AddMinutes(1)), cancellationToken);
        var newerOpenCritical = await SavedAsync(Alert(subject, SafetyConcern.HarmToOthers, at: at.AddMinutes(3)), cancellationToken);
        var openHigh = await SavedAsync(Alert(subject, SafetyConcern.LowResilienceScore, SafetyAlertSource.HouseholdAssessment, at.AddMinutes(5)), cancellationToken);
        var acknowledged = await SavedAsync(Alert(subject, SafetyConcern.AbuseOrDanger, at: at.AddMinutes(6)), cancellationToken);
        var resolved = await SavedAsync(Alert(subject, SafetyConcern.SuicideOrSelfHarm, at: at.AddMinutes(7)), cancellationToken);
        await ChangeAsync(acknowledged.Id, alert => alert.Acknowledge(Staff, at.AddMinutes(8)), cancellationToken);
        await ChangeAsync(resolved.Id, alert => alert.Resolve(SafetyAlertResolution.NoActionNeeded, Staff, at.AddMinutes(9)), cancellationToken);
        Guid[] mine = [.. new[] { olderOpenCritical, newerOpenCritical, openHigh, acknowledged, resolved }.Select(alert => alert.Id.Value)];
        Guid[] Mine(IEnumerable<AdminSafetyAlertResponse> items) => [.. items.Select(item => item.Id).Where(mine.Contains)];

        var all = await ReadAsync(model => model.ListAsync(new SafetyAlertsQuery(null, null, 1, 100), cancellationToken));
        var unresolved = await ReadAsync(model => model.ListAsync(new SafetyAlertsQuery(SafetyAlertStatusFilter.Unresolved, null, 1, 100), cancellationToken));
        var firstTwo = await ReadAsync(model => model.ListAsync(new SafetyAlertsQuery(SafetyAlertStatusFilter.Unresolved, null, 1, 2), cancellationToken));
        var scores = await ReadAsync(model => model.ListAsync(new SafetyAlertsQuery(null, SafetyConcern.LowResilienceScore, null, null), cancellationToken));
        var closed = await ReadAsync(model => model.ListAsync(new SafetyAlertsQuery(SafetyAlertStatusFilter.Resolved, null, 1, 100), cancellationToken));

        Assert.Equal(
            [newerOpenCritical.Id.Value, olderOpenCritical.Id.Value, openHigh.Id.Value, acknowledged.Id.Value, resolved.Id.Value],
            Mine(all.Items));
        Assert.Equal([newerOpenCritical.Id.Value, olderOpenCritical.Id.Value, openHigh.Id.Value, acknowledged.Id.Value], Mine(unresolved.Items));
        Assert.All(unresolved.Items, item => Assert.NotEqual(SafetyAlertStatus.Resolved, item.Status));
        Assert.Equal((1, 2, unresolved.TotalCount), (firstTwo.Page, firstTwo.PageSize, firstTwo.TotalCount));
        Assert.Equal(unresolved.Items.Take(2).Select(item => item.Id), firstTwo.Items.Select(item => item.Id));
        Assert.Equal((1, SafetyAlertsQuery.DefaultPageSize), (scores.Page, scores.PageSize));
        Assert.Equal([openHigh.Id.Value], Mine(scores.Items));
        Assert.All(scores.Items, item => Assert.Equal(SafetyConcern.LowResilienceScore, item.Category));
        Assert.Equal([resolved.Id.Value], Mine(closed.Items));
        Assert.All(closed.Items, item => Assert.Equal(SafetyAlertStatus.Resolved, item.Status));
        var resolvedView = closed.Items.Single(item => item.Id == resolved.Id.Value);
        Assert.Equal((at.AddMinutes(9), SafetyAlertResolution.NoActionNeeded, (DateTimeOffset?)null), (resolvedView.ResolvedAt, resolvedView.Resolution, resolvedView.AcknowledgedAt));
    }

    [Fact]
    public async Task Summary_counts_open_acknowledged_and_open_critical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        var subject = new SafetyAlertSubject(null, household.Id, household.Members[0].Id);
        var at = TestData.Now.AddYears(1);
        // A resolved alert far newer than anything another test records, so the summary can only reach it by counting it.
        var muchLater = TestData.Now.AddYears(100);
        var before = await ReadAsync(model => model.SummaryAsync(cancellationToken));

        await SavedAsync(Alert(subject, SafetyConcern.SuicideOrSelfHarm, at: at.AddMinutes(1)), cancellationToken);
        await SavedAsync(Alert(subject, SafetyConcern.LowResilienceScore, SafetyAlertSource.HouseholdAssessment, at.AddMinutes(2)), cancellationToken);
        var acknowledged = await SavedAsync(Alert(subject, SafetyConcern.HarmToOthers, at: at.AddMinutes(3)), cancellationToken);
        var resolved = await SavedAsync(Alert(subject, SafetyConcern.AbuseOrDanger, at: muchLater), cancellationToken);
        await ChangeAsync(acknowledged.Id, alert => alert.Acknowledge(Staff, at.AddMinutes(5)), cancellationToken);
        await ChangeAsync(resolved.Id, alert => alert.Resolve(SafetyAlertResolution.ReferredToService, Staff, muchLater.AddMinutes(1)), cancellationToken);
        var after = await ReadAsync(model => model.SummaryAsync(cancellationToken));

        // Deltas: the collection runs its tests one at a time, so only this test changed the counts in between.
        Assert.Equal((2, 1, 1), (after.Open - before.Open, after.Acknowledged - before.Acknowledged, after.OpenCritical - before.OpenCritical));
        Assert.NotNull(after.LatestDetectedAt);
        Assert.True(after.LatestDetectedAt >= at.AddMinutes(3), $"The latest unresolved detection {after.LatestDetectedAt:O} is older than this test's.");
        Assert.True(after.LatestDetectedAt < muchLater, "The resolved alert was counted as the latest unresolved detection.");
    }
}
