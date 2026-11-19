using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class QuestionnaireAndAssignmentEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private static async Task<RosterConfirmationResponse> ConfirmAsync(Enrolment enrolment, CancellationToken cancellationToken)
    {
        using var response = await enrolment.Client.PostJsonAsync(
            $"/api/v1/households/{enrolment.Household.Id}/confirm-roster", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<RosterConfirmationResponse>(cancellationToken);
    }

    [Fact]
    public async Task Available_questionnaires_list_the_version_in_effect_with_its_sections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.GetAsync("/api/v1/questionnaires/available", cancellationToken);

        var page = await response.ReadAsync<PagedResponse<AvailableQuestionnaireResponse>>(cancellationToken);
        var seeded = Assert.Single(page.Items, item => item.InstrumentCode == api.Instrument.Code);
        Assert.Equal(api.Version.VersionNumber, seeded.VersionNumber);
        Assert.Equal(["A", "B"], seeded.Sections.Select(section => section.Code));
    }

    [Fact]
    public async Task A_published_version_shows_content_in_order_without_any_scoring_detail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.GetAsync(
            $"/api/v1/questionnaires/{api.Instrument.Code}/versions/{api.Version.VersionNumber}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var item = document.RootElement.GetProperty("sections")[0].GetProperty("items")[0];
        Assert.False(item.TryGetProperty("scoringDirection", out _));
        Assert.False(item.TryGetProperty("scoringCategory", out _));
        Assert.False(item.GetProperty("options")[0].TryGetProperty("value", out _));
        var version = await response.ReadAsync<PublishedVersionResponse>(cancellationToken);
        Assert.Equal(["A1", "A2"], version.Sections[0].Items.Select(candidate => candidate.Code));
        Assert.Equal(["o1", "o2", "o3"], version.Sections[0].Items[0].Options.Select(option => option.Code));
    }

    [Fact]
    public async Task A_draft_version_and_an_unknown_instrument_are_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await Factory.DraftVersionAsync(cancellationToken, api.Instrument, TestData.NextVersionNumber());
        using var client = Factory.AsParticipant();

        using var drafted = await client.GetAsync($"/api/v1/questionnaires/{api.Instrument.Code}/versions/{draft.VersionNumber}", cancellationToken);
        using var unknown = await client.GetAsync("/api/v1/questionnaires/no_such_instrument/versions/1", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, drafted.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await drafted.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("questionnaires.instrument_not_found", await unknown.ProblemCodeAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_may_read_published_content(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("staff"), role);

        using var response = await client.GetAsync(
            $"/api/v1/questionnaires/{api.Instrument.Code}/versions/{api.Version.VersionNumber}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_read_questionnaires()
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync("/api/v1/questionnaires/available", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_caller_sees_their_households_assignments_filtered_and_paged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var confirmed = await ConfirmAsync(enrolment, cancellationToken);
        using var other = await Enrolment.CreateAsync(Factory, cancellationToken);
        await ConfirmAsync(other, cancellationToken);

        using var all = await enrolment.Client.GetAsync("/api/v1/assignments", cancellationToken);
        using var assigned = await enrolment.Client.GetAsync("/api/v1/assignments?status=assigned&pageSize=1", cancellationToken);
        using var completed = await enrolment.Client.GetAsync("/api/v1/assignments?status=completed", cancellationToken);
        using var invalid = await enrolment.Client.GetAsync("/api/v1/assignments?status=sleeping", cancellationToken);

        var everything = await all.ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(cancellationToken);
        Assert.Equal(
            confirmed.Assignments.Select(assignment => assignment.AssignmentId).Order(),
            everything.Items.Select(assignment => assignment.Id).Order());
        // Which instrument each answers, so the app tells the household assessment from the modules (D-146).
        Assert.All(everything.Items, assignment => Assert.Equal(api.Instrument.Kind, assignment.InstrumentKind));
        var firstPage = await assigned.ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(cancellationToken);
        Assert.Equal((1, 2), (firstPage.Items.Count, firstPage.TotalCount));
        Assert.Equal(0, (await completed.ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(cancellationToken)).TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/assignments?status=in_progress")]
    [InlineData("/api/v1/assignments?status=InProgress")]
    public async Task A_two_word_status_filter_binds_in_snake_case_as_the_json_writes_it(string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);

        using var response = await enrolment.Client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task An_empty_status_filter_means_no_filter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);

        using var response = await enrolment.Client.GetAsync("/api/v1/assignments?status=", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/assignments?status=in__progress")]
    [InlineData("/api/v1/assignments?status=1")]
    public async Task A_status_that_is_not_a_name_is_refused(string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);

        using var response = await enrolment.Client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("status", await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task One_assignment_shows_what_to_answer_and_another_familys_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var assignmentId = Assert.Single((await ConfirmAsync(enrolment, cancellationToken)).Assignments).AssignmentId;
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken);
        using var staff = Factory.As(ApiClients.NewSubject("staff"), Roles.ProgramAdministrator);

        using var mine = await enrolment.Client.GetAsync($"/api/v1/assignments/{assignmentId}", cancellationToken);
        using var theirs = await stranger.Client.GetAsync($"/api/v1/assignments/{assignmentId}", cancellationToken);
        using var unknown = await enrolment.Client.GetAsync($"/api/v1/assignments/{Guid.NewGuid()}", cancellationToken);
        using var asStaff = await staff.GetAsync($"/api/v1/assignments/{assignmentId}", cancellationToken);

        var assignment = await mine.ReadAsync<ParticipantAssignmentResponse>(cancellationToken);
        Assert.Equal(
            (enrolment.Self, api.Instrument.Code, "A", api.Wave.Code, AssignmentStatus.Assigned, (Guid?)null, (DateTimeOffset?)api.Wave.ClosesAt),
            (assignment.MemberId, assignment.InstrumentCode, assignment.SectionCode, assignment.WaveCode, assignment.Status, assignment.ActiveSessionId, assignment.ExpiresAt));
        Assert.Equal("Section", assignment.SectionTitle?.En);
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal("assignments.assignment_not_found", await theirs.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
    }
}
