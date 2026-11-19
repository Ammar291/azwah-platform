using System.Text.RegularExpressions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Identity;

/// <summary>
/// The roles last seen in a subject's validated token. Roles are granted in the identity provider, not here; keeping
/// what the platform last saw lets it audit each role a subject gains or loses (Identity's <c>ObserveCallerRoles</c>).
/// Holds the subject and role codes only.
/// </summary>
public sealed partial class RoleSnapshot : AggregateRoot<RoleSnapshotId>
{
    public const int MaxSubjectLength = PlatformUser.MaxSubjectLength;
    public const int MaxRoles = 20;
    public const int MaxRoleLength = 64;

    private RoleSnapshot(RoleSnapshotId id)
        : base(id)
    {
    }

    public string Subject { get; private set; } = null!;

    /// <summary>Role codes in ordinal order, once each.</summary>
    public string[] Roles { get; private set; } = [];

    /// <summary>When the subject was first observed.</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    /// <summary>When <see cref="Roles"/> last changed (the first observation, until a change).</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    public static Result<RoleSnapshot> Record(string? subject, IEnumerable<string> roles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<RoleSnapshot>(CommonErrors.TimeNotUtc);
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
        {
            return Result.Failure<RoleSnapshot>(IdentityErrors.SubjectInvalid);
        }

        var codes = Normalize(roles);
        if (codes is null)
        {
            return Result.Failure<RoleSnapshot>(IdentityErrors.RolesInvalid);
        }

        return Result.Success(new RoleSnapshot(RoleSnapshotId.New())
        {
            Subject = subject,
            Roles = codes,
            ObservedAt = now,
            ChangedAt = now,
        });
    }

    public Result Change(IEnumerable<string> roles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        var codes = Normalize(roles);
        if (codes is null)
        {
            return Result.Failure(IdentityErrors.RolesInvalid);
        }

        Roles = codes;
        ChangedAt = now;
        return Result.Success();
    }

    private static string[]? Normalize(IEnumerable<string> roles)
    {
        var codes = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return codes.Length <= MaxRoles && codes.All(code => code.Length <= MaxRoleLength && RoleCode().IsMatch(code)) ? codes : null;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleCode();
}
