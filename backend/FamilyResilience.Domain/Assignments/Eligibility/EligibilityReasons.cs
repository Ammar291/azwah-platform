namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>Internal reason codes recorded with every decision. Shown to staff only, never to other household members.</summary>
public static class EligibilityReasons
{
    public const string Eligible = "eligible";
    public const string MemberInactive = "member_inactive";
    public const string MemberUnder18 = "member_under_18";
    public const string MissingInformation = "missing_information";
    public const string AgeBelowMinimum = "age_below_minimum";
    public const string AgeAboveMaximum = "age_above_maximum";
    public const string EmploymentStatusNotQualifying = "employment_status_not_qualifying";
    public const string NotHeadOfHousehold = "not_head_of_household";
    public const string NotPrimaryIncomeEarner = "not_primary_income_earner";
    public const string NoHouseholdMemberUnderAge = "no_household_member_under_age";
    public const string NoneOfAlternativesMet = "none_of_alternatives_met";
}
