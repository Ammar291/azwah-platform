using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class HouseholdPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Household_with_its_roster_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(), TestData.Spouse(), TestData.Child(9));

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(household.Reference, loaded.Reference);
        Assert.Equal(HouseholdStatus.Active, loaded.Status);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
        Assert.NotEqual(0u, loaded.Version);
        Assert.Equal(3, loaded.Members.Count);
        var child = Assert.Single(loaded.Members, member => member.RelationshipToHousehold == HouseholdRelationship.Child);
        Assert.Equal(TestData.BornYearsAgo(9), child.BirthMonth);
        Assert.Null(child.EmploymentStatus);
        Assert.True(loaded.HasActiveMemberUnder18On(TestData.Today));
    }

    [Fact]
    public async Task Reference_lookup_finds_a_saved_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);

        var exists = await fixture.InScopeAsync(
            services => services.GetRequiredService<IHouseholdRepository>().ReferenceExistsAsync(household.Reference, cancellationToken),
            cancellationToken);

        Assert.True(exists);
    }

    [Fact]
    public async Task Creating_a_household_writes_its_outbox_message_in_the_same_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);

        var messages = await fixture.OutboxMessagesAsync(
            "households.household_created.v1", "householdId", household.Id.Value, cancellationToken);

        var message = Assert.Single(messages);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(TestData.Now, message.OccurredAt);
        Assert.Empty(household.GetDomainEvents());
    }

    [Fact]
    public async Task A_duplicate_reference_returns_reference_taken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = await fixture.SavedHouseholdAsync(cancellationToken);
        var duplicate = TestData.Ok(Household.Create(existing.Reference, [TestData.Head()], TestData.Now));

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHouseholdRepository>().Add(duplicate);
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(HouseholdErrors.ReferenceTaken, result.Error);
    }

    [Fact]
    public async Task Concurrent_roster_changes_return_a_concurrency_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken);
        await using var first = fixture.CreateScope();
        await using var second = fixture.CreateScope();
        var firstCopy = await first.ServiceProvider.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken);
        var secondCopy = await second.ServiceProvider.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        TestData.Ok(firstCopy.AddMember(TestData.Spouse(), TestData.Now.AddMinutes(1)));
        Assert.True((await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        TestData.Ok(secondCopy.AddMember(TestData.Child(5), TestData.Now.AddMinutes(2)));
        var result = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(PersistenceErrors.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task Moving_the_primary_earner_flag_in_two_commits_inside_one_transaction_succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(), TestData.Spouse());
        var now = TestData.Now.AddMinutes(1);
        FamilyMemberId newEarnerId = default;

        await using (var scope = fixture.CreateScope())
        {
            var households = scope.ServiceProvider.GetRequiredService<IHouseholdRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var moved = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var loaded = await households.GetAsync(household.Id, token);
                    Assert.NotNull(loaded);

                    // Picked by flag, not by position or ID, so the test holds whichever member sorts first.
                    var current = Assert.Single(loaded.Members, member => member.IsActive && member.IsPrimaryIncomeEarner);
                    var next = Assert.Single(loaded.Members, member => member.IsActive && !member.IsPrimaryIncomeEarner);
                    newEarnerId = next.Id;

                    TestData.Ok(loaded.UpdateMember(current.Id, WithEarner(current, false), now));
                    var cleared = await unitOfWork.CommitAsync(token);
                    if (cleared.IsFailure)
                    {
                        return Result.Failure<bool>(cleared.Error!);
                    }

                    TestData.Ok(loaded.UpdateMember(next.Id, WithEarner(next, true), now));
                    var set = await unitOfWork.CommitAsync(token);
                    return set.IsSuccess ? Result.Success(true) : Result.Failure<bool>(set.Error!);
                },
                cancellationToken);

            Assert.True(moved.IsSuccess);
        }

        var reloaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken),
            cancellationToken);
        Assert.NotNull(reloaded);
        var earner = Assert.Single(reloaded.Members, member => member.IsActive && member.IsPrimaryIncomeEarner);
        Assert.Equal(newEarnerId, earner.Id);
    }

    [Fact]
    public async Task Database_allows_one_active_head_per_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(), TestData.Spouse());
        var spouseId = household.Members[1].Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE family_members SET is_head_of_household = true, relationship_to_household = NULL WHERE id = {spouseId}",
                cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "ux_family_members_one_active_head");
    }

    [Fact]
    public async Task Database_rejects_an_unknown_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var householdId = (await fixture.SavedHouseholdAsync(cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE households SET status = 'Deleted' WHERE id = {householdId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_households_status");
    }

    [Fact]
    public async Task Database_rejects_a_birth_month_outside_1_to_12()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var headId = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0].Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE family_members SET birth_month = 13 WHERE id = {headId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_family_members_birth_month");
    }

    [Fact]
    public async Task Database_rejects_a_head_with_a_relationship_to_the_head()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var headId = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0].Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE family_members SET relationship_to_household = 'Spouse' WHERE id = {headId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_family_members_head_has_no_relationship");
    }

    [Fact]
    public async Task A_household_with_members_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var householdId = (await fixture.SavedHouseholdAsync(cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM households WHERE id = {householdId}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    private static FamilyMemberDetails WithEarner(FamilyMember member, bool isPrimaryIncomeEarner) =>
        new(member.BirthMonth, member.RelationshipToHousehold, member.EmploymentStatus, member.IsHeadOfHousehold, isPrimaryIncomeEarner);
}
