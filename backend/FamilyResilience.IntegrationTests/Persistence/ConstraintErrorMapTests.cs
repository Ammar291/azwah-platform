using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ConstraintErrorMapTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Every_mapped_constraint_name_exists_in_the_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var existing = await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>(
                $"""
                 SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname = current_schema()
                 UNION
                 SELECT c.conname AS "Value" FROM pg_constraint c
                 JOIN pg_namespace n ON n.oid = c.connamespace
                 WHERE n.nspname = current_schema()
                 """)
            .ToListAsync(cancellationToken));

        Assert.NotEmpty(ConstraintErrorMap.KnownConstraintNames);
        Assert.All(ConstraintErrorMap.KnownConstraintNames, name => Assert.Contains(name, existing));
    }
}
