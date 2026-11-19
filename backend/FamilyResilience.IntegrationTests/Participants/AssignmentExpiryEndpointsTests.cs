using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// After the wave closes. The shared wave closes 90 days after <see cref="TestData.Now"/>, so these tests talk to a
/// second host on the same database whose clock stands a day after that. They only read and are refused there; running
/// maintenance on that clock would expire every assignment of the shared database, so that test has a database of its
/// own (<see cref="AssignmentExpiryMaintenanceTests"/>).
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class AssignmentExpiryEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private WebApplicationFactory<Program> AfterTheWaveClosed() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(api.Wave.ClosesAt.AddDays(1)))));

    private static HttpClient ParticipantOn(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Participant);
        return client;
    }

    [Fact]
    public async Task Saving_answers_and_submitting_after_the_wave_closed_answer_409_expired_and_nothing_is_scored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        var changed = await Journeys.AnswersAsync(enrolment.Client, api, "A", 1, cancellationToken);
        await using var later = AfterTheWaveClosed();
        using var client = ParticipantOn(later, enrolment.Subject);

        using var saved = await client.PutJsonAsync($"/api/v1/sessions/{sessionId}/responses", new { responses = changed }, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(client, sessionId, Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal("assignments.assignment_expired", await saved.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, submitted.StatusCode);
        Assert.Equal("assignments.assignment_expired", await submitted.ProblemCodeAsync(cancellationToken));
        var session = await Factory.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .Include(candidate => candidate.Responses)
            .SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        Assert.Equal((SessionStatus.InProgress, (DateTimeOffset?)null), (session.Status, session.SubmittedAt));
        Assert.All(session.Responses, response => Assert.Equal((1, TestData.Now), (response.RawValue, response.AnsweredAt)));
        Assert.Equal(0, await Factory.QueryAsync(db => db.ScoreResults.CountAsync(
            score => score.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
    }

}
