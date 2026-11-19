using System.Reflection;

namespace FamilyResilience.Application;

/// <summary>Points at this assembly for scanning and architecture tests.</summary>
public static class ApplicationAssembly
{
    public static Assembly Reference { get; } = typeof(ApplicationAssembly).Assembly;
}
