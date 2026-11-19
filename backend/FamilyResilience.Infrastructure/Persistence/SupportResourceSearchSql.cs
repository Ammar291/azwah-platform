namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>
/// The SQL that builds the search document of a support resource, written once so the model configuration, the
/// migration and the search query agree. Public because the integration tests compare it with the C# query folding.
/// </summary>
public static class SupportResourceSearchSql
{
    /// <summary>
    /// Folds alef, yeh and teh-marbuta variants, removes diacritics and tatweel, then strips one leading article
    /// (<c>وال فال بال كال لل ال</c>) from words that keep at least two letters. Immutable, so it can feed a stored
    /// generated column.
    /// </summary>
    public static string NormaliseArabic(string sqlExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlExpression);
        return "regexp_replace("
            + $"regexp_replace(translate(coalesce({sqlExpression}, ''), 'أإآٱىة', 'اااايه'), '[\\u064B-\\u065F\\u0670\\u0640]', '', 'g'),"
            + @" '(^|[^ء-ي])(وال|فال|بال|كال|لل|ال)(?=[ء-ي]{2,})', '\1', 'g')";
    }

    public static readonly string VectorExpression =
        "setweight(to_tsvector('english'::regconfig, coalesce(name_en,'')),'A') || "
        + "setweight(to_tsvector('english'::regconfig, coalesce(summary_en,'')),'B') || "
        + "setweight(to_tsvector('english'::regconfig, coalesce(provider_name_en,'')),'C') || "
        + $"setweight(to_tsvector('simple'::regconfig, {NormaliseArabic("name_ar")}),'A') || "
        + $"setweight(to_tsvector('simple'::regconfig, {NormaliseArabic("summary_ar")}),'B') || "
        + $"setweight(to_tsvector('simple'::regconfig, {NormaliseArabic("provider_name_ar")}),'C')";
}
