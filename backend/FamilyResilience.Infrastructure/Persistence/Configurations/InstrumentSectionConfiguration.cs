using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class InstrumentSectionConfiguration : IEntityTypeConfiguration<InstrumentSection>
{
    public void Configure(EntityTypeBuilder<InstrumentSection> builder)
    {
        builder.ToTable("instrument_sections", table =>
            table.HasCheckConstraint("ck_instrument_sections_display_order", "display_order > 0"));
        builder.HasKey(section => section.Id);
        builder.Property(section => section.Code).HasMaxLength(Codes.MaxContentCodeLength).IsRequired();
        builder.Property(section => section.DisplayOrder).HasConversion<short>();
        builder.HasLocalizedText(section => section.Title, "title");

        // Items, assignments and sessions point at (id, instrument_version_id), so they cannot name a section of
        // another version.
        builder.HasAlternateKey(section => new { section.Id, section.InstrumentVersionId }).HasName("ak_instrument_sections_id_version");

        builder.HasMany(section => section.Items)
            .WithOne()
            .HasForeignKey(item => new { item.InstrumentSectionId, item.InstrumentVersionId })
            .HasPrincipalKey(section => new { section.Id, section.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(section => section.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(section => new { section.InstrumentVersionId, section.Code }, "ux_instrument_sections_version_code")
            .HasDatabaseName("ux_instrument_sections_version_code")
            .IsUnique();
        builder.HasIndex(section => new { section.InstrumentVersionId, section.DisplayOrder }, "ux_instrument_sections_version_order")
            .HasDatabaseName("ux_instrument_sections_version_order")
            .IsUnique();
    }
}
