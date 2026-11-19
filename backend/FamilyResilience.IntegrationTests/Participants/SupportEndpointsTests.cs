using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class SupportEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private async Task<Guid> SubmittedLowScoreAsync(Enrolment enrolment, CancellationToken cancellationToken)
    {
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return sessionId;
    }

    [Fact]
    public async Task With_referral_consent_the_caller_sees_the_recommendation_and_its_open_referral()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Referral, cancellationToken);
        var sessionId = await SubmittedLowScoreAsync(enrolment, cancellationToken);

        using var recommendations = await enrolment.Client.GetAsync("/api/v1/me/recommendations", cancellationToken);
        using var referrals = await enrolment.Client.GetAsync("/api/v1/me/referrals", cancellationToken);

        var recommendation = Assert.Single((await recommendations.ReadAsync<PagedResponse<RecommendationResponse>>(cancellationToken)).Items);
        var referral = Assert.Single((await referrals.ReadAsync<PagedResponse<ReferralResponse>>(cancellationToken)).Items);
        Assert.Equal((sessionId, "synthetic_support", referral.Id, (ReferralStatus?)ReferralStatus.Open),
            (recommendation.SessionId, recommendation.CategoryCode, recommendation.ReferralId, recommendation.ReferralStatus));
        Assert.Equal((enrolment.Self, ReferralStatus.Open, (string?)null), (referral.MemberId, referral.Status, referral.AssignedServiceCode));
    }

    [Fact]
    public async Task Without_referral_consent_there_is_a_recommendation_but_no_referral()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        await SubmittedLowScoreAsync(enrolment, cancellationToken);

        using var recommendations = await enrolment.Client.GetAsync("/api/v1/me/recommendations", cancellationToken);
        using var referrals = await enrolment.Client.GetAsync("/api/v1/me/referrals", cancellationToken);

        var recommendation = Assert.Single((await recommendations.ReadAsync<PagedResponse<RecommendationResponse>>(cancellationToken)).Items);
        Assert.Equal(((Guid?)null, (ReferralStatus?)null), (recommendation.ReferralId, recommendation.ReferralStatus));
        Assert.Equal(0, (await referrals.ReadAsync<PagedResponse<ReferralResponse>>(cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Another_family_sees_none_of_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await Enrolment.CreateAsync(Factory, cancellationToken);
        await owner.Client.GrantConsentAsync(owner.Self, ConsentType.Referral, cancellationToken);
        await SubmittedLowScoreAsync(owner, cancellationToken);
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken);
        using var newcomer = Factory.AsParticipant();

        foreach (var client in new[] { stranger.Client, newcomer })
        {
            using var recommendations = await client.GetAsync("/api/v1/me/recommendations", cancellationToken);
            using var referrals = await client.GetAsync("/api/v1/me/referrals", cancellationToken);
            Assert.Equal(0, (await recommendations.ReadAsync<PagedResponse<RecommendationResponse>>(cancellationToken)).TotalCount);
            Assert.Equal(0, (await referrals.ReadAsync<PagedResponse<ReferralResponse>>(cancellationToken)).TotalCount);
        }
    }

    [Theory]
    [InlineData("/api/v1/me/recommendations")]
    [InlineData("/api/v1/me/referrals")]
    public async Task Pages_are_bounded_staff_get_403_and_anonymous_callers_401(string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = Factory.AsParticipant();
        using var staff = Factory.As(ApiClients.NewSubject("staff"), Roles.ProgramAdministrator);
        using var anonymous = Factory.As(null);

        using var tooBig = await participant.GetAsync($"{url}?pageSize=101", cancellationToken);
        using var asStaff = await staff.GetAsync(url, cancellationToken);
        using var asAnonymous = await anonymous.GetAsync(url, cancellationToken);

        Assert.Contains("pageSize", await tooBig.ErrorKeysAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }
}
