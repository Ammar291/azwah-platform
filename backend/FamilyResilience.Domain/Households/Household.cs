using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Households;

/// <summary>
/// A family taking part, with its roster. The household is the only way to add or change members, so the roster
/// rules (one active head, one active primary earner, both adults) are checked in one place. Every roster change
/// also changes the household row, so its concurrency token covers the whole roster. The household also holds the
/// memberships that link platform users to members, so who may act for whom is decided from one aggregate.
/// </summary>
public sealed class Household : AggregateRoot<HouseholdId>
{
    /// <summary>The most active members a roster may hold; inactive members are history and do not count.</summary>
    public const int MaxActiveMembers = 30;

    private readonly List<FamilyMember> _members = [];
    private readonly List<HouseholdMembership> _memberships = [];

    private Household(HouseholdId id)
        : base(id)
    {
    }

    public HouseholdReference Reference { get; private set; } = null!;

    public HouseholdStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the roster was last confirmed; any roster change clears it until the roster is confirmed again.</summary>
    public DateTimeOffset? RosterConfirmedAt { get; private set; }

    public IReadOnlyList<FamilyMember> Members => _members.AsReadOnly();

    public IReadOnlyList<HouseholdMembership> Memberships => _memberships.AsReadOnly();

    public static Result<Household> Create(
        HouseholdReference reference, IReadOnlyCollection<FamilyMemberDetails> roster, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(roster);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<Household>(CommonErrors.TimeNotUtc);
        }

        if (roster.Count == 0)
        {
            return Result.Failure<Household>(HouseholdErrors.RosterEmpty);
        }

        var household = new Household(HouseholdId.New())
        {
            Reference = reference,
            Status = HouseholdStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var details in roster)
        {
            var added = household.AddMemberCore(details, now);
            if (added.IsFailure)
            {
                return Result.Failure<Household>(added.Error!);
            }
        }

