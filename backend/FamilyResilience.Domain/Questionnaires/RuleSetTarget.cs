using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>
/// What a rule set applies to. Family-voices rule sets target exactly one module (a section); household-assessment
/// rule sets target the whole version. The two instruments are never combined.
/// </summary>
public static class RuleSetTarget
{
    public static Result<InstrumentSection?> Resolve(Instrument instrument, InstrumentVersion version, string? sectionCode)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(version);

        if (version.InstrumentId != instrument.Id)
        {
            return Result.Failure<InstrumentSection?>(QuestionnaireErrors.InstrumentMismatch);
        }

        var wholeVersion = sectionCode is null;
        if (wholeVersion != (instrument.Kind == InstrumentKind.HouseholdAssessment))
        {
            return Result.Failure<InstrumentSection?>(QuestionnaireErrors.RuleSetTargetInvalid);
        }

        if (wholeVersion)
        {
            return Result.Success<InstrumentSection?>(null);
        }

        var section = version.Sections.FirstOrDefault(candidate => candidate.Code == sectionCode);
        return section is null
            ? Result.Failure<InstrumentSection?>(QuestionnaireErrors.SectionNotFound)
            : Result.Success<InstrumentSection?>(section);
    }
}
