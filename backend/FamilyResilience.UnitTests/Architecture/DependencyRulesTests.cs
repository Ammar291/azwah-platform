using System.Reflection;
using System.Xml.Linq;
using FamilyResilience.Api;
using FamilyResilience.Application;
using FamilyResilience.Domain;
using FamilyResilience.Infrastructure;

namespace FamilyResilience.UnitTests.Architecture;

public sealed class DependencyRulesTests
{
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "FamilyResilience.Domain", [] },
        { "FamilyResilience.Application", ["FamilyResilience.Domain"] },
        { "FamilyResilience.Infrastructure", ["FamilyResilience.Application", "FamilyResilience.Domain"] },
        { "FamilyResilience.Api", ["FamilyResilience.Application", "FamilyResilience.Infrastructure"] },
        // The development identity provider mints tokens, so it stays a separate app that nothing references.
        { "FamilyResilience.IdentityProvider", [] },
        // The live evaluation harness (D-127) drives the real adapters; nothing references it and it never ships.
        { "FamilyResilience.Evaluation", ["FamilyResilience.Application", "FamilyResilience.Infrastructure"] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Project_declares_only_its_allowed_project_references(string project, string[] allowed)
    {
        var declared = ReadProjectReferences(project);

        Assert.Equal(allowed.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Domain_does_not_reference_other_layers_or_frameworks() =>
        AssertNoReferenceStartingWith(
            DomainAssembly.Reference,
            "FamilyResilience.", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "FluentValidation");

    [Fact]
    public void Application_never_references_ef_core_aspnetcore_or_outer_layers() =>
        AssertNoReferenceStartingWith(
            ApplicationAssembly.Reference,
            "FamilyResilience.Infrastructure", "FamilyResilience.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql");

    [Fact]
    public void Infrastructure_never_references_the_api() =>
        AssertNoReferenceStartingWith(InfrastructureAssembly.Reference, "FamilyResilience.Api", "Microsoft.AspNetCore");

    [Fact]
    public void The_api_never_references_the_development_identity_provider() =>
        AssertNoReferenceStartingWith(ApiAssembly.Reference, "FamilyResilience.IdentityProvider");

    [Fact]
    public void The_api_image_never_contains_the_development_identity_provider()
    {
        var dockerfile = File.ReadAllText(Path.Combine(BackendRoot(), "Dockerfile"));

        Assert.DoesNotContain("IdentityProvider", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_api_never_references_or_ships_the_evaluation_harness()
    {
        AssertNoReferenceStartingWith(ApiAssembly.Reference, "FamilyResilience.Evaluation");
        Assert.DoesNotContain("Evaluation", File.ReadAllText(Path.Combine(BackendRoot(), "Dockerfile")), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ReadProjectReferences(string project)
    {
        var path = Path.Combine(BackendRoot(), project, $"{project}.csproj");
        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(include => Path.GetFileNameWithoutExtension(include.Split('/')[^1]))
            .ToList();
    }

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyResilience.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("FamilyResilience.slnx was not found above the test output folder.");
    }

    private static void AssertNoReferenceStartingWith(Assembly assembly, params string[] prefixes)
    {
        var offending = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"{assembly.GetName().Name} must not reference: {string.Join(", ", offending)}");
    }
}
