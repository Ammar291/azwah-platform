using System.Reflection;
using FamilyResilience.Api.Errors;
using FamilyResilience.Application;
using FamilyResilience.Domain;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Api;

public sealed class ErrorResultsTests
{
    private static HashSet<string> DeclaredCodes() =>
        new[] { DomainAssembly.Reference, ApplicationAssembly.Reference }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsInitOnly && field.FieldType == typeof(Error))
            .Select(field => field.GetValue(null))
            .OfType<Error>()
            .Select(error => error.Code)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_declared_error_has_exactly_one_http_status()
    {
        var missing = DeclaredCodes().Where(code => !ErrorResults.StatusCodesByErrorCode.ContainsKey(code)).Order().ToList();

        Assert.True(missing.Count == 0, $"Classify these errors in ErrorResults: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_map_names_no_error_that_does_not_exist()
    {
        var stale = ErrorResults.StatusCodesByErrorCode.Keys.Except(DeclaredCodes()).Order().ToList();

        Assert.True(stale.Count == 0, $"Remove unknown codes from ErrorResults: {string.Join(", ", stale)}");
    }

    [Theory]
    [InlineData("questionnaires.version_not_found", 404)]
    [InlineData("eligibility.definition_invalid", 400)]
    [InlineData("scoring.policy_not_approved", 409)]
    [InlineData("scoring.policy_unsupported", 409)]
    [InlineData("audit.action_invalid", 500)]
    public void Representative_errors_map_to_their_status(string code, int status) =>
        Assert.Equal(status, ErrorResults.StatusCodesByErrorCode[code]);
}
