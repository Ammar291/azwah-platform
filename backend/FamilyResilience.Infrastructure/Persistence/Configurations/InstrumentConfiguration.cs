using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("instruments", table =>
            table.HasCheckConstraint("ck_instruments_code", "code ~ '^[a-z][a-z0-9_]{0,63}$'"));
        builder.HasKey(instrument => instrument.Id);
        builder.Property(instrument => instrument.Code).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.HasLocalizedText(instrument => instrument.Name, "name");
        builder.HasLocalizedText(instrument => instrument.Description, "description");
        builder.HasIndex(instrument => instrument.Code, "ux_instruments_code").HasDatabaseName("ux_instruments_code").IsUnique();
    }
}
