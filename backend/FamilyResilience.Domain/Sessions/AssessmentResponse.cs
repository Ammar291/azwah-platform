using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Sessions;

/// <summary>
/// One answer: the chosen option and its raw value. There is no free-text answer. Changes only while the session is
/// in progress (enforced again by a database trigger).
/// </summary>
public sealed class AssessmentResponse : Entity<AssessmentResponseId>
{
    private AssessmentResponse(AssessmentResponseId id)
        : base(id)
    {
    }

    public AssessmentSessionId AssessmentSessionId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public InstrumentItemId InstrumentItemId { get; private set; }

    public ResponseOptionId ResponseOptionId { get; private set; }

    public int RawValue { get; private set; }

    public DateTimeOffset AnsweredAt { get; private set; }

    public DateTimeOffset LastModifiedAt { get; private set; }

    internal static AssessmentResponse Create(
        AssessmentSessionId sessionId, InstrumentVersionId versionId, InstrumentItemId itemId, ResponseOption option, DateTimeOffset now) =>
        new(AssessmentResponseId.New())
        {
            AssessmentSessionId = sessionId,
            InstrumentVersionId = versionId,
            InstrumentItemId = itemId,
            ResponseOptionId = option.Id,
            RawValue = option.Value,
            AnsweredAt = now,
            LastModifiedAt = now,
        };

    internal void Change(ResponseOption option, DateTimeOffset now)
    {
        ResponseOptionId = option.Id;
        RawValue = option.Value;
        LastModifiedAt = now;
    }
}
