using System.Reflection;
using System.Text.RegularExpressions;
using FamilyResilience.Application;
using FamilyResilience.UnitTests.Support;

namespace FamilyResilience.UnitTests.Architecture;

public sealed class SupportResourcesIsolationTests
{
    private static readonly Regex ForbiddenModules = new(
        @"FamilyResilience\.(Domain|Application)\.(Sessions|Scoring|Referrals|Assignments|Households|Members|Consent|Questionnaires|Analytics|Privacy)\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] AllowedNamespaces =
    [
        "FamilyResilience.Application.SupportResources",
        "FamilyResilience.Domain.SupportResources",
        "FamilyResilience.Domain.Common",
        "FamilyResilience.Application.Abstractions",
        "FamilyResilience.Application.Audit",
        // The participant's search text is screened for crisis phrases and recorded as fixed codes (D-135).
        "FamilyResilience.Application.Safeguarding",
        "FamilyResilience.Domain.Safeguarding",
        "System",
        "Microsoft.Extensions",
    ];

    [Fact]
    public void The_module_never_uses_participant_data_modules()
    {
        var root = RepositoryFiles.Root();
        var folders = new[]
        {
            Path.Combine(root, "backend", "FamilyResilience.Domain", "SupportResources"),
            Path.Combine(root, "backend", "FamilyResilience.Application", "SupportResources"),
        };

        var files = folders.SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)).ToList();
        Assert.NotEmpty(files);

        var offenders = files.Where(file => ForbiddenModules.IsMatch(File.ReadAllText(file))).ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Module_services_depend_only_on_allowed_types()
    {
        var classes = ApplicationAssembly.Reference.GetTypes()
            .Where(type => type.IsClass && type.Namespace == "FamilyResilience.Application.SupportResources")
            .ToList();
        Assert.NotEmpty(classes);

        var offenders = classes
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .SelectMany(constructor => constructor.GetParameters()
                .SelectMany(parameter => Flatten(parameter.ParameterType))
                .Where(type => !IsAllowed(type))
                .Select(type => $"{constructor.DeclaringType!.Name} takes {type.FullName}"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }

    private static bool IsAllowed(Type type) =>
        type.IsGenericParameter
        || AllowedNamespaces.Any(allowed => type.Namespace is not null
            && (type.Namespace == allowed || type.Namespace.StartsWith(allowed + ".", StringComparison.Ordinal)));
}
