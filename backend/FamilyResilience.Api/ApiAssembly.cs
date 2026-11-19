using System.Reflection;

namespace FamilyResilience.Api;

/// <summary>Points at this assembly for scanning and architecture tests.</summary>
public static class ApiAssembly
{
    public static Assembly Reference { get; } = typeof(ApiAssembly).Assembly;
}
