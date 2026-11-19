using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Maintenance after the wave closed, on a <see cref="ParticipantApi"/> of its own: the run expires every overdue
/// assignment of its database, so on the shared participant database it would expire the other test classes'
/// assignments and abandon their sessions. Here the only household is this test's, so the counts are exact.
/// </summary>
public sealed class AssignmentExpiryMaintenanceTests(ParticipantApi api) : IClassFixture<ParticipantApi>
{
    private ApiFactory Factory => api.Factory;

    private WebApplicationFactory<Program> AfterTheWaveClosed() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(api.Wave.ClosesAt.AddDays(1)))));

    [Fact]
    public async Task Maintenance_closes_what_the_wave_left_open_and_monitoring_then_counts_it_as_expired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var admin = Factory.AsProgramAdministrator();
        var participation = $"/api/v1/admin/participation?reference={enrolment.Household.Reference}";
        using var before = await admin.GetAsync(participation, cancellationToken);
        await using var later = AfterTheWaveClosed();

        OverdueAssignmentsReport report;
        await using (var scope = later.Services.CreateAsyncScope())
        {
            report = TestData.Ok(await scope.ServiceProvider.GetRequiredService<ExpireOverdueAssignments>().HandleAsync(cancellationToken));
        }

        using var after = await admin.GetAsync(participation, cancellationToken);
        using var assessments = await admin.GetAsync($"/api/v1/admin/assessments?householdId={enrolment.Household.Id}", cancellationToken);

        // The head's module A was in progress and the parent's module B had not been started.
        Assert.Equal(
            new AssignmentCountsResponse(1, 1, 0, 0, 0),
            Assert.Single((await before.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).Items).Assignments);
        Assert.Equal((2, 1), (report.AssignmentsExpired, report.SessionsAbandoned));
        Assert.Empty(report.Failures);
        Assert.Equal(
            new AssignmentCountsResponse(0, 0, 0, 2, 0),
            Assert.Single((await after.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).Items).Assignments);
        var rows = (await assessments.ReadAsync<PagedResponse<AssessmentRowResponse>>(cancellationToken)).Items;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(
            (AssignmentStatus.Expired, (SessionStatus?)null, false), (row.AssignmentStatus, row.SessionStatus, row.Scored)));
        var abandoned = await Factory.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        Assert.Equal((SessionStatus.Abandoned, (DateTimeOffset?)api.Wave.ClosesAt.AddDays(1)), (abandoned.Status, abandoned.AbandonedAt));
        // The platform itself acted: no subject is recorded. Nothing outside this household was closed.
        var audit = await Factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.Action == "sessions.session.abandoned" || candidate.Action == "assignments.assignment.expired")
            .ToListAsync(cancellationToken));
        Assert.Equal(sessionId.ToString(), Assert.Single(audit, candidate => candidate.Action == "sessions.session.abandoned").ResourceId);
        Assert.Equal(
            rows.Select(row => (string?)row.AssignmentId.ToString()).Order(StringComparer.Ordinal),
            audit.Where(candidate => candidate.Action == "assignments.assignment.expired").Select(candidate => candidate.ResourceId).Order(StringComparer.Ordinal));
        Assert.All(audit, candidate => Assert.Equal((AuditActorType.System, (string?)null), (candidate.ActorType, candidate.ActorId)));

        // The participant still sees the translated "time has passed" state, on the ordinary clock too.
        var answers = await Journeys.AnswersAsync(enrolment.Client, api, "A", 1, cancellationToken);
        using var saved = await enrolment.Client.PutJsonAsync($"/api/v1/sessions/{sessionId}/responses", new { responses = answers }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal("assignments.assignment_expired", await saved.ProblemCodeAsync(cancellationToken));
    }
}
