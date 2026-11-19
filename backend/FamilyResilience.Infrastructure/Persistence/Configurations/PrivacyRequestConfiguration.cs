using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class PrivacyRequestConfiguration : IEntityTypeConfiguration<PrivacyRequest>
{
    public void Configure(EntityTypeBuilder<PrivacyRequest> builder)
    {
        builder.ToTable("privacy_requests", table =>
            table.HasCheckConstraint(
                "ck_privacy_requests_closing",
                "(status = 'Open' AND closed_at IS NULL AND closed_by IS NULL AND resolution IS NULL) OR " +
                "(status = 'Closed' AND closed_at >= created_at AND closed_by IS NOT NULL AND resolution IS NOT NULL)"));
        builder.HasKey(request => request.Id);
        builder.Property(request => request.RequestedBy).HasMaxLength(PrivacyRequest.MaxActorLength);
        builder.Property(request => request.ClosedBy).HasMaxLength(PrivacyRequest.MaxActorLength);

        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(request => new { request.FamilyMemberId, request.HouseholdId })
            .HasPrincipalKey(member => new { member.Id, member.HouseholdId })
            .HasConstraintName("fk_privacy_requests_family_members")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => new { request.FamilyMemberId, request.HouseholdId }, "ix_privacy_requests_member_household")
            .HasDatabaseName("ix_privacy_requests_member_household");

        builder.HasIndex(request => new { request.FamilyMemberId, request.Type }, "ux_privacy_requests_one_open_per_type")
            .HasDatabaseName("ux_privacy_requests_one_open_per_type")
            .IsUnique()
            .HasFilter("status = 'Open'");
        builder.HasIndex(request => new { request.Status, request.CreatedAt }, "ix_privacy_requests_status_created")
            .HasDatabaseName("ix_privacy_requests_status_created");
    }
}
