using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Everything a referral needs, saved: a scored session (as last tracked, so its status is Scored), its version and
/// member, an approved referral rule set of that version and the member's active referral consent.
/// </summary>
internal sealed record ReferralScenario(
    AssessmentSession Session, InstrumentVersion Version, FamilyMember Member, RuleSet ReferralRules, ConsentRecord Consent);
