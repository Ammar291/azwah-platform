using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Partner organisations and manual hand-offs (D-61): system administrators keep the partner list, support coordinators
/// hand consented referrals to a partner outside the platform, and participants see that it was a manual hand-off.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class PartnerHandOffEndpointsTests(ParticipantApi api)
{
    private const string Category = "synthetic_support";

    private ApiFactory Factory => api.Factory;

    private static object Partner(string? code = null, params string[] categories) => new
    {
        code = code ?? TestData.UniqueCode("partner"),
        nameAr = "شريك تجريبي",
        nameEn = "Synthetic partner",
        categories = categories.Length == 0 ? [Category] : categories,
    };

    private async Task<PartnerResponse> RegisteredAsync(CancellationToken cancellationToken, params string[] categories)
    {
        using var admin = Factory.AsSystemAdministrator();
        using var response = await admin.PostJsonAsync("/api/v1/admin/partners", Partner(null, categories), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<PartnerResponse>(cancellationToken);
    }

    /// <summary>A newly enrolled household whose head consented to referral and submitted a low score: one open referral.</summary>
    private async Task<(Enrolment Enrolment, ReferralResponse Referral)> OpenReferralAsync(CancellationToken cancellationToken)
    {
        var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Referral, cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var referrals = await enrolment.Client.GetAsync("/api/v1/me/referrals", cancellationToken);
        return (enrolment, Assert.Single((await referrals.ReadAsync<PagedResponse<ReferralResponse>>(cancellationToken)).Items));
    }

    private static Task<HttpResponseMessage> HandOffAsync(HttpClient client, Guid referralId, Guid partnerId, CancellationToken cancellationToken) =>
        client.PostJsonAsync($"/api/v1/admin/referrals/{referralId}/hand-off", new { partnerId }, cancellationToken);

    [Fact]
    public async Task A_system_administrator_registers_a_partner_that_coordinators_can_list()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsSystemAdministrator();
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var created = await admin.PostJsonAsync("/api/v1/admin/partners", Partner(null, Category, "family_mediation"), cancellationToken);
        var partner = await created.ReadAsync<PartnerResponse>(cancellationToken);
        using var fetched = await coordinator.GetAsync(created.Headers.Location!.OriginalString, cancellationToken);
        using var listed = await coordinator.GetAsync("/api/v1/admin/partners?pageSize=100", cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/admin/partners/{partner.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal(("شريك تجريبي", "Synthetic partner", true), (partner.NameAr, partner.NameEn, partner.IsActive));
        Assert.Equal([Category, "family_mediation"], partner.Categories);
        Assert.Equal(partner.Id, (await fetched.ReadAsync<PartnerResponse>(cancellationToken)).Id);
        Assert.Contains((await listed.ReadAsync<PagedResponse<PartnerResponse>>(cancellationToken)).Items, item => item.Id == partner.Id);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "referrals.partner.registered" && audit.ResourceId == partner.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task A_partner_code_is_unique_and_well_formed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsSystemAdministrator();
        var code = TestData.UniqueCode("partner");
        using var first = await admin.PostJsonAsync("/api/v1/admin/partners", Partner(code), cancellationToken);

        using var duplicate = await admin.PostJsonAsync("/api/v1/admin/partners", Partner(code), cancellationToken);
        using var malformed = await admin.PostJsonAsync("/api/v1/admin/partners", Partner("Not A Code"), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("referrals.partner_code_taken", await duplicate.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("referrals.partner_code_invalid", await malformed.ProblemCodeAsync(cancellationToken));
    }

    [Theory]
    [InlineData("""{"code":"partner_a","nameEn":"Partner","categories":["synthetic_support"]}""", "nameAr")]
    [InlineData("""{"code":"partner_a","nameAr":"شريك","categories":["synthetic_support"]}""", "nameEn")]
    [InlineData("""{"code":"partner_a","nameAr":"شريك","nameEn":"Partner","categories":[]}""", "categories")]
    [InlineData("""{"nameAr":"شريك","nameEn":"Partner","categories":["synthetic_support"]}""", "code")]
    public async Task A_partner_without_its_fields_returns_400_keyed_by_the_field(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsSystemAdministrator();

        using var response = await admin.PostRawJsonAsync("/api/v1/admin/partners", body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.SupportCoordinator, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(Roles.Researcher, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(Roles.ProgramAdministrator, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Participant, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Only_system_administrators_register_partners_and_only_they_and_coordinators_read_them(
        string role, HttpStatusCode register, HttpStatusCode read)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var created = await client.PostJsonAsync("/api/v1/admin/partners", Partner(), cancellationToken);
        using var listed = await client.GetAsync("/api/v1/admin/partners", cancellationToken);

        Assert.Equal(register, created.StatusCode);
        Assert.Equal(read, listed.StatusCode);
    }

    [Fact]
    public async Task A_coordinator_hands_off_an_open_referral_and_the_participant_sees_it_was_manual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var partner = await RegisteredAsync(cancellationToken);
        var subject = ApiClients.NewSubject("coordinator");
        using var coordinator = Factory.As(subject, Roles.SupportCoordinator);

        using var response = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);
        using var mine = await enrolment.Client.GetAsync("/api/v1/me/referrals", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var handedOff = await response.ReadAsync<CoordinatedReferralResponse>(cancellationToken);
        Assert.Equal(
            (referral.Id, enrolment.Household.Reference, Category, ReferralStatus.InProgress),
            (handedOff.Id, handedOff.HouseholdReference, handedOff.CategoryCode, handedOff.Status));
        Assert.Equal(new HandOffResponse(partner.Code, partner.NameAr, partner.NameEn, HandOffMethod.Manual, TestData.Now), handedOff.HandOff);
        var seen = Assert.Single((await mine.ReadAsync<PagedResponse<ReferralResponse>>(cancellationToken)).Items);
        Assert.Equal((ReferralStatus.InProgress, partner.Code), (seen.Status, seen.AssignedServiceCode));
        Assert.Equal(handedOff.HandOff, seen.HandOff);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "referrals.referral.handed_off" && audit.ActorId == subject && audit.ResourceId == referral.Id.ToString(),
            cancellationToken)));
    }

    [Fact]
    public async Task A_hand_off_needs_an_open_referral_and_an_active_partner_that_covers_its_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var inactive = await RegisteredAsync(cancellationToken);
        using var admin = Factory.AsSystemAdministrator();
        using var deactivated = await admin.PatchJsonAsync($"/api/v1/admin/partners/{inactive.Id}", new { isActive = false }, cancellationToken);
        var otherCategory = await RegisteredAsync(cancellationToken, "another_category");
        var partner = await RegisteredAsync(cancellationToken);
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var toInactive = await HandOffAsync(coordinator, referral.Id, inactive.Id, cancellationToken);
        using var toOtherCategory = await HandOffAsync(coordinator, referral.Id, otherCategory.Id, cancellationToken);
        using var toUnknownPartner = await HandOffAsync(coordinator, referral.Id, Guid.NewGuid(), cancellationToken);
        using var unknownReferral = await HandOffAsync(coordinator, Guid.NewGuid(), partner.Id, cancellationToken);
        using var first = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);
        using var second = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);
        using var missing = await coordinator.PostJsonAsync($"/api/v1/admin/referrals/{referral.Id}/hand-off", new { }, cancellationToken);

        Assert.False((await deactivated.ReadAsync<PartnerResponse>(cancellationToken)).IsActive);
        Assert.Equal("referrals.partner_inactive", await toInactive.ProblemCodeAsync(cancellationToken));
        Assert.Equal("referrals.partner_category_mismatch", await toOtherCategory.ProblemCodeAsync(cancellationToken));
        Assert.Equal((HttpStatusCode.NotFound, "referrals.partner_not_found"), (toUnknownPartner.StatusCode, await toUnknownPartner.ProblemCodeAsync(cancellationToken)));
        Assert.Equal((HttpStatusCode.NotFound, "referrals.referral_not_found"), (unknownReferral.StatusCode, await unknownReferral.ProblemCodeAsync(cancellationToken)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "referrals.not_open"), (second.StatusCode, await second.ProblemCodeAsync(cancellationToken)));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("partnerId", await missing.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_referral_cancelled_by_a_consent_withdrawal_cannot_be_handed_off()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var policy = await enrolment.Client.CurrentPolicyAsync(ConsentType.Referral, cancellationToken);
        using var withdrawn = await enrolment.Client.PostJsonAsync(
            "/api/v1/consent", new { memberId = enrolment.Self, policyVersionId = policy.Id, decision = "withdraw" }, cancellationToken);
        var partner = await RegisteredAsync(cancellationToken);
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var response = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("referrals.not_open", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_open_referral_whose_consent_is_no_longer_active_returns_409_rather_than_tripping_the_guard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        // Withdrawal through the API also cancels the referral, so this state can only be made directly in the database.
        await Factory.QueryAsync(db => db.Database.ExecuteSqlAsync(
            $"""
             UPDATE consent_records SET withdrawn_at = granted_at, withdrawn_by = 'raw-sql', updated_at = granted_at
             WHERE family_member_id = {enrolment.Self} AND consent_type = 'Referral'
             """,
            cancellationToken));
        var partner = await RegisteredAsync(cancellationToken);
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var response = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("referrals.consent_not_active", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Coordinators_list_referrals_by_status_with_the_household_reference_and_each_read_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var subject = ApiClients.NewSubject("coordinator");
        using var coordinator = Factory.As(subject, Roles.SupportCoordinator);

        using var open = await coordinator.GetAsync("/api/v1/admin/referrals?status=open&pageSize=100", cancellationToken);
        using var inProgress = await coordinator.GetAsync("/api/v1/admin/referrals?status=in_progress&pageSize=100", cancellationToken);

        var row = Assert.Single((await open.ReadAsync<PagedResponse<CoordinatedReferralResponse>>(cancellationToken)).Items, item => item.Id == referral.Id);
        Assert.Equal((enrolment.Household.Reference, ReferralStatus.Open, (HandOffResponse?)null), (row.HouseholdReference, row.Status, row.HandOff));
        Assert.DoesNotContain((await inProgress.ReadAsync<PagedResponse<CoordinatedReferralResponse>>(cancellationToken)).Items, item => item.Id == referral.Id);
        Assert.Equal(2, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "referrals.referrals.read" && audit.ActorId == subject, cancellationToken)));
    }

    [Theory]
    [InlineData(Roles.SystemAdministrator)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.Participant)]
    public async Task Only_support_coordinators_see_and_hand_off_referrals(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var list = await client.GetAsync("/api/v1/admin/referrals", cancellationToken);
        using var handOff = await HandOffAsync(client, Guid.NewGuid(), Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, handOff.StatusCode);
    }

    [Theory]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?status=lost", "status")]
    public async Task The_referral_queue_rejects_bad_filters(string query, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var response = await coordinator.GetAsync($"/api/v1/admin/referrals{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(null);
        var someId = Guid.NewGuid();

        using var partners = await client.GetAsync("/api/v1/admin/partners", cancellationToken);
        using var partner = await client.GetAsync($"/api/v1/admin/partners/{someId}", cancellationToken);
        using var register = await client.PostJsonAsync("/api/v1/admin/partners", Partner(), cancellationToken);
        using var update = await client.PatchJsonAsync($"/api/v1/admin/partners/{someId}", new { isActive = false }, cancellationToken);
        using var referrals = await client.GetAsync("/api/v1/admin/referrals", cancellationToken);
        using var handOff = await HandOffAsync(client, someId, someId, cancellationToken);

        Assert.All(
            new[] { partners, partner, register, update, referrals, handOff },
            response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task A_partner_that_does_not_exist_answers_404_to_reading_and_updating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);
        using var admin = Factory.AsSystemAdministrator();

        using var read = await coordinator.GetAsync($"/api/v1/admin/partners/{Guid.NewGuid()}", cancellationToken);
        using var update = await admin.PatchJsonAsync($"/api/v1/admin/partners/{Guid.NewGuid()}", new { isActive = false }, cancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, "referrals.partner_not_found"), (read.StatusCode, await read.ProblemCodeAsync(cancellationToken)));
        Assert.Equal((HttpStatusCode.NotFound, "referrals.partner_not_found"), (update.StatusCode, await update.ProblemCodeAsync(cancellationToken)));
    }

    [Fact]
    public async Task Updating_a_partner_needs_is_active_and_answers_with_the_partner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var partner = await RegisteredAsync(cancellationToken);
        using var admin = Factory.AsSystemAdministrator();

        using var missing = await admin.PatchRawJsonAsync($"/api/v1/admin/partners/{partner.Id}", "{}", cancellationToken);
        using var deactivated = await admin.PatchJsonAsync($"/api/v1/admin/partners/{partner.Id}", new { isActive = false }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("isActive", await missing.ErrorKeysAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await deactivated.ReadAsync<PartnerResponse>(cancellationToken)).IsActive);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "referrals.partner.updated" && audit.ResourceId == partner.Id.ToString(), cancellationToken)));
    }

    [Theory]
    [InlineData(Roles.SupportCoordinator, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(Roles.Researcher, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Participant, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Only_system_administrators_update_a_partner_and_only_they_and_coordinators_read_it(
        string role, HttpStatusCode update, HttpStatusCode read)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var partner = await RegisteredAsync(cancellationToken);
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var patched = await client.PatchJsonAsync($"/api/v1/admin/partners/{partner.Id}", new { isActive = false }, cancellationToken);
        using var fetched = await client.GetAsync($"/api/v1/admin/partners/{partner.Id}", cancellationToken);

        Assert.Equal(update, patched.StatusCode);
        Assert.Equal(read, fetched.StatusCode);
    }

    [Fact]
    public async Task After_the_member_withdraws_referral_consent_the_queue_withholds_the_household_reference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var partner = await RegisteredAsync(cancellationToken);
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);
        using var handedOff = await HandOffAsync(coordinator, referral.Id, partner.Id, cancellationToken);
        var policy = await enrolment.Client.CurrentPolicyAsync(ConsentType.Referral, cancellationToken);
        using var withdrawn = await enrolment.Client.PostJsonAsync(
            "/api/v1/consent", new { memberId = enrolment.Self, policyVersionId = policy.Id, decision = "withdraw" }, cancellationToken);

        using var cancelled = await coordinator.GetAsync("/api/v1/admin/referrals?status=cancelled&pageSize=100", cancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (handedOff.StatusCode, withdrawn.StatusCode));
        var row = Assert.Single((await cancelled.ReadAsync<PagedResponse<CoordinatedReferralResponse>>(cancellationToken)).Items, item => item.Id == referral.Id);
        Assert.Equal((ReferralStatus.Cancelled, (string?)null), (row.Status, row.HouseholdReference));
        Assert.Equal(partner.Code, row.HandOff?.PartnerCode);
    }

    [Fact]
    public async Task A_partner_deactivated_during_a_hand_off_answers_409_partner_inactive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, referral) = await OpenReferralAsync(cancellationToken);
        using var _ = enrolment;
        var partner = await RegisteredAsync(cancellationToken);
        using var coordinator = Factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);
        Task<HttpResponseMessage>? handOff = null;

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var deactivated = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async token =>
                {
                    var db = services.GetRequiredService<FamilyResilienceDbContext>();
                    await db.Database.ExecuteSqlAsync(
                        $"UPDATE partner_organisations SET is_active = false, updated_at = {TestData.Now} WHERE id = {partner.Id}", token);
                    handOff = HandOffAsync(coordinator, referral.Id, partner.Id, token);
                    await PostgresWait.UntilAnotherConnectionWaitsOnAsync(db, token);
                    return FamilyResilience.Domain.Common.Result.Success(true);
                },
                cancellationToken);
            Assert.True(deactivated.IsSuccess);
        }

        using var response = await handOff!;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("referrals.partner_inactive", await response.ProblemCodeAsync(cancellationToken));
    }
}
