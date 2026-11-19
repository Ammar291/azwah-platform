using System.Net;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// For tests that raise safety signals: a participant the server knows (an alert needs a platform user, D-135), and what
/// the recorder saved for a subject.
/// </summary>
internal static class SafetySignals
{
    /// <summary>A new participant subject, registered as a platform user through the API.</summary>
    public static async Task<string> RegisteredParticipantAsync(this ApiFactory factory, CancellationToken cancellationToken)
    {
        var subject = ApiClients.NewSubject("participant");
        using var client = factory.AsParticipant(subject);
        using var response = await client.PostJsonAsync("/api/v1/me/registration", new { detailsConfirmed = true }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return subject;
    }

    /// <summary>The subject's alerts; none when the subject has no platform user.</summary>
    public static Task<List<SafetyAlert>> AlertsOfAsync(this ApiFactory factory, string subject, CancellationToken cancellationToken) =>
        factory.QueryAsync(async db =>
        {
            var user = await db.PlatformUsers.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Subject == subject, cancellationToken);
            return user is null
                ? new List<SafetyAlert>()
                : await db.SafetyAlerts.AsNoTracking().Where(alert => alert.UserId == user.Id).ToListAsync(cancellationToken);
        });

    /// <summary>
    /// Every column of the subject's alerts, their own audit events and the audit events about their alerts (which the
    /// System actor records), as stored, one JSON text per row.
    /// </summary>
    public static Task<List<string>> StoredRowsOfAsync(this ApiFactory factory, string subject, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.Database.SqlQuery<string>($"""
            SELECT row_to_json(a)::text AS "Value"
            FROM safety_alerts a JOIN platform_users u ON u.id = a.platform_user_id
            WHERE u.subject = {subject}
            UNION ALL
            SELECT row_to_json(e)::text AS "Value" FROM audit_events e
            WHERE e.actor_id = {subject}
               OR (e.resource_type = 'safety_alert' AND e.resource_id IN (
                   SELECT a.id::text FROM safety_alerts a JOIN platform_users u ON u.id = a.platform_user_id WHERE u.subject = {subject}))
            """).ToListAsync(cancellationToken));

    /// <summary>The audit events about one alert, oldest first.</summary>
    public static Task<List<AuditEvent>> AlertAuditsAsync(this ApiFactory factory, SafetyAlertId alertId, CancellationToken cancellationToken)
    {
        var resourceId = alertId.Value.ToString();
        return factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ResourceType == "safety_alert" && audit.ResourceId == resourceId)
            .OrderBy(audit => audit.OccurredAt)
            .ThenBy(audit => audit.Id)
            .ToListAsync(cancellationToken));
    }
}