        household.Raise(new HouseholdCreated(household.Id, now));
        return Result.Success(household);
    }

    /// <summary>
    /// Creates the household from its roster and makes <paramref name="userId"/> its manager, linked to the roster
    /// entry at <paramref name="selfIndex"/>, who must be 18 or over.
    /// </summary>
    public static Result<Household> Enrol(
        HouseholdReference reference,
        IReadOnlyList<FamilyMemberDetails> roster,
        int selfIndex,
        PlatformUserId userId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentOutOfRangeException.ThrowIfNegative(selfIndex);

        var created = Create(reference, roster, now);
        if (created.IsFailure)
        {
            return created;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selfIndex, roster.Count);
        var household = created.Value;
        var self = household._members[selfIndex];
        if (!self.IsCertainlyAdultOn(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return Result.Failure<Household>(HouseholdErrors.ManagerUnder18);
        }

        household._memberships.Add(HouseholdMembership.Create(household.Id, userId, self.Id, HouseholdRole.Manager, now));
        return Result.Success(household);
    }

    /// <summary>
    /// Gives an adult member their own account: <paramref name="userId"/> becomes a <see cref="HouseholdRole.Member"/>
    /// linked to <paramref name="memberId"/>, who from then on answers only for themselves. The basis for joining a
    /// household; no endpoint uses it yet.
    /// </summary>
    public Result<HouseholdMembership> LinkAccount(PlatformUserId userId, FamilyMemberId memberId, DateTimeOffset now)
    {
        var allowed = EnsureCanChange(now);
        if (allowed.IsFailure)
        {
            return Result.Failure<HouseholdMembership>(allowed.Error!);
        }

        var member = FindActiveMember(memberId);
        if (member is null)
        {
            return Result.Failure<HouseholdMembership>(HouseholdErrors.MemberNotFound);
        }

        if (!member.IsCertainlyAdultOn(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return Result.Failure<HouseholdMembership>(HouseholdErrors.AccountHolderUnder18);
        }

        if (ActiveMembershipFor(memberId) is not null)
        {
            return Result.Failure<HouseholdMembership>(HouseholdErrors.MemberAlreadyLinked);
        }

        if (FindActiveMembership(userId) is not null)
        {
            return Result.Failure<HouseholdMembership>(HouseholdErrors.AlreadyMember);
        }

        var membership = HouseholdMembership.Create(Id, userId, memberId, HouseholdRole.Member, now);
        _memberships.Add(membership);
        UpdatedAt = now;
        return Result.Success(membership);
    }

    /// <summary>
    /// Confirms the roster as complete enough to decide eligibility: exactly one active head, a relationship for every
    /// other active member and an employment status for every active adult (D-50, approved in D-58). Confirming again
    /// moves the confirmation time.
    /// </summary>
    public Result ConfirmRoster(DateTimeOffset now)
    {
        var allowed = EnsureCanChange(now);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var active = _members.Where(member => member.IsActive).ToList();
        if (active.Count(member => member.IsHeadOfHousehold) != 1)
        {
            return Result.Failure(HouseholdErrors.RosterHasNoHead);
        }

        if (active.Exists(member => !member.IsHeadOfHousehold && member.RelationshipToHousehold is null))
        {
            return Result.Failure(HouseholdErrors.RosterMissingRelationship);
        }

        if (active.Exists(member => member.AgeOn(today) >= FamilyMember.AdultAge && member.EmploymentStatus is null))
        {
            return Result.Failure(HouseholdErrors.RosterMissingEmploymentStatus);
        }

        RosterConfirmedAt = now;
        UpdatedAt = now;
        Raise(new RosterConfirmed(Id, now));
        return Result.Success();
    }

    public Result<FamilyMember> AddMember(FamilyMemberDetails details, DateTimeOffset now)
    {
        var allowed = EnsureCanChange(now);
        if (allowed.IsFailure)
        {
            return Result.Failure<FamilyMember>(allowed.Error!);
        }

        var added = AddMemberCore(details, now);
        if (added.IsSuccess)
        {
            RosterChanged(now);
        }

        return added;
    }

    public Result UpdateMember(FamilyMemberId memberId, FamilyMemberDetails details, DateTimeOffset now)
    {
        var allowed = EnsureCanChange(now);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var member = FindActiveMember(memberId);
        if (member is null)
        {
            return Result.Failure(HouseholdErrors.MemberNotFound);
        }

        var valid = Validate(details, memberId, now);
        if (valid.IsFailure)
        {
            return valid;
        }

        member.Update(details, now);
        RosterChanged(now);
        return Result.Success();
    }

    public Result DeactivateMember(FamilyMemberId memberId, DateTimeOffset now)
    {
        var allowed = EnsureCanChange(now);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var member = FindActiveMember(memberId);
        if (member is null)
        {
            return Result.Failure(HouseholdErrors.MemberNotFound);
        }

        var membership = ActiveMembershipFor(memberId);
        if (membership is { Role: HouseholdRole.Manager })
        {
            return Result.Failure(HouseholdErrors.MemberIsManager);
        }

        membership?.End(now);
        member.Deactivate(now);
        RosterChanged(now);
        Raise(new FamilyMemberDeactivated(Id, memberId, now));
        return Result.Success();
    }

    public Result Withdraw(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status == HouseholdStatus.Withdrawn)
        {
            return Result.Failure(HouseholdErrors.AlreadyWithdrawn);
        }

        Status = HouseholdStatus.Withdrawn;
        UpdatedAt = now;
        Raise(new HouseholdWithdrawn(Id, now));
        return Result.Success();
    }

    public FamilyMember? FindMember(FamilyMemberId memberId) => _members.Find(member => member.Id == memberId);

    /// <summary>Whether an active member is under 18 on <paramref name="date"/> (family-voices Module B routing input).</summary>
    public bool HasActiveMemberUnder18On(DateOnly date) =>
        _members.Exists(member => member.IsActive && member.AgeOn(date) < FamilyMember.AdultAge);

    public HouseholdMembership? FindActiveMembership(PlatformUserId userId) =>
        _memberships.Find(membership => membership.PlatformUserId == userId && membership.IsActive);

    public HouseholdMembership? ActiveMembershipFor(FamilyMemberId memberId) =>
        _memberships.Find(membership => membership.FamilyMemberId == memberId && membership.IsActive);

    public bool IsManagedBy(PlatformUserId userId) => FindActiveMembership(userId) is { Role: HouseholdRole.Manager };

    /// <summary>
    /// Whether <paramref name="userId"/> signs in as the household's active head: their own linked member, never a member
    /// they answer for. Only the head sees the household's scores in the family app (D-103).
    /// </summary>
    public bool IsHeadOfHousehold(PlatformUserId userId) =>
        FindActiveMembership(userId) is { } membership
        && FindMember(membership.FamilyMemberId) is { IsActive: true, IsHeadOfHousehold: true };

    /// <summary>
    /// Whether <paramref name="userId"/> may act for <paramref name="memberId"/>: their own linked member, or, as the
    /// active manager, an active member who has no account of their own (D-49, approved in D-58).
    /// </summary>
    public bool MayActFor(PlatformUserId userId, FamilyMemberId memberId)
    {
        var membership = FindActiveMembership(userId);
        if (membership is null)
        {
            return false;
        }

        if (membership.FamilyMemberId == memberId)
        {
            return true;
        }

        return membership.Role == HouseholdRole.Manager
            && FindMember(memberId) is { IsActive: true }
            && ActiveMembershipFor(memberId) is null;
    }

    /// <summary>Every member <paramref name="userId"/> may act for.</summary>
    public IReadOnlyList<FamilyMemberId> MembersActableBy(PlatformUserId userId) =>
        _members.Where(member => MayActFor(userId, member.Id)).Select(member => member.Id).ToList();

    private void RosterChanged(DateTimeOffset now)
    {
        RosterConfirmedAt = null;
        UpdatedAt = now;
    }

    private FamilyMember? FindActiveMember(FamilyMemberId memberId) =>
        _members.Find(member => member.Id == memberId && member.IsActive);

    private Result EnsureCanChange(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        return Status == HouseholdStatus.Active ? Result.Success() : Result.Failure(HouseholdErrors.NotActive);
    }

    private Result<FamilyMember> AddMemberCore(FamilyMemberDetails details, DateTimeOffset now)
    {
        var valid = Validate(details, null, now);
        if (valid.IsFailure)
        {
            return Result.Failure<FamilyMember>(valid.Error!);
        }

        if (_members.Count(existing => existing.IsActive) >= MaxActiveMembers)
        {
            return Result.Failure<FamilyMember>(HouseholdErrors.RosterFull);
        }

        var member = FamilyMember.Create(Id, details, now);
        _members.Add(member);
        return Result.Success(member);
    }

    private Result Validate(FamilyMemberDetails details, FamilyMemberId? replacing, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(details.BirthMonth);

        if (details.RelationshipToHousehold is { } relationship && !Enum.IsDefined(relationship))
        {
            return Result.Failure(HouseholdErrors.RelationshipInvalid);
        }

        if (details.EmploymentStatus is { } employment && !Enum.IsDefined(employment))
        {
            return Result.Failure(HouseholdErrors.EmploymentStatusInvalid);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var age = details.BirthMonth.AgeOn(today);
        if (age < 0)
        {
            return Result.Failure(HouseholdErrors.BirthMonthInFuture);
        }

        var others = _members.Where(member => member.IsActive && member.Id != replacing).ToList();

        if (details.IsHeadOfHousehold)
        {
            if (details.RelationshipToHousehold is not null)
            {
                return Result.Failure(HouseholdErrors.HeadHasRelationship);
            }

            if (age < FamilyMember.AdultAge)
            {
                return Result.Failure(HouseholdErrors.HeadUnder18);
            }

            if (others.Exists(member => member.IsHeadOfHousehold))
            {
                return Result.Failure(HouseholdErrors.HeadAlreadyExists);
            }
        }

        if (details.IsPrimaryIncomeEarner)
        {
            if (age < FamilyMember.AdultAge)
            {
                return Result.Failure(HouseholdErrors.PrimaryEarnerUnder18);
            }

            if (others.Exists(member => member.IsPrimaryIncomeEarner))
            {
                return Result.Failure(HouseholdErrors.PrimaryEarnerAlreadyExists);
            }
        }

        // A member with an account stays an adult: correcting their birth month cannot undo the age check made when
        // the account was linked. Like that check, this is a safeguard, so the member must be certainly 18; the head
        // and primary-earner rules above describe the roster and keep the first-of-the-month convention.
        if (replacing is { } memberId
            && !details.BirthMonth.IsCertainlyAtLeast(FamilyMember.AdultAge, today)
            && ActiveMembershipFor(memberId) is { } membership)
        {
            return Result.Failure(membership.Role == HouseholdRole.Manager
                ? HouseholdErrors.ManagerUnder18
                : HouseholdErrors.AccountHolderUnder18);
        }

        return Result.Success();
    }
}
