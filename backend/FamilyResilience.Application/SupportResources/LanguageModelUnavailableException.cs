namespace FamilyResilience.Application.SupportResources;

/// <summary>A language-model provider could not answer. <see cref="Transient"/> says whether trying again may help.</summary>
public sealed class LanguageModelUnavailableException : Exception
{
    public LanguageModelUnavailableException()
    {
    }

    public LanguageModelUnavailableException(string message)
        : base(message)
    {
    }

    public LanguageModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LanguageModelUnavailableException(bool transient)
    {
        Transient = transient;
    }

    public bool Transient { get; }
}
