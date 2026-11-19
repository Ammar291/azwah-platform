namespace FamilyResilience.Application.Questionnaires;

/// <summary>A committed content file: the instrument and its first draft version.</summary>
public sealed record InstrumentContentPackage(CreateInstrumentRequest Instrument, InstrumentContentDocument Version);
