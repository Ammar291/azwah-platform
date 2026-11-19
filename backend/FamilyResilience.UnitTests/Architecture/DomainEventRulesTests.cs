using System.Reflection;
using System.Text.RegularExpressions;
using FamilyResilience.Domain;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Architecture;

public sealed partial class DomainEventRulesTests
{
    private static readonly Type[] EventTypes = DomainAssembly.Reference.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
        .ToArray();

    [Fact]
    public void Every_domain_event_has_a_versioned_type_code()
    {
        var missing = EventTypes
            .Where(type => !TypeCode().IsMatch(CodeOf(type) ?? string.Empty))
            .Select(type => type.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Add [DomainEventType(\"module.event_name.v1\")] to: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Domain_event_type_codes_are_unique()
    {
        var duplicates = EventTypes
            .GroupBy(CodeOf)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate event type codes: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Domain_events_carry_identifiers_enums_numbers_and_times_only()
    {
        var offending = EventTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => !IsAllowed(property.PropertyType))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"Domain events must not carry text or personal values: {string.Join(", ", offending)}");
    }

    private static string? CodeOf(Type type) => type.GetCustomAttribute<DomainEventTypeAttribute>()?.Code;

    private static bool IsAllowed(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum
            || underlying == typeof(Guid)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(int)
            || underlying == typeof(decimal)
            || underlying == typeof(bool)
            || underlying.GetInterfaces().Any(contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEntityId<>));
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*\.v[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypeCode();
}
