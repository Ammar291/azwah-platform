using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Synthetic data for persistence tests. Nothing here resembles a real participant or approved content.</summary>
internal static class TestData
{
    private static int _versionNumber = 1000;

    public static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public static void Ok(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Test data failed: {result.Error!.Code}");
        }
    }

    public static T Ok<T>(Result<T> result)
    {
        Ok((Result)result);
        return result.Value;
    }

    /// <summary>A snake code no other test uses, so tests can share one database.</summary>
    public static string UniqueCode(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    /// <summary>Version numbers no other test uses (the development seeder uses 1).</summary>
    public static int NextVersionNumber() => Interlocked.Increment(ref _versionNumber);

    public static YearMonth BornYearsAgo(int years) => Ok(YearMonth.Create(Today.Year - years, Today.Month, Today));

    public static FamilyMemberDetails Head(int age = 42) =>
        new(BornYearsAgo(age), null, EmploymentStatus.Employed, true, true);

    public static FamilyMemberDetails Spouse(int age = 39) =>
        new(BornYearsAgo(age), HouseholdRelationship.Spouse, EmploymentStatus.Employed, false, false);

    public static FamilyMemberDetails Child(int age) =>
        new(BornYearsAgo(age), HouseholdRelationship.Child, null, false, false);

    public static Approval TestApproval() => Ok(Approval.Create("approver-subject", Now, "Synthetic test approval"));

    public static Household NewHousehold(params FamilyMemberDetails[] roster) =>
        Ok(Household.Create(HouseholdReference.Generate(), roster.Length == 0 ? [Head()] : roster, Now));

    public static LocalizedText Text(string arabic, string english) => Ok(LocalizedText.Create(arabic, english, 2000));

    public static LocalizedText DraftText(string english) => Ok(LocalizedText.CreateDraft(null, english, 2000));

    public static Instrument NewInstrument(InstrumentKind kind = InstrumentKind.FamilyVoices) =>
        Ok(Instrument.Create(UniqueCode("test_instrument"), kind, Text("أداة", "Instrument"), Text("وصف", "Description"), Now));

    /// <summary>Sections A and B, each with two required single-choice items (A1, A2, B1, B2) and options valued 1–3.</summary>
    public static InstrumentVersion DraftVersion(Instrument instrument, int versionNumber = 1)
    {
        var version = Ok(InstrumentVersion.CreateDraft(instrument, versionNumber, Now));
        Ok(version.SetContentReferences("test-source (ar)", "test-source (en)", Now));
        foreach (var (sectionCode, sectionOrder) in new[] { ("A", 1), ("B", 2) })
        {
            var section = Ok(version.AddSection(sectionCode, Text("قسم", "Section"), sectionOrder, Now));
            for (var itemOrder = 1; itemOrder <= 2; itemOrder++)
            {
                var item = Ok(version.AddItem(
                    section.Id, $"{sectionCode}{itemOrder}", itemOrder, ItemType.SingleChoice, true, null,
                    ScoringDirection.Forward, Text("سؤال", "Question"), Now));
                for (var value = 1; value <= 3; value++)
                {
                    Ok(version.AddOption(item.Id, $"o{value}", value, value, Text("خيار", "Option"), Now));
                }
            }
        }

        return version;
    }

    /// <summary>A synthetic computation: answers count as their raw values, all in category "synthetic". Not a real rule.</summary>
    public static ScoringComputation SyntheticComputation(AssessmentSession session, string? tierCode = null)
    {
        var total = session.Responses.Sum(response => response.RawValue);
        return new ScoringComputation(
            "synthetic_policy",
            1,
            ScoreStatus.Complete,
            null,
            total,
            tierCode,
            session.Responses
                .Select(response => new ItemContribution(response.InstrumentItemId, response.RawValue, response.RawValue, "synthetic"))
                .ToList(),
            [new CategoryScore("synthetic", total, session.Responses.Count)]);
    }

    /// <summary>A synthetic incomplete computation: no total, tier or category, as an unanswered required item gives.</summary>
    public static ScoringComputation IncompleteComputation(AssessmentSession session) =>
        new(
            "synthetic_policy",
            1,
            ScoreStatus.Incomplete,
            "synthetic_incomplete",
            null,
            null,
            session.Responses
                .Select(response => new ItemContribution(response.InstrumentItemId, response.RawValue, response.RawValue, "synthetic"))
                .ToList(),
            []);
}
