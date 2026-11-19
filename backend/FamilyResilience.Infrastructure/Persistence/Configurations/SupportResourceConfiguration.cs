using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class SupportResourceConfiguration : IEntityTypeConfiguration<SupportResource>
{
    public void Configure(EntityTypeBuilder<SupportResource> builder)
    {
        builder.ToTable("support_resources", table =>
        {
            table.HasCheckConstraint("ck_support_resources_url_https", "url LIKE 'https://%'");
            table.HasCheckConstraint(
                "ck_support_resources_categories",
                $"cardinality(categories) BETWEEN 1 AND {SupportResourceContent.MaxCategories}");
            table.HasCheckConstraint(
                "ck_support_resources_audiences",
                $"cardinality(audiences) <= {SupportResourceContent.MaxAudiences}");
            table.HasCheckConstraint(
                "ck_support_resources_service_languages",
                $"cardinality(service_languages) BETWEEN 1 AND {SupportResourceContent.MaxServiceLanguages}");
            table.HasCheckConstraint("ck_support_resources_version_number", "version_number >= 1");
            table.HasCheckConstraint(
                "ck_support_resources_approval",
                "status = 'Draft' OR (name_ar IS NOT NULL AND summary_ar IS NOT NULL AND provider_name_ar IS NOT NULL"
                + " AND approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL"
                + " AND review_due_at IS NOT NULL AND review_due_at > approved_at)");
            table.HasCheckConstraint("ck_support_resources_retired", "(status = 'Retired') = (retired_at IS NOT NULL)");
        });
        builder.HasKey(resource => resource.Id);
        builder.Property(resource => resource.Code).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.HasLocalizedText(resource => resource.Name, "name");
        builder.HasLocalizedText(resource => resource.Summary, "summary");
        builder.HasLocalizedText(resource => resource.ProviderName, "provider_name");
        builder.Property(resource => resource.Url).HasMaxLength(SupportResourceContent.MaxUrlLength).IsRequired();
        builder.PrimitiveCollection(resource => resource.Categories)
            .HasField("_categories")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .ElementType()
            .HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.PrimitiveCollection(resource => resource.Audiences)
            .HasField("_audiences")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .ElementType()
            .HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.PrimitiveCollection(resource => resource.ServiceLanguages)
            .HasField("_serviceLanguages")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .ElementType()
            .HasMaxLength(2);
        builder.Property(resource => resource.ApprovedBy).HasMaxLength(Approval.MaxApprovedByLength);
        builder.Property(resource => resource.ApprovalSource).HasMaxLength(Approval.MaxSourceLength);

        // Stored generated column: PostgreSQL maintains it, EF Core never writes it, and no domain type sees it.
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(SupportResourceSearchSql.VectorExpression, stored: true);
        builder.HasIndex(["SearchVector"], "ix_support_resources_search_vector")
            .HasDatabaseName("ix_support_resources_search_vector")
            .HasMethod("gin");

        builder.HasIndex(resource => new { resource.Code, resource.VersionNumber }, "ux_support_resources_code_version")
            .HasDatabaseName("ux_support_resources_code_version")
            .IsUnique();
        builder.HasIndex(resource => resource.Code, "ux_support_resources_one_approved_per_code")
            .HasDatabaseName("ux_support_resources_one_approved_per_code")
            .IsUnique()
            .HasFilter("status = 'Approved'");
        builder.HasIndex(resource => resource.ReviewDueAt, "ix_support_resources_discoverable")
            .HasDatabaseName("ix_support_resources_discoverable")
            .HasFilter("status = 'Approved'");
    }
}
