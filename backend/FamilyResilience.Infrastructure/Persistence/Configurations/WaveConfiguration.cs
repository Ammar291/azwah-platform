using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class WaveConfiguration : IEntityTypeConfiguration<Wave>
{
    public void Configure(EntityTypeBuilder<Wave> builder)
    {
        builder.ToTable("waves", table => table.HasCheckConstraint("ck_waves_window", "closes_at > opens_at"));
        builder.HasKey(wave => wave.Id);
        builder.Property(wave => wave.Code).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.HasIndex(wave => wave.Code, "ux_waves_code").HasDatabaseName("ux_waves_code").IsUnique();
    }
}
