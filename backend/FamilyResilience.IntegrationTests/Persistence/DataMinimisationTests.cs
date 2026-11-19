using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// The platform stores no direct identifiers (docs/security-model.md, data minimisation): no national or Emirates ID,
/// passport, phone, email, address, location, names of people or exact birth dates. A column whose name suggests one
/// fails this test until the field is approved and added to <see cref="Approved"/> with the decision that allows it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DataMinimisationTests(DatabaseFixture fixture)
{
    private static readonly string[] Identifying =
    [
        "national", "emirates", "passport", "identity_number", "id_number", "phone", "mobile_number", "email", "address",
        "street", "latitude", "longitude", "location", "first_name", "last_name", "full_name", "given_name", "family_name",
        "date_of_birth", "birth_date", "birthdate", "ip_address",
    ];

    /// <summary>Table and column pairs allowed despite a match, each with its approval. None so far.</summary>
    private static readonly HashSet<string> Approved = new(StringComparer.Ordinal);

    [Fact]
    public void No_column_holds_a_direct_identifier()
    {
        using var scope = fixture.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>().Model;

        var suspicious = model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => $"{entity.GetTableName()}.{property.GetColumnName()}"))
            .Where(column => Identifying.Any(fragment => column.Contains(fragment, StringComparison.Ordinal)))
            .Where(column => !Approved.Contains(column))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(suspicious.Count == 0, $"These columns look like direct identifiers; get them approved first: {string.Join(", ", suspicious)}");
    }

    [Fact]
    public void The_check_would_catch_an_identifier()
    {
        Assert.Contains(Identifying, fragment => "family_members.emirates_id".Contains(fragment, StringComparison.Ordinal));
        Assert.Contains(Identifying, fragment => "platform_users.email".Contains(fragment, StringComparison.Ordinal));
        Assert.DoesNotContain(Identifying, fragment => "family_members.birth_year".Contains(fragment, StringComparison.Ordinal));
    }
}
