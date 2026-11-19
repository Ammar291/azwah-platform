using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Participant journey steps over HTTP against <see cref="ParticipantApi"/>, each asserting success.</summary>
internal static class Journeys
{
    /// <summary>Confirms the roster and returns the assignment of <paramref name="memberId"/> in <paramref name="sectionCode"/>.</summary>
    public static async Task<Guid> AssignmentAsync(Enrolment enrolment, Guid memberId, string sectionCode, CancellationToken cancellationToken)
    {
        using var response = await enrolment.Client.PostJsonAsync(
            $"/api/v1/households/{enrolment.Household.Id}/confirm-roster", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var confirmation = await response.ReadAsync<RosterConfirmationResponse>(cancellationToken);
        return confirmation.Assignments.Single(assignment => assignment.MemberId == memberId && assignment.SectionCode == sectionCode).AssignmentId;
    }

    public static async Task<SessionResponse> StartAsync(HttpClient client, Guid assignmentId, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync($"/api/v1/assignments/{assignmentId}/sessions", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SessionResponse>(cancellationToken);
    }

    /// <summary>One answer per item of <paramref name="sectionCode"/>, choosing the option at <paramref name="optionIndex"/>.</summary>
    public static async Task<object[]> AnswersAsync(
        HttpClient client, ParticipantApi api, string sectionCode, int optionIndex, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/v1/questionnaires/{api.Instrument.Code}/versions/{api.Version.VersionNumber}", cancellationToken);
        var version = await response.ReadAsync<PublishedVersionResponse>(cancellationToken);
        return version.Sections.Single(section => section.Code == sectionCode).Items
            .Select(item => (object)new { itemId = item.Id, optionId = item.Options[optionIndex].Id })
            .ToArray();
    }

    public static async Task SaveAsync(HttpClient client, Guid sessionId, object[] answers, CancellationToken cancellationToken)
    {
        using var response = await client.PutJsonAsync($"/api/v1/sessions/{sessionId}/responses", new { responses = answers }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid sessionId, Guid key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit");
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Grants participation consent for <paramref name="memberId"/> and starts its session, answering nothing.</summary>
    public static async Task<Guid> StartedSessionAsync(
        Enrolment enrolment, ParticipantApi api, Guid memberId, string sectionCode, CancellationToken cancellationToken)
    {
        _ = api;
        await enrolment.Client.GrantConsentAsync(memberId, ConsentType.Participation, cancellationToken);
        var assignmentId = await AssignmentAsync(enrolment, memberId, sectionCode, cancellationToken);
        return (await StartAsync(enrolment.Client, assignmentId, cancellationToken)).Id;
    }

    /// <summary>Grants participation consent for <paramref name="memberId"/>, starts its session and answers every item.</summary>
    public static async Task<Guid> AnsweredSessionAsync(
        Enrolment enrolment, ParticipantApi api, Guid memberId, string sectionCode, int optionIndex, CancellationToken cancellationToken)
    {
        await enrolment.Client.GrantConsentAsync(memberId, ConsentType.Participation, cancellationToken);
        var assignmentId = await AssignmentAsync(enrolment, memberId, sectionCode, cancellationToken);
        var session = await StartAsync(enrolment.Client, assignmentId, cancellationToken);
        await SaveAsync(enrolment.Client, session.Id, await AnswersAsync(enrolment.Client, api, sectionCode, optionIndex, cancellationToken), cancellationToken);
        return session.Id;
    }
}
