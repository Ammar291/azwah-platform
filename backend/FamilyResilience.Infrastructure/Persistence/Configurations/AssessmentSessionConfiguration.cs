using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class AssessmentSessionConfiguration : IEntityTypeConfiguration<AssessmentSession>
{
    public void Configure(EntityTypeBuilder<AssessmentSession> builder)
    {
        builder.ToTable("assessment_sessions", table =>
        {
            table.HasCheckConstraint(
                "ck_assessment_sessions_submitted_after_started", "submitted_at IS NULL OR submitted_at >= started_at");
            table.HasCheckConstraint(
                "ck_assessment_sessions_scored_after_submitted", "scored_at IS NULL OR scored_at >= submitted_at");
            table.HasCheckConstraint(
                "ck_assessment_sessions_status_shape",
                "(status = 'InProgress' AND submitted_at IS NULL AND scored_at IS NULL AND abandoned_at IS NULL)"
                + " OR (status = 'Submitted' AND submitted_at IS NOT NULL AND scored_at IS NULL AND abandoned_at IS NULL)"
                + " OR (status = 'Scored' AND submitted_at IS NOT NULL AND scored_at IS NOT NULL AND abandoned_at IS NULL)"
                + " OR (status = 'Abandoned' AND abandoned_at IS NOT NULL AND submitted_at IS NULL)");
        });
        builder.HasKey(session => session.Id);

        // Responses and score results point at (id, instrument_version_id); referrals at (id, family_member_id).
        builder.HasAlternateKey(session => new { session.Id, session.InstrumentVersionId }).HasName("ak_assessment_sessions_id_version");
        builder.HasAlternateKey(session => new { session.Id, session.FamilyMemberId }).HasName("ak_assessment_sessions_id_member");

        builder.HasOne<AssessmentAssignment>()
            .WithMany()
            .HasForeignKey(session => new { session.AssessmentAssignmentId, session.FamilyMemberId, session.InstrumentVersionId })
            .HasPrincipalKey(assignment => new { assignment.Id, assignment.FamilyMemberId, assignment.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstrumentSection>()
            .WithMany()
            .HasForeignKey(session => new { session.InstrumentSectionId, session.InstrumentVersionId })
            .HasPrincipalKey(section => new { section.Id, section.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(session => session.Responses)
            .WithOne()
            .HasForeignKey(response => new { response.AssessmentSessionId, response.InstrumentVersionId })
            .HasPrincipalKey(session => new { session.Id, session.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(session => session.Responses).HasField("_responses").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(session => session.AssessmentAssignmentId, "ux_assessment_sessions_live")
            .HasDatabaseName("ux_assessment_sessions_live")
            .IsUnique()
            .HasFilter("status <> 'Abandoned'");
        builder.HasIndex(session => session.FamilyMemberId, "ix_assessment_sessions_family_member")
            .HasDatabaseName("ix_assessment_sessions_family_member");
    }
}
