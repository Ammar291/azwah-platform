using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Households;

public sealed class ConfirmRosterTests
{
    private static readonly PlatformUserId Manager = PlatformUserId.New();
    private readonly Household _household = Ok(Household.Enrol(
        HouseholdReference.Generate(), [Head(), Adult(39, relationship: HouseholdRelationship.Spouse)], 0, Manager, Now));

    private readonly FakeAssignmentService _service = new();

    private ConfirmRoster UseCase(params Wave[] openWaves) =>
        new(
            new ManagerAccess(_household.Id),
            new SingleHouseholdRepository(_household),
            new NoLock(),
            new OpenWaves(openWaves),
            _service,
            new FakeUnitOfWork(),
            new AuditLog(new FakeAuditEventRepository(), new FakeUnitOfWork(), new FakeCurrentUser(true, "manager"), new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));

    private static Wave OpenWave()
    {
        var wave = Ok(Wave.Plan($"wave_{Guid.NewGuid():N}", Now.AddDays(-1), Now.AddDays(30), Now));
        Ok(wave.Open(Now));
        return wave;
    }

    [Fact]
    public async Task With_no_open_wave_the_roster_is_confirmed_and_nothing_is_assigned()
    {
        var result = await UseCase().HandleAsync(_household.Id.Value, TestContext.Current.CancellationToken);

        var confirmation = Ok(result);
        Assert.Equal((null, null), (confirmation.WaveId, confirmation.WaveCode));
        Assert.Empty(confirmation.Assignments);
        Assert.Equal(Now, _household.RosterConfirmedAt);
        Assert.Equal(0, _service.Calls);
    }

    [Fact]
    public async Task With_two_open_waves_the_current_wave_is_ambiguous()
    {
        var result = await UseCase(OpenWave(), OpenWave()).HandleAsync(_household.Id.Value, TestContext.Current.CancellationToken);

        Assert.Equal(AssignmentErrors.WaveAmbiguous, result.Error);
        Assert.Equal(0, _service.Calls);
    }

    [Fact]
    public async Task A_failed_assignment_run_fails_the_whole_confirmation()
    {
        _service.Failure = AssignmentErrors.WaveClosed;

        var result = await UseCase(OpenWave()).HandleAsync(_household.Id.Value, TestContext.Current.CancellationToken);

        Assert.Equal(AssignmentErrors.WaveClosed, result.Error);
        Assert.Equal(1, _service.Calls);
    }

    [Fact]
    public async Task A_caller_who_does_not_manage_the_household_finds_nothing()
    {
        var result = await UseCase(OpenWave()).HandleAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(HouseholdErrors.HouseholdNotFound, result.Error);
        Assert.Null(_household.RosterConfirmedAt);
    }

    private sealed class ManagerAccess(HouseholdId managed) : ICallerAccess
    {
        public Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default) => Task.FromResult<CallerRecord?>(null);

        public Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
            Task.FromResult(householdId == managed);

        public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberId>>([]);
    }

    private sealed class SingleHouseholdRepository(Household household) : IHouseholdRepository
    {
        public void Add(Household added) => throw new NotSupportedException();

        public Task<Household?> GetAsync(HouseholdId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == household.Id ? household : null);

        public Task<bool> ReferenceExistsAsync(HouseholdReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<HouseholdStatus?> StatusOfMemberHouseholdAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult<HouseholdStatus?>(household.FindMember(memberId) is null ? null : household.Status);

        public Task<IReadOnlyList<WithdrawnHousehold>> ListWithdrawnBeforeAsync(
            DateTimeOffset before, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoLock : IHouseholdRunLock
    {
        public Task LockAsync(HouseholdId householdId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OpenWaves(IReadOnlyList<Wave> open) : IWaveRepository
    {
        public void Add(Wave wave) => throw new NotSupportedException();

        public Task<Wave?> GetAsync(WaveId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(open.FirstOrDefault(wave => wave.Id == id));

        public Task<Wave?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<Wave?>(null);

        public Task<IReadOnlyList<Wave>> ListOpenAtAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
            Task.FromResult(open);

        public Task<IReadOnlyList<Wave>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Wave>>([.. open.Skip(skip).Take(take)]);

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(open.Count);
    }

    private sealed class FakeAssignmentService : IHouseholdAssignmentService
    {
        public int Calls { get; private set; }

        public Error? Failure { get; set; }

        public Task<Result<AssignmentRunResponse>> AssignAsync(HouseholdId householdId, WaveId waveId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Failure is { } error
                ? Result.Failure<AssignmentRunResponse>(error)
                : Result.Success(new AssignmentRunResponse(householdId.Value, waveId.Value, Now, [], [], [], [])));
        }
    }
}
