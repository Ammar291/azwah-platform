using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class AssessmentAssignmentConfiguration : IEntityTypeConfiguration<AssessmentAssignment>
{
    public void Configure(EntityTypeBuilder<AssessmentAssignment> builder)
    {
        builder.ToTable("assessment_assignments", table =>
        {
            table.HasCheckConstraint(
                "ck_assessment_assignments_expiry", "expires_at IS NULL OR expires_at > assigned_at");
            table.HasCheckConstraint(
                "ck_assessment_assignments_completed", "status <> 'Completed' OR completed_at IS NOT NULL");
        });
        builder.HasKey(assignment => assignment.Id);

        // Sessions point at (id, family_member_id, instrument_version_id), so a session can never disagree with its
        // assignment about who answers which version.
        builder.HasAlternateKey(assignment => new { assignment.Id, assignment.FamilyMemberId, assignment.InstrumentVersionId })
            .HasName("ak_assessment_assignments_id_member_version");

        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(assignment => assignment.FamilyMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstrumentVersion>()
            .WithMany()
            .HasForeignKey(assignment => assignment.InstrumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstrumentSection>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.InstrumentSectionId, assignment.InstrumentVersionId })
            .HasPrincipalKey(section => new { section.Id, section.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuleSet>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.EligibilityRuleSetId, assignment.InstrumentVersionId })
            .HasPrincipalKey(ruleSet => new { ruleSet.Id, ruleSet.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Wave>()
            .WithMany()
            .HasForeignKey(assignment => assignment.WaveId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(
                assignment => new { assignment.FamilyMemberId, assignment.InstrumentVersionId, assignment.InstrumentSectionId, assignment.WaveId },
                "ux_assessment_assignments_live")
            .HasDatabaseName("ux_assessment_assignments_live")
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("status NOT IN ('Cancelled', 'Expired')");
        builder.HasIndex(assignment => new { assignment.WaveId, assignment.Status }, "ix_assessment_assignments_wave_status")
            .HasDatabaseName("ix_assessment_assignments_wave_status");

        // The live index starts with family_member_id but is partial, so it cannot serve every lookup by member (the
        // member foreign key's RESTRICT check included); EF Core would not add one on its own because of that prefix.
        builder.HasIndex(assignment => assignment.FamilyMemberId, "ix_assessment_assignments_family_member")
            .HasDatabaseName("ix_assessment_assignments_family_member");
    }
}
