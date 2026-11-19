using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Privacy;

/// <summary>
/// A family's request about its data (D-89): access, correction or erasure, for one member the requester may act for.
/// It holds no free text; staff follow up through the programme's support channel. Open until a program administrator
/// closes it with a resolution; a closed request never changes.
/// </summary>
public sealed class PrivacyRequest : AggregateRoot<PrivacyRequestId>
{
    public const int MaxActorLength = 200;

    private PrivacyRequest(PrivacyRequestId id)
        : base(id)
    {
    }

    public HouseholdId HouseholdId { get; private set; }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public PrivacyRequestType Type { get; private set; }

    public PrivacyRequestStatus Status { get; private set; }

    /// <summary>The token subject that asked.</summary>
    public string RequestedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? ClosedBy { get; private set; }

    public PrivacyRequestResolution? Resolution { get; private set; }

    public static Result<PrivacyRequest> Create(
        HouseholdId householdId, FamilyMemberId memberId, PrivacyRequestType type, string? requestedBy, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<PrivacyRequest>(CommonErrors.TimeNotUtc);
        }

        if (!Enum.IsDefined(type))
        {
            return Result.Failure<PrivacyRequest>(PrivacyErrors.TypeInvalid);
        }

        if (string.IsNullOrWhiteSpace(requestedBy) || requestedBy.Length > MaxActorLength)
        {
            return Result.Failure<PrivacyRequest>(PrivacyErrors.ActorInvalid);
        }

        return Result.Success(new PrivacyRequest(PrivacyRequestId.New())
        {
            HouseholdId = householdId,
            FamilyMemberId = memberId,
            Type = type,
            Status = PrivacyRequestStatus.Open,
            RequestedBy = requestedBy,
            CreatedAt = now,
        });
    }

    public Result Close(PrivacyRequestResolution resolution, string? closedBy, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status == PrivacyRequestStatus.Closed)
        {
            return Result.Failure(PrivacyErrors.RequestAlreadyClosed);
        }

        if (!Enum.IsDefined(resolution))
        {
            return Result.Failure(PrivacyErrors.ResolutionInvalid);
        }

        if (string.IsNullOrWhiteSpace(closedBy) || closedBy.Length > MaxActorLength)
        {
            return Result.Failure(PrivacyErrors.ActorInvalid);
        }

        Status = PrivacyRequestStatus.Closed;
        Resolution = resolution;
        ClosedBy = closedBy;
        ClosedAt = now < CreatedAt ? CreatedAt : now;
        return Result.Success();
    }
}
