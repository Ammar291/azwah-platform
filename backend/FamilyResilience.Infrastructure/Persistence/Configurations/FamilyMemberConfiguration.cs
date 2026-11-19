using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("family_members", table =>
        {
            table.HasCheckConstraint("ck_family_members_birth_month", "birth_month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_family_members_birth_year", "birth_year >= 1900");
            table.HasCheckConstraint(
                "ck_family_members_head_has_no_relationship",
                "NOT is_head_of_household OR relationship_to_household IS NULL");
        });
        builder.HasKey(member => member.Id);
        builder.ComplexProperty(member => member.BirthMonth, birth =>
        {
            birth.Property(value => value.Year).HasColumnName("birth_year").HasConversion<short>();
            birth.Property(value => value.Month).HasColumnName("birth_month").HasConversion<short>();
        });

        // Target of composite foreign keys that must agree on the member's household (consent records).
        builder.HasAlternateKey(member => new { member.Id, member.HouseholdId }).HasName("ak_family_members_id_household");

        builder.HasIndex(member => member.HouseholdId, "ix_family_members_household_id")
            .HasDatabaseName("ix_family_members_household_id");
        builder.HasIndex(member => member.HouseholdId, "ux_family_members_one_active_head")
            .HasDatabaseName("ux_family_members_one_active_head")
            .IsUnique()
            .HasFilter("is_head_of_household AND status = 'Active'");
        builder.HasIndex(member => member.HouseholdId, "ux_family_members_one_active_primary_earner")
            .HasDatabaseName("ux_family_members_one_active_primary_earner")
            .IsUnique()
            .HasFilter("is_primary_income_earner AND status = 'Active'");
    }
}
