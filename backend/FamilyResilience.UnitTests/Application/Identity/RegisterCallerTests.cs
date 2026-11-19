using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Identity;

public sealed class RegisterCallerTests
{
    private const string Subject = "uaepass-stg|7f0c-test";

    private readonly InMemoryPlatformUsers _users = new();
    private readonly FakeAuditEventRepository _audit = new();

    private RegisterCaller UseCase(IUnitOfWork? unitOfWork = null, string? subject = Subject)
    {
        var currentUser = new FakeCurrentUser(subject is not null, subject, "participant");
        unitOfWork ??= new FakeUnitOfWork();
        return new RegisterCaller(
            currentUser,
            _users,
            unitOfWork,
            new AuditLog(_audit, unitOfWork, currentUser, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task A_caller_without_a_platform_user_is_registered_now_and_audited()
    {
        var result = await UseCase().HandleAsync(TestContext.Current.CancellationToken);

        var registration = Ok(result);
        Assert.True(registration.Created);
        Assert.Equal(Now, registration.Response.RegisteredAt);
        var user = Assert.Single(_users.Added);
        Assert.Equal((Subject, Now), (user.Subject, user.CreatedAt));
        var recorded = Assert.Single(_audit.Added);
        Assert.Equal(
            (RegisterCaller.Registered, RegisterCaller.ResourceType, user.Id.Value.ToString()),
            (recorded.Action, recorded.ResourceType, recorded.ResourceId));
    }

    [Fact]
    public async Task A_caller_already_registered_keeps_the_first_registration_time_and_nothing_is_written()
    {
        var earlier = Ok(PlatformUser.Register(Subject, Now.AddDays(-3)));
        _users.Existing.Add(earlier);
        var unitOfWork = new FakeUnitOfWork();

        var result = await UseCase(unitOfWork).HandleAsync(TestContext.Current.CancellationToken);

        var registration = Ok(result);
        Assert.False(registration.Created);
        Assert.Equal(Now.AddDays(-3), registration.Response.RegisteredAt);
        Assert.Empty(_users.Added);
        Assert.Empty(_audit.Added);
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_registration_made_by_another_request_at_the_same_moment_is_a_conflict_not_a_household_error()
    {
        // The unique subject index maps to households.already_member for household creation; here it means a race.
        var result = await UseCase(new RefusingUnitOfWork(HouseholdErrors.AlreadyMember)).HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PersistenceErrors.Conflict, result.Error);
    }

    [Fact]
    public async Task A_subject_the_platform_cannot_store_fails_with_the_identity_error()
    {
        var result = await UseCase(subject: new string('s', PlatformUser.MaxSubjectLength + 1)).HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IdentityErrors.SubjectInvalid, result.Error);
        Assert.Empty(_users.Added);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void Only_an_explicit_confirmation_is_valid(bool? confirmed, bool valid)
    {
        var outcome = new RegistrationRequestValidator().Validate(new RegistrationRequest(confirmed));

        Assert.Equal(valid, outcome.IsValid);
        if (!valid)
        {
            Assert.Equal(nameof(RegistrationRequest.DetailsConfirmed), Assert.Single(outcome.Errors).PropertyName);
        }
    }

    private sealed class InMemoryPlatformUsers : IPlatformUserRepository
    {
        public List<PlatformUser> Existing { get; } = [];

        public List<PlatformUser> Added { get; } = [];

        public void Add(PlatformUser user) => Added.Add(user);

        public Task<PlatformUser?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing.Concat(Added).FirstOrDefault(user => user.Subject == subject));

        public Task<DateTimeOffset?> FindRegisteredAtAsync(string subject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing.Concat(Added).FirstOrDefault(user => user.Subject == subject)?.CreatedAt);
    }

    private sealed class RefusingUnitOfWork(Error error) : IUnitOfWork
    {
        public Task<Result> CommitAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Failure(error));

        public Task<Result<T>> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default) =>
            work(cancellationToken);
    }
}
