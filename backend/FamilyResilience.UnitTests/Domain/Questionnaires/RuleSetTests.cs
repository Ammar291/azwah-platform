using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Questionnaires;

public sealed class RuleSetTests
{
    [Fact]
    public void Proposed_rule_set_keeps_its_definition_and_version()
    {
        var version = DraftVersion();

        var rules = Ok(RuleSet.Propose(RuleSetKind.Scoring, "sum_reverse_scored", 3, version, """{"a": 1}""", Now));

        Assert.Equal(RuleSetStatus.Proposed, rules.Status);
        Assert.Equal(version.Id, rules.InstrumentVersionId);
        Assert.Equal(3, rules.VersionNumber);
        Assert.Equal("""{"a": 1}""", rules.Definition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[1, 2]")]
    [InlineData("not json")]
    [InlineData("\"text\"")]
    public void Definition_must_be_a_json_object(string definition) =>
        Assert.Equal(
            QuestionnaireErrors.RuleSetDefinitionInvalid,
            RuleSet.Propose(RuleSetKind.Scoring, "rules", 1, DraftVersion(), definition, Now).Error);

    [Fact]
    public void Code_must_be_a_snake_code() =>
        Assert.Equal(
            QuestionnaireErrors.RuleSetCodeInvalid,
            RuleSet.Propose(RuleSetKind.Scoring, "Rules-1", 1, DraftVersion(), "{}", Now).Error);

    [Fact]
    public void Rule_set_can_be_approved_only_for_its_own_approved_version()
    {
        var draft = DraftVersion();
        var draftRules = Ok(RuleSet.Propose(RuleSetKind.Scoring, "rules", 1, draft, "{}", Now));
        var approved = ApprovedVersion();
        var approvedRules = Ok(RuleSet.Propose(RuleSetKind.Scoring, "rules", 1, approved, "{}", Now));

        Assert.Equal(QuestionnaireErrors.VersionNotApproved, draftRules.Approve(TestApproval(), draft, Now).Error);
        Assert.Equal(QuestionnaireErrors.RuleSetVersionMismatch, approvedRules.Approve(TestApproval(), draft, Now).Error);
        Assert.True(approvedRules.Approve(TestApproval(), approved, Now).IsSuccess);
        Assert.Equal(RuleSetStatus.Approved, approvedRules.Status);
        Assert.Equal("Synthetic test approval", approvedRules.ApprovalSource);
    }

    [Fact]
    public void Approved_rule_set_cannot_be_approved_again_and_can_be_retired()
    {
        var version = ApprovedVersion();
        var rules = ApprovedRuleSet(RuleSetKind.Eligibility, version);

        Assert.Equal(QuestionnaireErrors.RuleSetNotProposed, rules.Approve(TestApproval(), version, Now).Error);
        Assert.True(rules.Retire(Now).IsSuccess);
        Assert.Equal(RuleSetStatus.Retired, rules.Status);
        Assert.Equal(QuestionnaireErrors.RuleSetNotApproved, rules.Retire(Now).Error);
    }
}
