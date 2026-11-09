using System.Collections;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>
/// A stand-in for your test framework's assertions (xUnit, NUnit, MSTest), so the live examples read exactly like the
/// tests in the demo test projects. Failures are reported like the toolkit's own assertion failures.
/// </summary>
public static class Assert
{
    public static void Empty(IEnumerable collection)
    {
        var items = collection.Cast<object?>().ToArray();
        if (items.Length > 0)
        {
            throw new AccessibilityAssertionException(
                $"Assert.Empty() failure: the collection contains {items.Length} item(s):{Environment.NewLine}{string.Join(Environment.NewLine, items.Select(Describe))}")
            {
                Findings = items.OfType<AccessibilityFinding>().ToArray(),
            };
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AccessibilityAssertionException($"Assert.Equal() failure{Environment.NewLine}Expected: {Describe(expected)}{Environment.NewLine}Actual:   {Describe(actual)}");
        }
    }

    public static void Contains<T>(T expected, IEnumerable<T> collection)
    {
        var items = collection.ToArray();
        if (!items.Contains(expected))
        {
            throw new AccessibilityAssertionException($"Assert.Contains() failure: {Describe(expected)} not found in [{string.Join(", ", items.Select(i => Describe(i)))}]");
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "(null)",
        string s => $"\"{s}\"",
        AccessibilityFinding finding => FindingFormatter.Format(finding),
        _ => value.ToString() ?? string.Empty,
    };
}
