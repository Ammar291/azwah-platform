using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FamilyResilience.Infrastructure.Persistence.Erasure;

/// <summary>
/// The erasure procedure (D-88): parameterised deletes in the transaction the use case opened, after
/// <c>SET LOCAL azwah.erasure = 'on'</c>, which the response, referral and safety-alert guards require. First it locks the
/// accounts being erased (the household's users with no membership in another household, <c>FOR UPDATE</c>, so no alert
/// or membership can be added for them meanwhile). Order: safety alerts about the household, its sessions or those
/// accounts, recommendations, referrals, score results (their contributions and category scores cascade), responses,
/// sessions, assignments, privacy requests, consent records, memberships, members, the household, the accounts and their
/// role snapshots, then idempotency keys and outbox messages that name any erased ID. Audit events are kept. An account is
/// never kept for an alert's sake; an account that still belongs to another household keeps its alerts about that
/// household and its own.
/// </summary>
public sealed class HouseholdErasure(FamilyResilienceDbContext dbContext) : IHouseholdErasure
{
    public async Task<bool> LockAsync(HouseholdId householdId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = Transaction();
        await using var command = new NpgsqlCommand("SELECT 1 FROM households WHERE id = @h FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("h", householdId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<IReadOnlyDictionary<string, int>> EraseAsync(HouseholdId householdId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = Transaction();
        var household = householdId.Value;

        async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        async Task<List<T>> ReadAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var values = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(reader.GetFieldValue<T>(0));
            }

            return values;
        }

        await ExecuteAsync("SET LOCAL azwah.erasure = 'on'");
        var members = (await ReadAsync<Guid>("SELECT id FROM family_members WHERE household_id = @h", ("h", household))).ToArray();
        var users = (await ReadAsync<Guid>("SELECT DISTINCT platform_user_id FROM household_memberships WHERE household_id = @h", ("h", household))).ToArray();
        var sessions = (await ReadAsync<Guid>("SELECT id FROM assessment_sessions WHERE family_member_id = ANY(@m)", ("m", members))).ToArray();
        // Inserting an alert or a membership takes FOR KEY SHARE on the account row, so neither can slip in before the
        // account is deleted below.
        var accounts = (await ReadAsync<Guid>(
            """
            SELECT users.id FROM platform_users AS users
            WHERE users.id = ANY(@u)
              AND NOT EXISTS (
                  SELECT 1 FROM household_memberships AS memberships
                  WHERE memberships.platform_user_id = users.id AND memberships.household_id <> @h)
            FOR UPDATE OF users
            """,
            ("u", users), ("h", household))).ToArray();
        var named = new List<Guid> { household };
        named.AddRange(members);
        named.AddRange(sessions);
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM assessment_assignments WHERE family_member_id = ANY(@m)", ("m", members)));
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM score_results WHERE assessment_session_id = ANY(@s)", ("s", sessions)));
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM support_referrals WHERE family_member_id = ANY(@m)", ("m", members)));
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM support_recommendations WHERE family_member_id = ANY(@m)", ("m", members)));
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM privacy_requests WHERE household_id = @h", ("h", household)));
        named.AddRange(await ReadAsync<Guid>("SELECT id FROM consent_records WHERE household_id = @h", ("h", household)));
        named.AddRange(await ReadAsync<Guid>(
            "SELECT id FROM safety_alerts WHERE household_id = @h OR assessment_session_id = ANY(@s) OR platform_user_id = ANY(@a)",
            ("h", household), ("s", sessions), ("a", accounts)));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["safety_alerts"] = await ExecuteAsync(
                "DELETE FROM safety_alerts WHERE household_id = @h OR assessment_session_id = ANY(@s) OR platform_user_id = ANY(@a)",
                ("h", household), ("s", sessions), ("a", accounts)),
            ["support_recommendations"] = await ExecuteAsync("DELETE FROM support_recommendations WHERE family_member_id = ANY(@m)", ("m", members)),
            ["support_referrals"] = await ExecuteAsync("DELETE FROM support_referrals WHERE family_member_id = ANY(@m)", ("m", members)),
            ["score_results"] = await ExecuteAsync("DELETE FROM score_results WHERE assessment_session_id = ANY(@s)", ("s", sessions)),
            ["assessment_responses"] = await ExecuteAsync("DELETE FROM assessment_responses WHERE assessment_session_id = ANY(@s)", ("s", sessions)),
            ["assessment_sessions"] = await ExecuteAsync("DELETE FROM assessment_sessions WHERE id = ANY(@s)", ("s", sessions)),
            ["assessment_assignments"] = await ExecuteAsync("DELETE FROM assessment_assignments WHERE family_member_id = ANY(@m)", ("m", members)),
        };
        counts["privacy_requests"] = await ExecuteAsync("DELETE FROM privacy_requests WHERE household_id = @h", ("h", household));
        counts["consent_records"] = await ExecuteAsync("DELETE FROM consent_records WHERE household_id = @h", ("h", household));
        counts["household_memberships"] = await ExecuteAsync("DELETE FROM household_memberships WHERE household_id = @h", ("h", household));
        counts["family_members"] = await ExecuteAsync("DELETE FROM family_members WHERE household_id = @h", ("h", household));
        counts["households"] = await ExecuteAsync("DELETE FROM households WHERE id = @h", ("h", household));

        var subjects = (await ReadAsync<string>(
            """
            DELETE FROM platform_users AS users
            WHERE users.id = ANY(@u)
              AND NOT EXISTS (SELECT 1 FROM household_memberships AS memberships WHERE memberships.platform_user_id = users.id)
            RETURNING users.subject
            """,
            ("u", users))).ToArray();
        counts["platform_users"] = subjects.Length;
        counts["role_snapshots"] = await ExecuteAsync("DELETE FROM role_snapshots WHERE subject = ANY(@subjects)", ("subjects", subjects));
        counts["idempotency_keys"] = await ExecuteAsync(
            "DELETE FROM idempotency_keys WHERE subject = ANY(@subjects) OR resource_id = ANY(@ids)",
            ("subjects", subjects), ("ids", named.ToArray()));
        counts["outbox_messages"] = await ExecuteAsync(
            "DELETE FROM outbox_messages WHERE payload::text LIKE ANY(@patterns)",
            ("patterns", named.Select(id => $"%{id:D}%").ToArray()));
        return counts;
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) Transaction()
    {
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("Erasure runs inside the transaction of the use case.");
        return (transaction.Connection ?? throw new InvalidOperationException("The erasure transaction has no connection."), transaction);
    }
}
