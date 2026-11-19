using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// A synthetic, clearly labelled demonstration for a live phone → API → dashboard walk-through (D-140), written only to a
/// database whose name ends in <c>_demo</c> (<see cref="DemonstrationDatabase"/>; the API's <c>db demo</c> command is
/// Development only). It holds the development seed's content plus LOCAL DEMONSTRATION approvals of the household
/// assessment, its proposed scoring, a demonstration eligibility rule (the head of household answers) and placeholder
/// consent policies; two waves (one closed, one open); and sixty nameless households whose heads answered through the
/// domain and were scored by the approved rule set (<see cref="DemonstrationPopulation"/>). The family survey stays a
/// draft with proposed rules; no referral rule, partner or support resource exists. The development identity provider's
/// participant personas are linked: the lead heads household 1, the member is that household's spouse, the new
/// participant has no household.
/// <para>
/// Everything is written in one transaction, so a build either completes or leaves the database empty, and the last
/// thing it writes is the marker (<see cref="SeededAuditAction"/>) that <see cref="DemonstrationDatabase.InspectAsync"/>
/// looks for before <c>db demo</c> drops or migrates anything. A database that already holds the demonstration is left
/// as it is; one holding anything else is refused here too, as a last check after the command's own inspection.
/// </para>
/// </summary>
public static class DemonstrationDataSeeder
{
    public const string WaveOneCode = "demo_wave_1";
    public const string WaveTwoCode = "demo_wave_2";
    public const string EligibilityRuleSetCode = "demo_household_assessment_head";

    /// <summary>The demonstration's eligibility rule: the head of household answers the whole household assessment.</summary>
    public const string EligibilityDefinition = """{"schema":"eligibility.v1","target":null,"rule":{"isHeadOfHousehold":true}}""";

    /// <summary>The approval source of every demonstration approval. Nothing here is approved by a research owner.</summary>
    public const string ApprovalReference = "LOCAL DEMONSTRATION ONLY (D-140): not approved by a research owner";

    /// <summary>The consent policies' document reference, in the development seed's placeholder convention.</summary>
    public const string ConsentDocumentReference = "demo-placeholder (not reviewed consent text)";

    /// <summary>The approver and audit actor: a subject no identity provider issues.</summary>
    public const string SeedSubject = "azwah-demonstration-seed";

    /// <summary>The development identity provider's participant personas (FamilyResilience.IdentityProvider/Personas.cs).</summary>
    public const string LeadSubject = "dev-participant-lead";

    public const string MemberSubject = "dev-participant-member";

    public const string NewParticipantSubject = "dev-participant-new";

    /// <summary>The audit event recording that the seed built the database.</summary>
    public const string SeededAuditAction = "demonstration.database.seeded";

    /// <summary>
    /// Builds the demonstration once. Returns false, changing nothing, when the database already holds it. Throws when
    /// the database's name does not end in <c>_demo</c>, or when it holds anything other than the demonstration.
    /// </summary>
    public static async Task<bool> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var dbContext = provider.GetRequiredService<FamilyResilienceDbContext>();
        await EnsureDemonstrationDatabaseAsync(dbContext, cancellationToken);

        if (await dbContext.Waves.AnyAsync(wave => wave.Code == WaveOneCode, cancellationToken))
        {
            return false;
        }

        if (await dbContext.Instruments.AnyAsync(cancellationToken) || await dbContext.Households.AnyAsync(cancellationToken)
            || await dbContext.Waves.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The demonstration database holds data that is not the demonstration. Wipe and rebuild it with 'db demo --reset'.");
        }

        var plan = DemonstrationPopulation.Build(provider.GetRequiredService<TimeProvider>().GetUtcNow());
        var builder = new DemonstrationBuilder(provider, plan);
        var built = await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async token =>
            {
                await builder.BuildAsync(token);
                return Result.Success(true);
            },
            cancellationToken);
        return built.IsSuccess ? built.Value : throw new InvalidOperationException($"Building the demonstration database failed: {built.Error!.Code}");
    }

    /// <summary>What the demonstration database holds, as lines for the console: counts only, never data.</summary>
    public static async Task<IReadOnlyList<string>> SummaryAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();
        var database = await CurrentDatabaseAsync(dbContext, cancellationToken);
        var households = await dbContext.Households.AsNoTracking()
            .GroupBy(household => household.Status).Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var members = await dbContext.Set<FamilyMember>().AsNoTracking().CountAsync(cancellationToken);
        var lines = new List<string>
        {
            $"Demonstration database '{database}' (LOCAL DEMONSTRATION ONLY, D-140): {households.Sum(row => row.Count)} households "
            + $"({Count(households.Where(row => row.Key == HouseholdStatus.Active).Select(row => row.Count))} active, "
            + $"{Count(households.Where(row => row.Key == HouseholdStatus.Withdrawn).Select(row => row.Count))} withdrawn), {members} members.",
        };

        var waves = await dbContext.Waves.AsNoTracking().OrderBy(wave => wave.OpensAt).ToListAsync(cancellationToken);
        foreach (var wave in waves)
        {
            var assignments = await dbContext.AssessmentAssignments.AsNoTracking().Where(assignment => assignment.WaveId == wave.Id)
                .GroupBy(assignment => assignment.Status).Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var sessions = await dbContext.AssessmentSessions.AsNoTracking()
                .Join(dbContext.AssessmentAssignments.Where(assignment => assignment.WaveId == wave.Id),
                    session => session.AssessmentAssignmentId, assignment => assignment.Id, (session, _) => session.Status)
                .GroupBy(status => status).Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var tiers = await dbContext.ScoreResults.AsNoTracking()
                .Join(dbContext.AssessmentSessions, result => result.AssessmentSessionId, session => session.Id,
                    (result, session) => new { result.TierCode, session.AssessmentAssignmentId })
                .Join(dbContext.AssessmentAssignments.Where(assignment => assignment.WaveId == wave.Id),
                    row => row.AssessmentAssignmentId, assignment => assignment.Id, (row, _) => row.TierCode)
                .GroupBy(tier => tier).Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            lines.Add(
                $"  {wave.Code} ({wave.Status.ToString().ToLowerInvariant()}): assignments {assignments.Sum(row => row.Count)} "
                + $"({Describe(assignments.Select(row => (row.Key.ToString(), row.Count)))}); "
                + $"sessions {sessions.Sum(row => row.Count)} ({Describe(sessions.Select(row => (row.Key.ToString(), row.Count)))}); "
                + $"results {tiers.Sum(row => row.Count)} ({Describe(tiers.Select(row => (row.Key ?? "no tier", row.Count)))}).");
        }

        return lines;
    }

    private static async Task EnsureDemonstrationDatabaseAsync(FamilyResilienceDbContext dbContext, CancellationToken cancellationToken)
    {
        var name = await CurrentDatabaseAsync(dbContext, cancellationToken);
        if (!DemonstrationDatabase.IsDemonstrationName(name))
        {
            throw new InvalidOperationException(
                $"Demonstration data is written only to a database whose name ends in '{DemonstrationDatabase.Suffix}'; "
                + $"this one is '{name}'. Use 'db demo', which derives the demonstration database from the configured one.");
        }
    }

    private static async Task<string> CurrentDatabaseAsync(FamilyResilienceDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Database.SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync(cancellationToken);

    private static int Count(IEnumerable<int> counts) => counts.Sum();

    private static string Describe(IEnumerable<(string Name, int Count)> counts) =>
        string.Join(", ", counts.OrderBy(row => row.Name, StringComparer.Ordinal).Select(row => $"{row.Name} {row.Count}"));
}
