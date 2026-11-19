using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class HouseholdMembershipConfiguration : IEntityTypeConfiguration<HouseholdMembership>
{
    public void Configure(EntityTypeBuilder<HouseholdMembership> builder)
    {
        builder.ToTable("household_memberships", table =>
            table.HasCheckConstraint(
                "ck_household_memberships_ended_at",
                "(status = 'Ended') = (ended_at IS NOT NULL) AND (ended_at IS NULL OR ended_at >= created_at)"));
        builder.HasKey(membership => membership.Id);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(membership => membership.PlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The linked member must belong to the membership's household.
        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(membership => new { membership.FamilyMemberId, membership.HouseholdId })
            .HasPrincipalKey(member => new { member.Id, member.HouseholdId })
            .HasConstraintName("fk_household_memberships_member_household")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(membership => new { membership.FamilyMemberId, membership.HouseholdId }, "ix_household_memberships_member_household")
            .HasDatabaseName("ix_household_memberships_member_household");

        builder.HasIndex(membership => membership.PlatformUserId, "ux_household_memberships_one_active_per_user")
            .HasDatabaseName("ux_household_memberships_one_active_per_user")
            .IsUnique()
            .HasFilter("status = 'Active'");
        builder.HasIndex(membership => membership.FamilyMemberId, "ux_household_memberships_one_active_per_member")
            .HasDatabaseName("ux_household_memberships_one_active_per_member")
            .IsUnique()
            .HasFilter("status = 'Active'");
        builder.HasIndex(membership => membership.PlatformUserId, "ix_household_memberships_platform_user")
            .HasDatabaseName("ix_household_memberships_platform_user");
    }
}
