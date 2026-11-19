namespace FamilyResilience.Application.Assistant;

/// <summary>What the consultant does once a model session is open (D-124, D-141).</summary>
public enum ConsultantOpening
{
    /// <summary>Greets the person in one short sentence (back, when there is earlier conversation or a note).</summary>
    Greet,

    /// <summary>Says nothing and listens: a session that carries on a call after its first one was stopped.</summary>
    Listen,
}
