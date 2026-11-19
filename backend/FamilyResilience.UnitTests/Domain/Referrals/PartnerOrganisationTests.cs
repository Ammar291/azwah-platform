using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Referrals;

public sealed class PartnerOrganisationTests
{
    private const string Coordinator = "coordinator-subject";

    private static PartnerOrganisation Partner(params string[] categories) =>
        Ok(PartnerOrganisation.Register(
            "synthetic_partner", Text("شريك", "Synthetic partner"), categories.Length == 0 ? ["financial_counselling"] : categories, Now));

    private static SupportReferral OpenReferral(string category = "financial_counselling")
    {
        var household = NewHousehold();
        var member = household.Members[0];
        var version = ApprovedVersion();
        var assignment = Assign(member, version);
        var session = Ok(AssessmentSession.Start(assignment, Now));
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Ok(session.Submit(version, assignment, Now));
        Ok(FamilyResilience.Domain.Scoring.ScoreResult.Record(
            session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session), Now));
        return Ok(SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(member), category, Now));
    }

    [Fact]
    public void A_partner_is_registered_active_with_the_categories_it_covers()
    {
        var partner = Partner("financial_counselling", "family_mediation");

        Assert.Equal(("synthetic_partner", "Synthetic partner", "شريك", true), (partner.Code, partner.Name.English, partner.Name.Arabic, partner.IsActive));
        Assert.True(partner.Covers("family_mediation"));
        Assert.False(partner.Covers("housing"));
    }

    public static TheoryData<string, string[], Error> InvalidPartners => new()
    {
        { "Not A Code", ["financial_counselling"], ReferralErrors.PartnerCodeInvalid },
        { "synthetic_partner", [], ReferralErrors.PartnerCategoriesInvalid },
        { "synthetic_partner", ["financial_counselling", "financial_counselling"], ReferralErrors.PartnerCategoriesInvalid },
        { "synthetic_partner", ["Financial"], ReferralErrors.PartnerCategoriesInvalid },
        { "synthetic_partner", [.. Enumerable.Range(0, PartnerOrganisation.MaxCategories + 1).Select(index => $"category_{index}")], ReferralErrors.PartnerCategoriesInvalid },
    };

    [Theory]
    [MemberData(nameof(InvalidPartners))]
    public void Registration_refuses_a_bad_code_or_category_list(string code, string[] categories, Error expected) =>
        Assert.Equal(expected, PartnerOrganisation.Register(code, Text("شريك", "Partner"), categories, Now).Error);

    [Fact]
    public void Registration_needs_the_name_in_both_languages() =>
        Assert.Equal(
            CommonErrors.TextRequired,
            PartnerOrganisation.Register("synthetic_partner", DraftText("Partner"), ["financial_counselling"], Now).Error);

    [Fact]
    public void A_partner_can_be_deactivated_and_activated_again()
    {
        var partner = Partner();
        var later = Now.AddDays(1);

        Ok(partner.SetActive(false, later));
        Assert.Equal((false, later), (partner.IsActive, partner.UpdatedAt));

        Ok(partner.SetActive(true, later));
        Assert.True(partner.IsActive);
    }

    [Fact]
    public void A_manual_hand_off_moves_an_open_referral_to_the_partner()
    {
        var referral = OpenReferral();
        var partner = Partner();
        referral.ClearDomainEvents();

        Ok(referral.HandOff(partner, Coordinator, Now));

        Assert.Equal(
            (ReferralStatus.InProgress, (PartnerOrganisationId?)partner.Id, (HandOffMethod?)HandOffMethod.Manual, (DateTimeOffset?)Now, Coordinator, "synthetic_partner"),
            (referral.Status, referral.PartnerOrganisationId, referral.HandOffMethod, referral.HandedOffAt, referral.HandedOffBy, referral.AssignedServiceCode));
        Assert.Equal(ReferralStatus.InProgress, Assert.IsType<SupportReferralStatusChanged>(Assert.Single(referral.GetDomainEvents())).Status);
    }

    [Fact]
    public void A_hand_off_needs_an_open_referral_and_an_active_partner_covering_its_category()
    {
        var inactive = Partner();
        Ok(inactive.SetActive(false, Now));
        var handedOff = OpenReferral();
        Ok(handedOff.HandOff(Partner(), Coordinator, Now));

        Assert.Equal(ReferralErrors.PartnerInactive, OpenReferral().HandOff(inactive, Coordinator, Now).Error);
        Assert.Equal(ReferralErrors.PartnerCategoryMismatch, OpenReferral("housing").HandOff(Partner(), Coordinator, Now).Error);
        Assert.Equal(ReferralErrors.NotOpen, handedOff.HandOff(Partner(), Coordinator, Now).Error);
        Assert.Equal(ReferralErrors.ActorIdTooLong, OpenReferral().HandOff(Partner(), new string('c', SupportReferral.MaxActorIdLength + 1), Now).Error);
    }

    [Fact]
    public void A_cancelled_referral_cannot_be_handed_off()
    {
        var referral = OpenReferral();
        Ok(referral.Cancel("consent_withdrawn", null, Now));

        Assert.Equal(ReferralErrors.NotOpen, referral.HandOff(Partner(), Coordinator, Now).Error);
        Assert.Null(referral.PartnerOrganisationId);
    }
}
