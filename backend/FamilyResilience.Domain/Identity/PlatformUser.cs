using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Identity;

/// <summary>
/// A person known to the platform by the subject of their validated token. Created when a participant enrols; holds no
/// name, contact detail or identity-provider data. Roles stay in the token; what the user may reach comes from their
/// household membership.
/// </summary>
public sealed class PlatformUser : AggregateRoot<PlatformUserId>
{
    public const int MaxSubjectLength = 200;

    private PlatformUser(PlatformUserId id)
        : base(id)
    {
    }

    public string Subject { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<PlatformUser> Register(string? subject, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<PlatformUser>(CommonErrors.TimeNotUtc);
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
        {
            return Result.Failure<PlatformUser>(IdentityErrors.SubjectInvalid);
        }

        return Result.Success(new PlatformUser(PlatformUserId.New()) { Subject = subject, CreatedAt = now });
    }
}
