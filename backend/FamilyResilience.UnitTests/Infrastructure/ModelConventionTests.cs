using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class ModelConventionTests
{
    private static readonly IModel Model = DesignModel.Load();

    private static readonly HashSet<(Type Principal, Type Dependent)> AllowedCascades =
    [
        (typeof(InstrumentVersion), typeof(InstrumentSection)),
        (typeof(InstrumentSection), typeof(InstrumentItem)),
        (typeof(InstrumentItem), typeof(ResponseOption)),
        (typeof(ScoreResult), typeof(ScoreContribution)),
        (typeof(ScoreResult), typeof(ScoreCategoryScore)),
    ];

    private static IEnumerable<IEntityType> Tables => Model.GetEntityTypes().Where(type => type.GetTableName() is not null);

    [Fact]
    public void Every_enum_is_stored_as_text_with_a_check_constraint_listing_its_names()
    {
        foreach (var entityType in Tables)
        {
            foreach (var property in entityType.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum)
                {
                    continue;
                }

                Assert.Equal(typeof(string), property.GetProviderClrType());
                var name = $"ck_{entityType.GetTableName()}_{property.GetColumnName()}";
                var check = entityType.GetCheckConstraints().SingleOrDefault(constraint => constraint.ModelName == name);
                Assert.NotNull(check);
                Assert.All(Enum.GetNames(enumType), value => Assert.Contains($"'{value}'", check.Sql, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Every_aggregate_root_uses_xmin_as_its_concurrency_token()
    {
        foreach (var entityType in Tables.Where(type => IsAggregateRoot(type.ClrType)))
        {
            var version = entityType.FindProperty("Version");
            Assert.NotNull(version);
            Assert.Equal("xmin", version.GetColumnName());
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        }
    }

    [Fact]
    public void Keys_are_generated_by_the_domain_never_by_the_database() =>
        Assert.All(
            Tables.SelectMany(type => type.FindPrimaryKey()?.Properties ?? []),
            key => Assert.Equal(ValueGenerated.Never, key.ValueGenerated));

    [Fact]
    public void Foreign_keys_restrict_deletes_except_inside_draft_content_and_score_results()
    {
        foreach (var foreignKey in Tables.SelectMany(type => type.GetForeignKeys()))
        {
            var pair = (foreignKey.PrincipalEntityType.ClrType, foreignKey.DeclaringEntityType.ClrType);
            var expected = AllowedCascades.Contains(pair) ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
            Assert.True(
                foreignKey.DeleteBehavior == expected,
                $"{pair.Item2.Name} -> {pair.Item1.Name} deletes with {foreignKey.DeleteBehavior}, expected {expected}.");
        }
    }

    [Fact]
    public void Value_objects_never_become_tables()
    {
        var mapped = Model.GetEntityTypes().Select(type => type.ClrType).ToList();

        Assert.DoesNotContain(typeof(LocalizedText), mapped);
        Assert.DoesNotContain(typeof(YearMonth), mapped);
        Assert.DoesNotContain(typeof(Approval), mapped);
    }

    [Fact]
    public void Bilingual_text_maps_to_ar_and_en_columns()
    {
        foreach (var complex in Tables.SelectMany(type => type.GetComplexProperties())
                     .Where(property => property.ClrType == typeof(LocalizedText)))
        {
            var columns = complex.ComplexType.GetProperties()
                .Select(property => property.GetColumnName())
                .Order(StringComparer.Ordinal)
                .ToArray();
            var prefix = columns[0][..^3];
            Assert.Equal(new[] { $"{prefix}_ar", $"{prefix}_en" }, columns);
        }
    }

    [Fact]
    public void Outbox_messages_keep_payloads_as_jsonb_with_a_pending_index()
    {
        var outbox = Model.FindEntityType(typeof(OutboxMessage));
        Assert.NotNull(outbox);

        Assert.Equal("outbox_messages", outbox.GetTableName());
        Assert.Equal("jsonb", outbox.FindProperty(nameof(OutboxMessage.Payload))?.GetColumnType());
        var pending = Assert.Single(outbox.GetIndexes(), index => index.GetDatabaseName() == "ix_outbox_messages_pending");
        Assert.Equal("processed_at IS NULL", pending.GetFilter());
    }

    [Fact]
    public void Assignments_are_indexed_by_member_for_the_member_foreign_key()
    {
        var assignments = Model.FindEntityType(typeof(AssessmentAssignment));
        Assert.NotNull(assignments);

        var index = Assert.Single(
            assignments.GetIndexes(), candidate => candidate.GetDatabaseName() == "ix_assessment_assignments_family_member");
        Assert.Equal(new[] { "family_member_id" }, index.Properties.Select(property => property.GetColumnName()).ToArray());
        Assert.False(index.IsUnique);
        Assert.Null(index.GetFilter());
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}
