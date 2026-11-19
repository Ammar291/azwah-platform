using FamilyResilience.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("households");
        builder.HasKey(household => household.Id);
        builder.Property(household => household.Reference)
            .HasConversion(reference => reference.Value, value => HouseholdReference.Parse(value).Value)
            .HasMaxLength(HouseholdReference.Length)
            .IsRequired();
        builder.HasIndex(household => household.Reference, "ux_households_reference")
            .HasDatabaseName("ux_households_reference")
            .IsUnique();
        builder.HasMany(household => household.Members)
            .WithOne()
            .HasForeignKey(member => member.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(household => household.Members)
            .HasField("_members")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(household => household.Memberships)
            .WithOne()
            .HasForeignKey(membership => membership.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(household => household.Memberships)
            .HasField("_memberships")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
