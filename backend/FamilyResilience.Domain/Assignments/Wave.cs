using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Assignments;

/// <summary>One round of assessment (a check-in), about six months apart. The study period for assignments.</summary>
public sealed class Wave : AggregateRoot<WaveId>
{
    private Wave(WaveId id)
        : base(id)
    {
    }

    public string Code { get; private set; } = null!;

    public DateTimeOffset OpensAt { get; private set; }

    public DateTimeOffset ClosesAt { get; private set; }

    public WaveStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AcceptsAssignments => Status is WaveStatus.Planned or WaveStatus.Open;

    public static Result<Wave> Plan(string code, DateTimeOffset opensAt, DateTimeOffset closesAt, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now) || !UtcTime.IsUtc(opensAt) || !UtcTime.IsUtc(closesAt))
        {
            return Result.Failure<Wave>(CommonErrors.TimeNotUtc);
        }

        if (!Codes.IsSnakeCode(code))
        {
            return Result.Failure<Wave>(AssignmentErrors.WaveCodeInvalid);
        }

        if (closesAt <= opensAt)
        {
            return Result.Failure<Wave>(AssignmentErrors.WaveWindowInvalid);
        }

        return Result.Success(new Wave(WaveId.New())
        {
            Code = code,
            OpensAt = opensAt,
            ClosesAt = closesAt,
            Status = WaveStatus.Planned,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    public Result Open(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != WaveStatus.Planned)
        {
            return Result.Failure(AssignmentErrors.WaveNotPlanned);
        }

        Status = WaveStatus.Open;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Close(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status == WaveStatus.Closed)
        {
            return Result.Failure(AssignmentErrors.WaveAlreadyClosed);
        }

        Status = WaveStatus.Closed;
        UpdatedAt = now;
        return Result.Success();
    }
}
