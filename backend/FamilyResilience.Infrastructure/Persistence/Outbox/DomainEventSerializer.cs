using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Infrastructure.Persistence.Outbox;

public static class DomainEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new EntityIdJsonConverterFactory(), new JsonStringEnumConverter() },
    };

    public static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var type = domainEvent.GetType();
        var code = type.GetCustomAttribute<DomainEventTypeAttribute>()?.Code
            ?? throw new InvalidOperationException($"{type.Name} has no [DomainEventType] code.");
        return OutboxMessage.Create(code, JsonSerializer.Serialize(domainEvent, type, Options), domainEvent.OccurredAt);
    }
}
