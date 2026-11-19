using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class RuleSetConfiguration : IEntityTypeConfiguration<RuleSet>
{
    public void Configure(EntityTypeBuilder<RuleSet> builder)
    {
        builder.ToTable("rule_sets", table =>
        {
            table.HasCheckConstraint("ck_rule_sets_version_number", "version_number > 0");
            table.HasCheckConstraint("ck_rule_sets_definition_is_object", "jsonb_typeof(definition) = 'object'");
            table.HasCheckConstraint(
                "ck_rule_sets_approval",
                "status = 'Proposed' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL)");
            table.HasCheckConstraint("ck_rule_sets_retired", "status <> 'Retired' OR retired_at IS NOT NULL");
        });
        builder.HasKey(ruleSet => ruleSet.Id);
        builder.Property(ruleSet => ruleSet.Code).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.Property(ruleSet => ruleSet.Definition).HasColumnType("jsonb").IsRequired();
        builder.Property(ruleSet => ruleSet.ApprovedBy).HasMaxLength(Approval.MaxApprovedByLength);
        builder.Property(ruleSet => ruleSet.ApprovalSource).HasMaxLength(Approval.MaxSourceLength);

        // Assignments and score results point at (id, instrument_version_id), so they cannot use another version's rules.
        builder.HasAlternateKey(ruleSet => new { ruleSet.Id, ruleSet.InstrumentVersionId }).HasName("ak_rule_sets_id_version");

        builder.HasOne<InstrumentVersion>()
            .WithMany()
            .HasForeignKey(ruleSet => ruleSet.InstrumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ruleSet => new { ruleSet.Kind, ruleSet.Code, ruleSet.VersionNumber }, "ux_rule_sets_kind_code_version")
            .HasDatabaseName("ux_rule_sets_kind_code_version")
            .IsUnique();
    }
}
