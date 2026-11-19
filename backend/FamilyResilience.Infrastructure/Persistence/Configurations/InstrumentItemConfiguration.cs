using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class InstrumentItemConfiguration : IEntityTypeConfiguration<InstrumentItem>
{
    public void Configure(EntityTypeBuilder<InstrumentItem> builder)
    {
        builder.ToTable("instrument_items", table =>
            table.HasCheckConstraint("ck_instrument_items_display_order", "display_order > 0"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(Codes.MaxContentCodeLength).IsRequired();
        builder.Property(item => item.DisplayOrder).HasConversion<short>();
        builder.Property(item => item.ScoringCategory).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.HasLocalizedText(item => item.Prompt, "prompt");

        // Responses and score contributions point at (id, instrument_version_id).
        builder.HasAlternateKey(item => new { item.Id, item.InstrumentVersionId }).HasName("ak_instrument_items_id_version");

        builder.HasMany(item => item.Options)
            .WithOne()
            .HasForeignKey(option => option.InstrumentItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(item => new { item.InstrumentVersionId, item.Code }, "ux_instrument_items_version_code")
            .HasDatabaseName("ux_instrument_items_version_code")
            .IsUnique();
        builder.HasIndex(item => new { item.InstrumentSectionId, item.DisplayOrder }, "ux_instrument_items_section_order")
            .HasDatabaseName("ux_instrument_items_section_order")
            .IsUnique();
    }
}
