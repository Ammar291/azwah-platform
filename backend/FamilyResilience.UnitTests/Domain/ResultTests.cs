using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain;

public sealed class ResultTests
{
    private static readonly Error SomeError = new("test.failed", "It failed.");

    [Fact]
    public void Success_carries_its_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_has_no_value()
    {
        var result = Result.Failure<int>(SomeError);

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_requires_an_error() =>
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
}
