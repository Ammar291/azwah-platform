using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Safeguarding;

public static class SafetyErrors
{
    public static readonly Error AlertNotFound =
        new("safeguarding.alert_not_found", "The safety alert does not exist.");

    public static readonly Error AlertNotOpen =
        new("safeguarding.alert_not_open", "The safety alert is not open.");

    public static readonly Error AlertResolved =
        new("safeguarding.alert_resolved", "The safety alert is already resolved.");
}
