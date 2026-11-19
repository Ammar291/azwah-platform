using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Api;

/// <summary>The read-only configuration views: the versions of an instrument and the rule sets of a version.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConfigurationReadEndpointsTests(ApiFactory factory)
{
    private async Task<(Instrument Instrument, InstrumentVersion Draft, InstrumentVersion Approved)> TwoVersionsAsync(CancellationToken cancellationToken)
    {
        var draft = await factory.DraftVersionAsync(cancellationToken);
        var instrument = await factory.InstrumentOfAsync(draft, cancellationToken);
        var approved = await factory.ApprovedVersionAsync(cancellationToken, instrument, versionNumber: 2);
        return (instrument, draft, approved);
    }

    [Fact]
    public async Task An_instruments_versions_are_listed_newest_first_without_their_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (instrument, draft, approved) = await TwoVersionsAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instruments/{instrument.Id.Value}/versions", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"sections\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        var page = await response.ReadAsync<PagedResponse<InstrumentVersionSummaryResponse>>(cancellationToken);
        Assert.Equal([approved.Id.Value, draft.Id.Value], page.Items.Select(version => version.Id));
        Assert.Equal(2, page.TotalCount);
        var newest = page.Items[0];
        Assert.Equal(
            (2, InstrumentVersionStatus.Approved, approved.ApprovedBy, approved.EffectiveFrom, approved.Sections.Count,
                approved.Sections.Sum(section => section.Items.Count)),
            (newest.VersionNumber, newest.Status, newest.ApprovedBy, newest.EffectiveFrom, newest.SectionCount, newest.ItemCount));
        Assert.Equal((InstrumentVersionStatus.Draft, (string?)null), (page.Items[1].Status, page.Items[1].ApprovedBy));
    }

    [Fact]
    public async Task The_version_list_pages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (instrument, draft, _) = await TwoVersionsAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instruments/{instrument.Id.Value}/versions?page=2&pageSize=1", cancellationToken);

        var page = await response.ReadAsync<PagedResponse<InstrumentVersionSummaryResponse>>(cancellationToken);
        Assert.Equal(draft.Id.Value, Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Versions_of_an_unknown_instrument_return_404()
    {
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instruments/{Guid.NewGuid()}/versions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.instrument_not_found", await response.ProblemCodeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_page_size_of_zero_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (instrument, _, _) = await TwoVersionsAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instruments/{instrument.Id.Value}/versions?pageSize=0", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pageSize", await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_versions_rule_sets_are_listed_by_kind_and_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, approved) = await TwoVersionsAsync(cancellationToken);
        var scoring = await factory.ApprovedRuleSetAsync(RuleSetKind.Scoring, approved, cancellationToken);
        var eligibility = await factory.ApprovedRuleSetAsync(RuleSetKind.Eligibility, approved, cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instrument-versions/{approved.Id.Value}/rule-sets", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ruleSets = await response.ReadAsync<List<RuleSetResponse>>(cancellationToken);
        Assert.Equal([eligibility.Id.Value, scoring.Id.Value], ruleSets.Select(ruleSet => ruleSet.Id));
        Assert.Equal((RuleSetKind.Eligibility, RuleSetStatus.Approved, approved.Id.Value),
            (ruleSets[0].Kind, ruleSets[0].Status, ruleSets[0].InstrumentVersionId));
    }

    [Fact]
    public async Task A_version_without_rule_sets_lists_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await factory.DraftVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instrument-versions/{draft.Id.Value}/rule-sets", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadAsync<List<RuleSetResponse>>(cancellationToken));
    }

    [Fact]
    public async Task Rule_sets_of_an_unknown_version_return_404()
    {
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instrument-versions/{Guid.NewGuid()}/rule-sets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await response.ProblemCodeAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    public async Task Analytics_staff_get_403(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.As(ApiClients.NewSubject("staff"), role);

        using var versions = await client.GetAsync($"/api/v1/instruments/{Guid.NewGuid()}/versions", cancellationToken);
        using var ruleSets = await client.GetAsync($"/api/v1/instrument-versions/{Guid.NewGuid()}/rule-sets", cancellationToken);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (versions.StatusCode, ruleSets.StatusCode));
    }
}
