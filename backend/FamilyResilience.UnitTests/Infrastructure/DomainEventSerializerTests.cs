using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Infrastructure.Persistence.Outbox;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class DomainEventSerializerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Event_becomes_a_pending_outbox_message_with_its_type_code_and_identifier_payload()
    {
        var householdId = HouseholdId.New();

        var message = DomainEventSerializer.ToOutboxMessage(new HouseholdCreated(householdId, Now));

        Assert.Equal("households.household_created.v1", message.Type);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(Now, message.OccurredAt);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(7, message.Id.Version);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(householdId.Value, payload.RootElement.GetProperty("householdId").GetGuid());
    }

    [Fact]
    public void Enum_values_are_written_as_names()
    {
        var message = DomainEventSerializer.ToOutboxMessage(
            new ConsentGranted(ConsentRecordId.New(), FamilyMemberId.New(), ConsentType.Referral, Now));

        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("Referral", payload.RootElement.GetProperty("consentType").GetString());
    }

    [Fact]
    public void Event_without_a_type_code_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => DomainEventSerializer.ToOutboxMessage(new Unlabelled(Now)));

    private sealed record Unlabelled(DateTimeOffset OccurredAt) : IDomainEvent;
}
