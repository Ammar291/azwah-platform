using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class InstrumentEndpointsTests(ApiFactory factory)
{
    private static object CreateBody(string code, string? arabicName = "أداة") =>
        new { code, kind = "family_voices", name = new { ar = arabicName, en = "Survey" }, description = new { ar = "وصف", en = "About" } };

    [Fact]
    public async Task Creating_an_instrument_returns_201_with_its_location_and_snake_case_enums()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync("/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument")), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<InstrumentResponse>(cancellationToken);
        Assert.Equal($"/api/v1/instruments/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Contains("\"kind\":\"family_voices\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.True(created.HasCompleteText);
    }

    [Fact]
    public async Task An_instrument_may_be_created_before_its_arabic_name_is_known()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            "/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument"), arabicName: null), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await response.ReadAsync<InstrumentResponse>(cancellationToken)).HasCompleteText);
    }

    [Fact]
    public async Task An_invalid_create_request_returns_400_with_field_errors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            "/api/v1/instruments",
            new { code = "", kind = "family_voices", name = new { ar = (string?)null, en = "" }, description = new { ar = (string?)null, en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"code\"", body, StringComparison.Ordinal);
        Assert.Contains("\"name.en\"", body, StringComparison.Ordinal);
    }

    private static string CreateJson(string code) =>
        $$$"""{"code":"{{{code}}}","kind":"family_voices","name":{"ar":"أداة","en":"Survey"},"description":{"ar":"وصف","en":"About"}}""";

    // A lenient reader would accept all five. The request has no number, so the third case is an enum sent as a number.
    [Theory]
    [InlineData("\"en\":\"Survey\"", "\"en\":\"Survey\",\"fr\":\"Survey\"")]
    [InlineData("\"ar\":\"وصف\",", "")]
    [InlineData("\"kind\":\"family_voices\"", "\"kind\":2")]
    [InlineData("\"en\":\"Survey\"", "\"en\":\"Survey\",\"en\":\"Other\"")]
    [InlineData("\"kind\"", "\"Kind\"")]
    public async Task A_create_request_that_is_not_strict_json_returns_400_at_the_path_and_saves_nothing(string original, string replacement)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var code = TestData.UniqueCode("api_instrument");

        using var response = await client.PostRawJsonAsync("/api/v1/instruments", CreateJson(code).ReplaceFirst(original, replacement), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await response.ErrorKeysAsync(cancellationToken), key => key.StartsWith('$'));
        Assert.False(await factory.QueryAsync(db => db.Instruments.AnyAsync(instrument => instrument.Code == code, cancellationToken)));
    }

    [Fact]
    public async Task A_taken_code_returns_409_with_its_error_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var code = TestData.UniqueCode("api_instrument");
        using var first = await client.PostJsonAsync("/api/v1/instruments", CreateBody(code), cancellationToken);

        using var second = await client.PostJsonAsync("/api/v1/instruments", CreateBody(code), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("questionnaires.instrument_code_taken", await second.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_unknown_instrument_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instruments/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.instrument_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Instruments_are_listed_a_page_at_a_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        using var first = await client.PostJsonAsync("/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument")), cancellationToken);
        using var second = await client.PostJsonAsync("/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument")), cancellationToken);

        using var response = await client.GetAsync("/api/v1/instruments?page=1&pageSize=1", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResponse<InstrumentResponse>>(cancellationToken);
        Assert.Single(page.Items);
        Assert.Equal(1, page.PageSize);
        Assert.True(page.TotalCount >= 2);
    }

    [Fact]
    public async Task A_page_size_over_100_is_reported_under_the_camel_case_key()
    {
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync("/api/v1/instruments?pageSize=101", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"pageSize\":", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_size_over_100_returns_400()
    {
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync("/api/v1/instruments?pageSize=101", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completing_the_arabic_name_makes_the_text_complete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        using var created = await client.PostJsonAsync(
            "/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument"), arabicName: null), cancellationToken);
        var instrument = await created.ReadAsync<InstrumentResponse>(cancellationToken);

        using var response = await client.PutJsonAsync(
            $"/api/v1/instruments/{instrument.Id}/text",
            new { name = new { ar = "أداة", en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.ReadAsync<InstrumentResponse>(cancellationToken)).HasCompleteText);
    }

    [Fact]
    public async Task Changing_the_text_without_arabic_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        using var created = await client.PostJsonAsync("/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument")), cancellationToken);
        var instrument = await created.ReadAsync<InstrumentResponse>(cancellationToken);

        using var response = await client.PutJsonAsync(
            $"/api/v1/instruments/{instrument.Id}/text",
            new { name = new { ar = (string?)null, en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("common.text_required", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_existing_instrument_can_be_fetched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        using var created = await client.PostJsonAsync("/api/v1/instruments", CreateBody(TestData.UniqueCode("api_instrument")), cancellationToken);
        var instrument = await created.ReadAsync<InstrumentResponse>(cancellationToken);

        using var response = await client.GetAsync($"/api/v1/instruments/{instrument.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(instrument.Code, (await response.ReadAsync<InstrumentResponse>(cancellationToken)).Code);
    }

    [Fact]
    public async Task Changing_the_text_of_an_unknown_instrument_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PutJsonAsync(
            $"/api/v1/instruments/{Guid.NewGuid()}/text",
            new { name = new { ar = "أداة", en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.instrument_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_page_number_that_would_overflow_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync("/api/v1/instruments?page=2147483647&pageSize=100", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"page\":", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }
}
