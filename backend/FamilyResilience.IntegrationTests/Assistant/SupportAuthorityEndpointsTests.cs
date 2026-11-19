using System.Net;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Identity;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>The organisations on the All services page (D-115), read from the graph that ships.</summary>
[Collection(ApiCollection.Name)]
public sealed class SupportAuthorityEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/support-authorities";

    [Fact]
    public async Task A_participant_lists_the_authorities_in_both_languages_with_their_services()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authorities = await response.ReadAsync<List<SupportAuthorityResponse>>(cancellationToken);
        Assert.Equal(13, authorities.Count);
        var care = authorities.Single(authority => authority.Code == "family_care_authority");
        Assert.Equal(("Family Care Authority", "هيئة الرعاية الأسرية", "800 444"), (care.Name.En, care.Name.Ar, care.Phone));
        Assert.Equal(["abu_dhabi"], care.Emirates);
        Assert.NotEmpty(care.Services);
    }

    [Fact]
    public async Task A_service_through_another_authority_names_it_and_a_body_with_no_family_service_lists_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.GetAsync(Route, cancellationToken);

        var authorities = await response.ReadAsync<List<SupportAuthorityResponse>>(cancellationToken);
        var card = authorities.Single(authority => authority.Code == "zayed_authority").Services.Single(service => service.Code == "za_determination_card");
        Assert.Equal("TAMM", card.Via?.En);
        Assert.Empty(authorities.Single(authority => authority.Code == "sharjah_statistics_community_development").Services);
    }

    [Fact]
    public async Task The_response_holds_no_review_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.GetAsync(Route, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("approvalReference", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_signing_in_it_is_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = factory.As(null);

        using var response = await anonymous.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_role_without_directory_access_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.As(ApiClients.NewSubject("researcher"), Roles.Researcher);

        using var response = await client.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
