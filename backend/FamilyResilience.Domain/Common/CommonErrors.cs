namespace FamilyResilience.Domain.Common;

public static class CommonErrors
{
    public static readonly Error TimeNotUtc =
        new("common.time_not_utc", "Times must be in UTC.");

    public static readonly Error TextRequired =
        new("common.text_required", "Both the Arabic and the English text are required.");

    public static readonly Error TextTooLong =
        new("common.text_too_long", "The text is longer than allowed.");

    public static readonly Error ArabicTextNotArabic =
        new("common.arabic_text_not_arabic", "The Arabic text must contain Arabic script.");

    public static readonly Error MonthOutOfRange =
        new("common.month_out_of_range", "The month must be between 1 and 12.");

    public static readonly Error YearOutOfRange =
        new("common.year_out_of_range", "The year must be 1900 or later.");

    public static readonly Error YearMonthInFuture =
        new("common.year_month_in_future", "The month must not be in the future.");

    public static readonly Error ApproverInvalid =
        new("common.approver_invalid", "The approver is required and must be at most 200 characters.");

    public static readonly Error ApprovalSourceInvalid =
        new("common.approval_source_invalid", "The approval source is required and must be at most 300 characters.");
}
