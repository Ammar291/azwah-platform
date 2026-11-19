using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class SupportReferralPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_referral_is_read_back_with_its_session_member_version_rules_and_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, scenario) = await fixture.SavedReferralAsync(cancellationToken, "financial_counselling");

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(scenario.Session.Id, saved.AssessmentSessionId);
        Assert.Equal(scenario.Member.Id, saved.FamilyMemberId);
        Assert.Equal(scenario.Version.Id, saved.InstrumentVersionId);
        Assert.Equal("financial_counselling", saved.CategoryCode);
        Assert.Equal(scenario.ReferralRules.Id, saved.ReferralRuleSetId);
        Assert.Equal(scenario.Consent.Id, saved.ConsentRecordId);
        Assert.Equal(ReferralStatus.Open, saved.Status);
        Assert.Null(saved.AssignedServiceCode);
        Assert.Null(saved.ResolvedAt);
        Assert.Equal(TestData.Now.AddMinutes(2), saved.CreatedAt);
    }

    [Fact]
    public async Task Referrals_of_a_session_are_listed_oldest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (first, scenario) = await fixture.SavedReferralAsync(cancellationToken, "category_one");
        var second = TestData.Ok(SupportReferral.Create(
            scenario.Session, scenario.ReferralRules, scenario.Consent, "category_two", TestData.Now.AddMinutes(1)));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISupportReferralRepository>().Add(second);
                return Task.CompletedTask;
            },
            cancellationToken);

        var listed = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportReferralRepository>().ListForSessionAsync(scenario.Session.Id, cancellationToken),
            cancellationToken);

        Assert.Equal(new[] { second.Id, first.Id }, listed.Select(referral => referral.Id).ToArray());
    }

    [Fact]
    public async Task A_second_live_referral_in_the_same_category_returns_already_referred()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, scenario) = await fixture.SavedReferralAsync(cancellationToken, "same_category");
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISupportReferralRepository>().Add(TestData.Ok(SupportReferral.Create(
            scenario.Session, scenario.ReferralRules, scenario.Consent, "same_category", TestData.Now.AddMinutes(3))));

        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(ReferralErrors.AlreadyReferred, committed.Error);
    }

    [Fact]
    public async Task A_cancelled_referral_leaves_its_category_free_for_a_new_referral()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, scenario) = await fixture.SavedReferralAsync(cancellationToken, "reopened_category");
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.Cancel("raised_in_error", "coordinator-subject", TestData.Now.AddMinutes(3)));
            },
            cancellationToken);
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISupportReferralRepository>().Add(TestData.Ok(SupportReferral.Create(
            scenario.Session, scenario.ReferralRules, scenario.Consent, "reopened_category", TestData.Now.AddMinutes(4))));

        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.True(committed.IsSuccess);
    }

    [Fact]
    public async Task Assigning_a_service_and_resolving_are_saved_with_the_resolution_and_resolver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.AssignService("synthetic_service", TestData.Now.AddHours(1)));
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.Resolve("support_provided", "coordinator-subject", TestData.Now.AddDays(2)));
            },
            cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken),
            cancellationToken);
        var statusChanges = await fixture.OutboxMessagesAsync(
            "referrals.support_referral_status_changed.v1", "supportReferralId", referral.Id.Value, cancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(ReferralStatus.Resolved, saved.Status);
        Assert.Equal("synthetic_service", saved.AssignedServiceCode);
        Assert.Equal("support_provided", saved.ResolutionCode);
        Assert.Equal("coordinator-subject", saved.ResolvedBy);
        Assert.Equal(TestData.Now.AddDays(2), saved.ResolvedAt);
        Assert.Equal(2, statusChanges.Count);
    }

    [Fact]
    public async Task Resolving_an_open_referral_and_cancelling_an_assigned_one_are_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (resolved, _) = await fixture.SavedReferralAsync(cancellationToken);
        var (cancelled, _) = await fixture.SavedReferralAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var repository = services.GetRequiredService<ISupportReferralRepository>();
                var open = await repository.GetAsync(resolved.Id, cancellationToken);
                var toAssign = await repository.GetAsync(cancelled.Id, cancellationToken);
                Assert.NotNull(open);
                Assert.NotNull(toAssign);
                TestData.Ok(open.Resolve("support_provided", null, TestData.Now.AddHours(1)));
                TestData.Ok(toAssign.AssignService("synthetic_service", TestData.Now.AddHours(1)));
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var assigned = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(cancelled.Id, cancellationToken);
                Assert.NotNull(assigned);
                TestData.Ok(assigned.Cancel("family_declined", "coordinator-subject", TestData.Now.AddHours(2)));
            },
            cancellationToken);

        var statuses = await fixture.QueryAsync(db => db.SupportReferrals
            .Where(referral => referral.Id == resolved.Id || referral.Id == cancelled.Id)
            .ToDictionaryAsync(referral => referral.Id, referral => referral.Status, cancellationToken));

        Assert.Equal(ReferralStatus.Resolved, statuses[resolved.Id]);
        Assert.Equal(ReferralStatus.Cancelled, statuses[cancelled.Id]);
    }

    [Fact]
    public async Task Concurrent_changes_to_one_referral_return_a_concurrency_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken);
        await using var first = fixture.CreateScope();
        await using var second = fixture.CreateScope();
        var firstCopy = await first.ServiceProvider.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
        var secondCopy = await second.ServiceProvider.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        TestData.Ok(firstCopy.AssignService("first_service", TestData.Now.AddHours(1)));
        Assert.True((await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        TestData.Ok(secondCopy.Cancel("raised_in_error", null, TestData.Now.AddHours(1)));
        var result = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(PersistenceErrors.ConcurrencyConflict, result.Error);
    }
}
