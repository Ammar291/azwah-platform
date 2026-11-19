namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Synthetic roster entries as the API reads them. Ages are exact on <see cref="TestData.Now"/>.</summary>
internal static class Rosters
{
    public static object Head(int age = 42, bool self = true) => Member(age, null, "employed", head: true, earner: true, self);

    public static object Spouse(int age = 39, bool self = false) => Member(age, "spouse", "employed", head: false, earner: false, self);

    public static object Parent(int age = 68) => Member(age, "parent", "retired", head: false, earner: false, self: false);

    public static object Child(int age) => Member(age, "child", null, head: false, earner: false, self: false);

    public static object Member(int age, string? relationship, string? employment, bool head, bool earner, bool self) =>
        new
        {
            birthYear = TestData.Today.Year - age,
            birthMonth = TestData.Today.Month,
            relationshipToHousehold = relationship,
            employmentStatus = employment,
            isHeadOfHousehold = head,
            isPrimaryIncomeEarner = earner,
            isSelf = self,
        };
}
