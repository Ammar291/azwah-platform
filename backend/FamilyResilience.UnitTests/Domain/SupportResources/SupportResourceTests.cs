using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.SupportResources;

public sealed class SupportResourceTests
{
    private static SupportResourceContent Content(string? summaryAr = "ملخص تجريبي") =>
        Ok(SupportResourceContentTests.Build(summaryAr: summaryAr));

    private static SupportResource Draft(SupportResourceContent? content = null) =>
        Ok(SupportResource.CreateDraft("synthetic_service", 1, content ?? Content(), Now));

    private static SupportResource Approved()
    {
        var resource = Draft();
        Ok(resource.Approve(TestApproval(), Now.AddDays(180), Now));
        return resource;
    }

    private static void AssertError(Result result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error!.Code);
    }

    [Fact]
    public void A_draft_needs_a_snake_case_code()
    {
        AssertError(SupportResource.CreateDraft("Not Snake", 1, Content(), Now), "support_resources.code_invalid");
        var resource = Draft();
        Assert.Equal(("synthetic_service", SupportResourceStatus.Draft, 1), (resource.Code, resource.Status, resource.VersionNumber));
        Assert.Equal("Synthetic service", resource.Name.English);
        Assert.Equal(["financial_counselling"], resource.Categories);
        Assert.Equal((Now, Now), (resource.CreatedAt, resource.UpdatedAt));
    }

    [Fact]
    public void A_draft_starts_at_version_one_or_later() =>
        AssertError(SupportResource.CreateDraft("synthetic_service", 0, Content(), Now), "support_resources.version_number_invalid");

    [Fact]
    public void A_draft_can_be_updated_until_it_is_approved()
    {
        var resource = Draft();
        var later = Now.AddHours(1);
        var changed = Ok(SupportResourceContent.Create(
            "اسم جديد", "Synthetic renamed", "ملخص", "Synthetic summary", "جهة", "Synthetic provider",
            "https://example.org/new", ["family_mediation"], [], ["en"], SupportResourceVisibility.StaffOnly));

        Ok(resource.UpdateDraft(changed, later));

        Assert.Equal(
            ("Synthetic renamed", "https://example.org/new", SupportResourceVisibility.StaffOnly),
            (resource.Name.English, resource.Url, resource.Visibility));
        Assert.Equal(["family_mediation"], resource.Categories);
        Assert.Equal(["en"], resource.ServiceLanguages);
        Assert.Equal(later, resource.UpdatedAt);

        Ok(resource.Approve(TestApproval(), Now.AddDays(30), later));
        AssertError(resource.UpdateDraft(changed, later), "support_resources.not_draft");
    }

    [Fact]
    public void Approval_needs_arabic_in_every_text()
    {
        var resource = Draft(Content(summaryAr: null));

        AssertError(resource.Approve(TestApproval(), Now.AddDays(30), Now), "support_resources.arabic_text_missing");
        Assert.Equal(SupportResourceStatus.Draft, resource.Status);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(366 * 86400, true)]
    [InlineData(366 * 86400 + 1, false)]
    public void The_review_date_must_fall_within_366_days_of_approval(int secondsAfterApproval, bool accepted)
    {
        var resource = Draft();
        var approval = TestApproval();

        var result = resource.Approve(approval, approval.ApprovedAt.AddSeconds(secondsAfterApproval), Now);

        Assert.Equal(accepted, result.IsSuccess);
        if (!accepted)
        {
            AssertError(result, "support_resources.review_due_invalid");
            Assert.Equal(SupportResourceStatus.Draft, resource.Status);
        }
    }

    [Fact]
    public void A_review_date_that_is_not_utc_is_refused() =>
        AssertError(
            Draft().Approve(TestApproval(), new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.FromHours(3)), Now),
            "support_resources.review_due_invalid");

    [Fact]
    public void Approval_records_the_approver_time_and_source()
    {
        var resource = Draft();
        var approval = TestApproval();
        var due = Now.AddDays(180);
        var at = Now.AddMinutes(5);

        Ok(resource.Approve(approval, due, at));

        Assert.Equal(SupportResourceStatus.Approved, resource.Status);
        Assert.Equal(
            (approval.ApprovedBy, approval.ApprovedAt, approval.Source),
            (resource.ApprovedBy, resource.ApprovedAt, resource.ApprovalSource));
        Assert.Equal((due, at), (resource.ReviewDueAt, resource.UpdatedAt));
    }

    [Fact]
    public void Only_a_draft_can_be_approved() =>
        AssertError(Approved().Approve(TestApproval(), Now.AddDays(30), Now), "support_resources.not_draft");

    [Fact]
    public void Only_an_approved_resource_can_be_retired()
    {
        AssertError(Draft().Retire(Now), "support_resources.not_approved");

        var resource = Approved();
        var at = Now.AddDays(1);
        Ok(resource.Retire(at));
        Assert.Equal((SupportResourceStatus.Retired, at), (resource.Status, resource.RetiredAt));
        AssertError(resource.Retire(at), "support_resources.not_approved");
    }

    [Fact]
    public void Times_must_be_utc()
    {
        var local = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(3));

        AssertError(SupportResource.CreateDraft("synthetic_service", 1, Content(), local), "common.time_not_utc");
        AssertError(Draft().UpdateDraft(Content(), local), "common.time_not_utc");
        AssertError(Draft().Approve(TestApproval(), Now.AddDays(30), local), "common.time_not_utc");
        AssertError(Approved().Retire(local), "common.time_not_utc");
    }

    [Fact]
    public void A_resource_is_discoverable_only_while_approved_and_before_its_review_date()
    {
        Assert.False(Draft().IsDiscoverableAt(Now));

        var resource = Approved();
        Assert.True(resource.IsDiscoverableAt(resource.ReviewDueAt!.Value.AddSeconds(-1)));
        Assert.False(resource.IsDiscoverableAt(resource.ReviewDueAt!.Value));

        Ok(resource.Retire(Now.AddDays(1)));
        Assert.False(resource.IsDiscoverableAt(Now));
    }
}
