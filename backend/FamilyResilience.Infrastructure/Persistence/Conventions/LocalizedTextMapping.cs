using System.Linq.Expressions;
using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Conventions;

internal static class LocalizedTextMapping
{
    /// <summary>
    /// Maps bilingual text to <c>{prefix}_ar</c> and <c>{prefix}_en</c> <c>text</c> columns. Arabic is nullable because
    /// draft content may lack it; a trigger refuses to approve a version while any of its text is missing Arabic.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasLocalizedText<TEntity>(
        this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, LocalizedText?>> property, string columnPrefix)
        where TEntity : class
    {
        builder.ComplexProperty(property, text =>
        {
            text.Property(value => value.Arabic).HasColumnName($"{columnPrefix}_ar").HasColumnType("text").IsRequired(false);
            text.Property(value => value.English).HasColumnName($"{columnPrefix}_en").HasColumnType("text").IsRequired();
        });
        return builder;
    }
}
