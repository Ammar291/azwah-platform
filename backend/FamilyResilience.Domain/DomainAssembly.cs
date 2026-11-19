using System.Reflection;

namespace FamilyResilience.Domain;

/// <summary>Points at this assembly for scanning and architecture tests.</summary>
public static class DomainAssembly
{
    public static Assembly Reference { get; } = typeof(DomainAssembly).Assembly;
}
