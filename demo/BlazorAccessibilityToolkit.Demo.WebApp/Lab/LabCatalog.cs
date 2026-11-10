using System.Reflection;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>Every live example of the showcase (the public static fields in Lab/Examples).</summary>
public static class LabCatalog
{
    private static readonly Lazy<IReadOnlyList<LabExample>> All = new(() => typeof(LabCatalog).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(Examples.ComponentAuditExamples).Namespace)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Select(f => f.GetValue(null))
        .OfType<LabExample>()
        .ToArray());

    public static IReadOnlyList<LabExample> Examples => All.Value;
}
