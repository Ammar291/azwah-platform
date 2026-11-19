using FamilyResilience.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class RoleSnapshotConfiguration : IEntityTypeConfiguration<RoleSnapshot>
{
    public void Configure(EntityTypeBuilder<RoleSnapshot> builder)
    {
        builder.ToTable("role_snapshots", table =>
            table.HasCheckConstraint("ck_role_snapshots_role_count", $"cardinality(roles) <= {RoleSnapshot.MaxRoles}"));
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Subject).HasMaxLength(RoleSnapshot.MaxSubjectLength).IsRequired();
        builder.Property(snapshot => snapshot.Roles).HasColumnType("text[]").IsRequired();
        builder.HasIndex(snapshot => snapshot.Subject, "ux_role_snapshots_subject")
            .HasDatabaseName("ux_role_snapshots_subject")
            .IsUnique();
    }
}
