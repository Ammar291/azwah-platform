using System.Reflection;

namespace FamilyResilience.Infrastructure;

/// <summary>Points at this assembly for scanning and architecture tests.</summary>
public static class InfrastructureAssembly
{
    public static Assembly Reference { get; } = typeof(InfrastructureAssembly).Assembly;
}
