using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class AssessmentResponseConfiguration : IEntityTypeConfiguration<AssessmentResponse>
{
    public void Configure(EntityTypeBuilder<AssessmentResponse> builder)
    {
        builder.ToTable("assessment_responses", table =>
            table.HasCheckConstraint(
                "ck_assessment_responses_modified_after_answered", "last_modified_at >= answered_at"));
        builder.HasKey(response => response.Id);
        builder.Property(response => response.RawValue).HasConversion<short>();

        // The item must belong to the session's version, and the option and raw value must be that item's.
        builder.HasOne<InstrumentItem>()
            .WithMany()
            .HasForeignKey(response => new { response.InstrumentItemId, response.InstrumentVersionId })
            .HasPrincipalKey(item => new { item.Id, item.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResponseOption>()
            .WithMany()
            .HasForeignKey(response => new { response.ResponseOptionId, response.InstrumentItemId, response.RawValue })
            .HasPrincipalKey(option => new { option.Id, option.InstrumentItemId, option.Value })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(response => new { response.AssessmentSessionId, response.InstrumentItemId }, "ux_assessment_responses_session_item")
            .HasDatabaseName("ux_assessment_responses_session_item")
            .IsUnique();
    }
}
