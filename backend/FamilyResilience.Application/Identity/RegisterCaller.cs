using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// Registers the signed-in caller as a platform user once they have confirmed their details, the same way creating a
/// household does (D-47): a platform user holds the token subject and the time only, never a name, an identity number
/// or anything else the identity provider released (D-85, D-97). A caller who is already registered gets their first
/// registration time back and nothing is written. Audited as <c>identity.user.registered</c>.
/// </summary>
public sealed class RegisterCaller(
    ICurrentUser currentUser,
    IPlatformUserRepository users,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const string Registered = "identity.user.registered";
    public const string ResourceType = "platform_user";

    public async Task<Result<Registration>> HandleAsync(CancellationToken cancellationToken)
    {
        var subject = currentUser.SubjectId ?? throw new InvalidOperationException("Registration needs a signed-in caller.");
        if (await users.GetBySubjectAsync(subject, cancellationToken) is { } existing)
        {
            return Result.Success(new Registration(new RegistrationResponse(existing.CreatedAt), Created: false));
        }

        // PostgreSQL keeps microseconds: the time returned now is the time every later read returns.
        var now = timeProvider.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10));
        var registered = PlatformUser.Register(subject, now);
        if (registered.IsFailure)
        {
            return Result.Failure<Registration>(registered.Error!);
        }

        users.Add(registered.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, Registered, ResourceType, registered.Value.Id.Value, cancellationToken);
        if (committed.Error == HouseholdErrors.AlreadyMember)
        {
            // The unique subject index: another request registered the caller at the same moment. Asking again answers 200.
            return Result.Failure<Registration>(PersistenceErrors.Conflict);
        }

        return committed.IsFailure
            ? Result.Failure<Registration>(committed.Error!)
            : Result.Success(new Registration(new RegistrationResponse(now), Created: true));
    }
}
