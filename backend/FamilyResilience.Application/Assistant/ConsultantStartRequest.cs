namespace FamilyResilience.Application.Assistant;

/// <summary>Query string <c>?language=en</c> or <c>?language=ar</c> of the live consultant's WebSocket request.</summary>
public sealed class ConsultantStartRequest
{
    public string? Language { get; init; }
}
