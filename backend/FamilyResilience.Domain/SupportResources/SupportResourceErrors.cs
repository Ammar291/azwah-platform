using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.SupportResources;

public static class SupportResourceErrors
{
    public static readonly Error CodeInvalid =
        new("support_resources.code_invalid", "The resource code must be lowercase snake case.");

    public static readonly Error VersionNumberInvalid =
        new("support_resources.version_number_invalid", "The resource version number must be 1 or more.");

    public static readonly Error UrlInvalid =
        new("support_resources.url_invalid", "The address must be an https link of at most 500 characters.");

    public static readonly Error CategoriesInvalid =
        new("support_resources.categories_invalid", "Give 1 to 10 distinct category codes in snake case.");

    public static readonly Error AudiencesInvalid =
        new("support_resources.audiences_invalid", "Give at most 10 distinct audience codes in snake case.");

    public static readonly Error ServiceLanguagesInvalid =
        new("support_resources.service_languages_invalid", "Give 1 to 5 distinct two-letter language codes.");

    public static readonly Error VisibilityInvalid =
        new("support_resources.visibility_invalid", "The visibility is not recognised.");

    public static readonly Error ReviewDueInvalid =
        new("support_resources.review_due_invalid", "The review date must be in UTC, after the approval and within 366 days of it.");

    public static readonly Error NotFound =
        new("support_resources.not_found", "The support resource was not found.");

    public static readonly Error NotDraft =
        new("support_resources.not_draft", "Only a draft support resource can be changed or approved.");

    public static readonly Error NotApproved =
        new("support_resources.not_approved", "The support resource is not approved.");

    public static readonly Error ArabicTextMissing =
        new("support_resources.arabic_text_missing", "Every text needs its Arabic version before approval.");

    public static readonly Error VersionNumberTaken =
        new("support_resources.version_number_taken", "This resource already has a version with that number.");

    public static readonly Error ApprovedVersionExists =
        new("support_resources.approved_version_exists", "This resource already has an approved version; retire it first.");
}
