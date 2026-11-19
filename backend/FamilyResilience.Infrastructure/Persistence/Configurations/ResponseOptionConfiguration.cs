using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ResponseOptionConfiguration : IEntityTypeConfiguration<ResponseOption>
{
    public void Configure(EntityTypeBuilder<ResponseOption> builder)
    {
        builder.ToTable("response_options", table =>
            table.HasCheckConstraint("ck_response_options_display_order", "display_order > 0"));
        builder.HasKey(option => option.Id);
        builder.Property(option => option.Code).HasMaxLength(Codes.MaxContentCodeLength).IsRequired();
        builder.Property(option => option.DisplayOrder).HasConversion<short>();
        builder.Property(option => option.Value).HasConversion<short>();
        builder.HasLocalizedText(option => option.Label, "label");

        // Responses point at (id, instrument_item_id, value), so a stored raw value always matches its option.
        builder.HasAlternateKey(option => new { option.Id, option.InstrumentItemId, option.Value })
            .HasName("ak_response_options_id_item_value");

        builder.HasIndex(option => new { option.InstrumentItemId, option.Code }, "ux_response_options_item_code")
            .HasDatabaseName("ux_response_options_item_code")
            .IsUnique();
        builder.HasIndex(option => new { option.InstrumentItemId, option.Value }, "ux_response_options_item_value")
            .HasDatabaseName("ux_response_options_item_value")
            .IsUnique();
        builder.HasIndex(option => new { option.InstrumentItemId, option.DisplayOrder }, "ux_response_options_item_order")
            .HasDatabaseName("ux_response_options_item_order")
            .IsUnique();
    }
}
