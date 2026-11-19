using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

/// <summary>
/// Safety alerts hold fixed codes only. The subject, source, concern, severity and first detection never change after
/// insert, and rows are deleted only by household erasure (<c>trg_safety_alerts_guarded</c>, AddSafetyAlertGuards).
/// </summary>
public sealed class SafetyAlertConfiguration : IEntityTypeConfiguration<SafetyAlert>
{
    public void Configure(EntityTypeBuilder<SafetyAlert> builder)
    {
        builder.ToTable("safety_alerts", table =>
        {
            table.HasCheckConstraint("ck_safety_alerts_subject", "platform_user_id IS NOT NULL OR household_id IS NOT NULL");
            table.HasCheckConstraint("ck_safety_alerts_member_household", "family_member_id IS NULL OR household_id IS NOT NULL");
            table.HasCheckConstraint("ck_safety_alerts_severity_of_concern", "(concern = 'LowResilienceScore') = (severity = 'High')");
            table.HasCheckConstraint("ck_safety_alerts_language", "language IS NULL OR language IN ('en', 'ar')");
            table.HasCheckConstraint(
                "ck_safety_alerts_occurrences", "occurrence_count >= 1 AND last_detected_at >= first_detected_at");
            table.HasCheckConstraint(
                "ck_safety_alerts_status_shape",
                "(status = 'Open' AND acknowledged_at IS NULL AND acknowledged_by IS NULL"
                + " AND resolved_at IS NULL AND resolved_by IS NULL AND resolution IS NULL)"
                + " OR (status = 'Acknowledged' AND acknowledged_at >= first_detected_at AND acknowledged_by IS NOT NULL"
                + " AND resolved_at IS NULL AND resolved_by IS NULL AND resolution IS NULL)"
                + " OR (status = 'Resolved' AND resolved_at >= first_detected_at AND resolved_by IS NOT NULL AND resolution IS NOT NULL"
                + " AND (acknowledged_at IS NULL) = (acknowledged_by IS NULL))");
        });
        builder.HasKey(alert => alert.Id);
        builder.Property(alert => alert.UserId).HasColumnName("platform_user_id");
        builder.Property(alert => alert.MemberId).HasColumnName("family_member_id");
        builder.Property(alert => alert.SignalCode).HasMaxLength(SafetyAlert.MaxSignalCodeLength);
        builder.Property(alert => alert.Language).HasMaxLength(2);
        builder.Property(alert => alert.AcknowledgedBy).HasMaxLength(SafetyAlert.MaxActorLength);
        builder.Property(alert => alert.ResolvedBy).HasMaxLength(SafetyAlert.MaxActorLength);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(alert => alert.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(alert => alert.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        // The member, when known, must belong to the alert's household.
        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(alert => new { alert.MemberId, alert.HouseholdId })
            .HasPrincipalKey(member => new { member.Id, member.HouseholdId })
            .HasConstraintName("fk_safety_alerts_family_members")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(alert => alert.AssessmentSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // One unresolved alert per user, concern and source: repeats recur on it. A score alert is unique per session.
        builder.HasIndex(alert => new { alert.UserId, alert.Concern, alert.Source }, "ux_safety_alerts_unresolved_subject")
            .HasDatabaseName("ux_safety_alerts_unresolved_subject")
            .IsUnique()
            .HasFilter("status <> 'Resolved' AND platform_user_id IS NOT NULL");
        builder.HasIndex(alert => alert.AssessmentSessionId, "ux_safety_alerts_session")
            .HasDatabaseName("ux_safety_alerts_session")
            .IsUnique()
            .HasFilter("assessment_session_id IS NOT NULL");
        builder.HasIndex(alert => new { alert.Status, alert.Severity, alert.LastDetectedAt }, "ix_safety_alerts_queue")
            .HasDatabaseName("ix_safety_alerts_queue");

        // Foreign-key indexes: the partial unique index above skips resolved alerts, so the user key gets its own.
        builder.HasIndex(alert => alert.UserId, "ix_safety_alerts_platform_user")
            .HasDatabaseName("ix_safety_alerts_platform_user");
        builder.HasIndex(alert => alert.HouseholdId, "ix_safety_alerts_household")
            .HasDatabaseName("ix_safety_alerts_household");
        builder.HasIndex(alert => new { alert.MemberId, alert.HouseholdId }, "ix_safety_alerts_member_household")
            .HasDatabaseName("ix_safety_alerts_member_household");
    }
}
