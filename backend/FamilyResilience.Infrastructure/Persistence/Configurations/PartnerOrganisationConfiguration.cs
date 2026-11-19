using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class PartnerOrganisationConfiguration : IEntityTypeConfiguration<PartnerOrganisation>
{
    public void Configure(EntityTypeBuilder<PartnerOrganisation> builder)
    {
        builder.ToTable("partner_organisations", table =>
        {
            table.HasCheckConstraint("ck_partner_organisations_name_ar", "name_ar IS NOT NULL");
            table.HasCheckConstraint(
                "ck_partner_organisations_categories",
                $"cardinality(categories) BETWEEN 1 AND {PartnerOrganisation.MaxCategories}");
        });
        builder.HasKey(partner => partner.Id);
        builder.Property(partner => partner.Code).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.HasLocalizedText(partner => partner.Name, "name");
        builder.PrimitiveCollection(partner => partner.Categories)
            .HasField("_categories")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .ElementType()
            .HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.HasIndex(partner => partner.Code, "ux_partner_organisations_code")
            .HasDatabaseName("ux_partner_organisations_code")
            .IsUnique();
    }
}
