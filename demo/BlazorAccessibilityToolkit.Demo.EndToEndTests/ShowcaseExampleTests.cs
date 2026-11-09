using System.Reflection;
using BlazorAccessibilityToolkit.Demo.WebApp.Lab;
using Assert = Xunit.Assert;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorAccessibilityToolkit.Demo.EndToEndTests;

/// <summary>
/// Runs every live example of the showcase through the app's own runner, as the "Run this test" buttons do, and checks
/// that it ends as the page says: tests of accessible components pass, tests of intentional defects fail. If a toolkit
/// change stopped detecting a defect, the showcase would say "Defect not detected"; this test catches that first.
/// </summary>
public sealed class ShowcaseExampleTests(AppFixture app)
{
    public static TheoryData<string> Examples => [.. All.Keys.Order(StringComparer.Ordinal)];

    private static Dictionary<string, LabExample> All { get; } = typeof(LabExample).Assembly.GetTypes()
        .Where(t => t.Namespace == "BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples")
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Select(f => f.GetValue(null))
        .OfType<LabExample>()
        .ToDictionary(e => e.Id);

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Example_ends_as_documented(string id)
    {
        var example = All[id];
        Assert.False(string.IsNullOrWhiteSpace(example.Snippet), "The page shows the code of every example.");

        var result = await app.Services.GetRequiredService<LabRunner>().RunAsync(example, TestContext.Current.CancellationToken);

        Assert.True(
            result.AsExpected(example),
            $"Expected the example to {(example.ExpectFailure ? "fail" : "pass")}, but it ended with {result.Outcome}:{Environment.NewLine}{result.Message}");
    }
}
