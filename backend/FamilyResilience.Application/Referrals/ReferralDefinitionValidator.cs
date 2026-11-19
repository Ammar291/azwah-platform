using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// A <c>referral.v1</c> definition is valid when it parses strictly and its target is the version or one of its
/// sections. Its tier codes are whatever the approved scoring rule set for that scope produces; categories are codes,
/// not claims of any partner service (open questions 13 and 33).
/// </summary>
public sealed class ReferralDefinitionValidator : IRuleDefinitionValidator
{
    public RuleSetKind Kind => RuleSetKind.Referral;

    public Result Validate(string definition, Instrument instrument, InstrumentVersion version)
    {
        var parsed = ReferralDefinitionParser.Parse(definition);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error!);
        }

        var target = RuleSetTarget.Resolve(instrument, version, parsed.Value.TargetSectionCode);
        return target.IsFailure ? Result.Failure(target.Error!) : Result.Success();
    }
}
