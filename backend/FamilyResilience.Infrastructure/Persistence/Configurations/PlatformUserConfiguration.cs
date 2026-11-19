using FamilyResilience.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Subject).HasMaxLength(PlatformUser.MaxSubjectLength).IsRequired();
        builder.HasIndex(user => user.Subject, "ux_platform_users_subject")
            .HasDatabaseName("ux_platform_users_subject")
            .IsUnique();
    }
}
