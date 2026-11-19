using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Application;

internal sealed class FakeAuditEventRepository : IAuditEventRepository
{
    public List<AuditEvent> Added { get; } = [];

    public void Add(AuditEvent auditEvent) => Added.Add(auditEvent);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitCount++;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default) =>
        work(cancellationToken);
}

internal sealed class FakeCurrentUser(bool isAuthenticated, string? subjectId, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated { get; } = isAuthenticated;

    public string? SubjectId { get; } = subjectId;

    public IReadOnlyCollection<string> Roles { get; } = roles;

    public bool IsSystem { get; init; }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
