using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// An organisation a support coordinator may hand referrals to, with the support categories it covers. A record on the
/// platform only: registering a partner connects nothing, and the platform sends a partner nothing (D-61). Partners
/// are entered by system administrators; none is seeded.
/// </summary>
public sealed class PartnerOrganisation : AggregateRoot<PartnerOrganisationId>
{
    public const int MaxNameLength = 200;
    public const int MaxCategories = 20;

    private readonly List<string> _categories = [];

    private PartnerOrganisation(PartnerOrganisationId id)
        : base(id)
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedText Name { get; private set; } = null!;

    /// <summary>
    /// The support category codes this partner takes, as referral rule sets name them. They are not checked against the
    /// rule sets: a system administrator keeps them in step.
    /// </summary>
    public IReadOnlyList<string> Categories => _categories.AsReadOnly();

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PartnerOrganisation> Register(
        string code, LocalizedText name, IReadOnlyList<string> categories, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(categories);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<PartnerOrganisation>(CommonErrors.TimeNotUtc);
        }

        if (!Codes.IsSnakeCode(code))
        {
            return Result.Failure<PartnerOrganisation>(ReferralErrors.PartnerCodeInvalid);
        }

        if (!name.IsComplete)
        {
            return Result.Failure<PartnerOrganisation>(CommonErrors.TextRequired);
        }

        if (categories.Count is 0 or > MaxCategories
            || !categories.All(Codes.IsSnakeCode)
            || categories.Distinct(StringComparer.Ordinal).Count() != categories.Count)
        {
            return Result.Failure<PartnerOrganisation>(ReferralErrors.PartnerCategoriesInvalid);
        }

        var partner = new PartnerOrganisation(PartnerOrganisationId.New())
        {
            Code = code,
            Name = name,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        partner._categories.AddRange(categories);
        return Result.Success(partner);
    }

    public Result SetActive(bool active, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        IsActive = active;
        UpdatedAt = now;
        return Result.Success();
    }

    public bool Covers(string categoryCode) => _categories.Contains(categoryCode, StringComparer.Ordinal);
}
