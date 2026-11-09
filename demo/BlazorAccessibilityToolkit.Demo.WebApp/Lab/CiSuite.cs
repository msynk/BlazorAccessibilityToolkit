using System.Collections.Concurrent;
using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using BlazorAccessibilityToolkit.Reporting;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>
/// A small component test suite for the CI page. It audits the demo components the way a test project does, and
/// returns the audits that the test run would write to BLAZOR_A11Y_RESULTS_DIR. The known defects are included: they
/// play the role of an existing app's accessibility debt.
/// </summary>
public static class CiSuite
{
    public static async Task<IReadOnlyList<AccessibilityAudit>> RunAsync(bool introduceRegression)
    {
        return
        [
            await AuditAsync<IconButton>(f => f.WithParameter(p => p.Label, "Close")),
            await AuditAsync<UnlabelledIconButton>(),

            // The regression: someone puts the old, unlabelled button back into the formatting toolbar.
            introduceRegression
                ? await AuditAsync<LegacyFormattingToolbar>(f => f.WithName(nameof(FormattingToolbar)))
                : await AuditAsync<FormattingToolbar>(),

            await AuditAsync<DisclosureCard>(f => f.WithParameter(p => p.Title, "Shipping").WithParameter(p => p.Details, "Ships in 2 days.")),
            await AuditAsync<ClickableCard>(f => f.WithParameter(p => p.Title, "Shipping").WithParameter(p => p.Details, "Ships in 2 days.")),
            await AuditAsync<CartLines>(),
            await AuditAsync<LegacyCartLines>(),
            await AuditAsync<ShippingOptions>(),
            await AuditAsync<UngroupedShippingOptions>(),
            await AuditAfterSubmitAsync<SignupForm>(),
            await AuditAfterSubmitAsync<LegacySignupForm>(),
        ];
    }

    private static async Task<AccessibilityAudit> AuditAsync<T>(Func<AccessibilityFixtureBuilder<T>, AccessibilityFixtureBuilder<T>>? configure = null)
        where T : IComponent
    {
        var builder = AccessibilityFixture.For<T>();
        await using var fixture = await (configure?.Invoke(builder) ?? builder).BuildAsync();
        return await fixture.AuditAsync();
    }

    private static async Task<AccessibilityAudit> AuditAfterSubmitAsync<T>()
        where T : IComponent
    {
        await using var fixture = await AccessibilityFixture.For<T>().BuildAsync();
        fixture.Component.Find("form").Submit();
        return await fixture.AuditAsync("after invalid submit");
    }
}

/// <summary>Keeps the reports generated on the CI page for a while, so that they can be downloaded.</summary>
public sealed class ReportStore
{
    private const int Capacity = 50;
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset Created, AccessibilityReport Report)> reports = new();

    public Guid Add(AccessibilityReport report)
    {
        var id = Guid.NewGuid();
        reports[id] = (DateTimeOffset.UtcNow, report);
        foreach (var old in reports.OrderByDescending(r => r.Value.Created).Skip(Capacity))
        {
            reports.TryRemove(old.Key, out _);
        }

        return id;
    }

    public AccessibilityReport? Find(Guid id) => reports.TryGetValue(id, out var entry) ? entry.Report : null;

    /// <summary>Maps the report download endpoint: /reports/{id}/{json|junit|sarif|html|console}.</summary>
    public static void MapEndpoints(WebApplication app) =>
        app.MapGet("/reports/{id:guid}/{format}", (Guid id, string format, ReportStore store) =>
        {
            if (store.Find(id) is not { } report || ReportFormatters.All.FirstOrDefault(f => f.Name == format) is not { } formatter)
            {
                return Results.NotFound();
            }

            var content = ReportFormatters.Format(formatter, report);
            var (contentType, inline) = formatter.Name switch
            {
                "html" => ("text/html; charset=utf-8", true),
                "json" or "sarif" => ("application/json; charset=utf-8", false),
                "junit" => ("application/xml; charset=utf-8", false),
                _ => ("text/plain; charset=utf-8", true),
            };
            return inline
                ? Results.Text(content, contentType)
                : Results.File(System.Text.Encoding.UTF8.GetBytes(content), contentType, formatter.DefaultFileName);
        });
}
