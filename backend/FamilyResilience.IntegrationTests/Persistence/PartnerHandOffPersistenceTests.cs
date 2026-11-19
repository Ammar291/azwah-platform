using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// Partner organisations and manual hand-offs in PostgreSQL. The raw SQL cases bypass the domain to prove the database
/// itself keeps a hand-off to one active partner that covers the referral's category, recorded once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PartnerHandOffPersistenceTests(DatabaseFixture fixture)
{
    private const string Category = "synthetic_category";

    private async Task<PartnerOrganisation> SavedPartnerAsync(CancellationToken cancellationToken, bool active = true, string category = Category)
    {
        var partner = TestData.Ok(PartnerOrganisation.Register(
            TestData.UniqueCode("partner"), TestData.Text("شريك", "Synthetic partner"), [category], TestData.Now));
        if (!active)
        {
            TestData.Ok(partner.SetActive(false, TestData.Now));
        }

        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPartnerOrganisationRepository>().Add(partner);
                return Task.CompletedTask;
            },
            cancellationToken);
        return partner;
    }

    private static FormattableString HandOffSql(SupportReferral referral, PartnerOrganisation partner) =>
        $"""
         UPDATE support_referrals
         SET status = 'InProgress', assigned_service_code = {partner.Code}, partner_organisation_id = {partner.Id.Value},
             hand_off_method = 'Manual', handed_off_at = {TestData.Now.AddMinutes(5)}, handed_off_by = 'raw-sql',
             updated_at = {TestData.Now.AddMinutes(5)}
         WHERE id = {referral.Id.Value}
         """;

    [Fact]
    public async Task A_partner_is_read_back_with_its_name_and_categories()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var partner = await SavedPartnerAsync(cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<IPartnerOrganisationRepository>().GetAsync(partner.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(saved);
        Assert.Equal((partner.Code, "Synthetic partner", "شريك", true), (saved.Code, saved.Name.English, saved.Name.Arabic, saved.IsActive));
        Assert.Equal([Category], saved.Categories);
    }

    [Fact]
    public async Task A_second_partner_with_the_same_code_returns_partner_code_taken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var partner = await SavedPartnerAsync(cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPartnerOrganisationRepository>().Add(TestData.Ok(PartnerOrganisation.Register(
            partner.Code, TestData.Text("شريك", "Another partner"), [Category], TestData.Now)));
        var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(ReferralErrors.PartnerCodeTaken, committed.Error);
    }

    [Fact]
    public async Task A_handed_off_referral_is_read_back_with_its_partner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken);

        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken);
                TestData.Ok(tracked!.HandOff(partner, "coordinator-subject", TestData.Now.AddMinutes(5)));
            },
            cancellationToken);
        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<ISupportReferralRepository>().GetAsync(referral.Id, cancellationToken),
            cancellationToken);

        Assert.Equal(
            (ReferralStatus.InProgress, (PartnerOrganisationId?)partner.Id, (HandOffMethod?)HandOffMethod.Manual, "coordinator-subject", partner.Code),
            (saved!.Status, saved.PartnerOrganisationId, saved.HandOffMethod, saved.HandedOffBy, saved.AssignedServiceCode));
    }

    [Fact]
    public async Task The_database_accepts_a_valid_hand_off_by_raw_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken);

        Assert.Equal(1, await fixture.ExecuteSqlAsync(HandOffSql(referral, partner), cancellationToken));
    }

    [Fact]
    public async Task The_database_refuses_a_hand_off_to_an_inactive_partner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken, active: false);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(HandOffSql(referral, partner), cancellationToken), PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task The_database_refuses_a_hand_off_to_a_partner_that_does_not_cover_the_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken, category: "another_category");

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(HandOffSql(referral, partner), cancellationToken), PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task The_database_refuses_a_hand_off_when_a_null_category_is_all_that_could_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken, category: "another_category");
        await fixture.ExecuteSqlAsync(
            $"UPDATE partner_organisations SET categories = ARRAY['another_category', NULL]::varchar(64)[] WHERE id = {partner.Id.Value}",
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(HandOffSql(referral, partner), cancellationToken), PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task The_database_refuses_moving_a_hand_off_to_another_partner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var first = await SavedPartnerAsync(cancellationToken);
        var second = await SavedPartnerAsync(cancellationToken);
        await fixture.ExecuteSqlAsync(HandOffSql(referral, first), cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE support_referrals SET partner_organisation_id = {second.Id.Value} WHERE id = {referral.Id.Value}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task The_database_refuses_a_partial_hand_off()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (referral, _) = await fixture.SavedReferralAsync(cancellationToken, Category);
        var partner = await SavedPartnerAsync(cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 UPDATE support_referrals
                 SET status = 'InProgress', assigned_service_code = {partner.Code}, partner_organisation_id = {partner.Id.Value}
                 WHERE id = {referral.Id.Value}
                 """,
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_support_referrals_hand_off_shape");
    }
}
