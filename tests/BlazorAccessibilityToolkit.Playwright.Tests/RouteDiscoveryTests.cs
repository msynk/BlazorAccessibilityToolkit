using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents.Routes;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class RouteDiscoveryTests(BrowserFixture browser)
{
    [Fact]
    public void Discovers_routes_with_parameters_and_builds_urls()
    {
        var routes = BlazorRoutes.Discover([typeof(RoutedHome).Assembly], new RouteDiscoveryOptions { ParameterValues = new Dictionary<string, string> { ["id"] = "7", ["Id"] = "42" } });

        var byTemplate = routes.ToDictionary(r => r.Template);
        Assert.Equal(string.Empty, byTemplate["/"].Url);
        Assert.Equal(typeof(RoutedHome), byTemplate["/"].ComponentType);
        Assert.Equal("admin/settings", byTemplate["/admin/settings"].Url);
        Assert.Equal("orders/42", byTemplate["/orders/{Id:int}"].Url);
        Assert.Equal(["Id", "Tab"], byTemplate["/orders/{Id:int}/{Tab?}"].Parameters);
        Assert.Equal(["Id"], byTemplate["/orders/{Id:int}/{Tab?}"].RequiredParameters);
        Assert.Equal("orders/42", byTemplate["/orders/{Id:int}/{Tab?}"].Url);
        Assert.Equal("docs", byTemplate["/docs/{*Path}"].Url);
    }

    [Fact]
    public void Routes_without_values_have_no_url_and_templates_can_be_excluded_or_given_explicit_urls()
    {
        var routes = BlazorRoutes.Discover([typeof(RoutedHome).Assembly], new RouteDiscoveryOptions
        {
            Exclude = ["/admin/*"],
            ParameterValues = new Dictionary<string, string> { ["/docs/{*Path}"] = "/docs/getting-started/install" },
        });

        Assert.DoesNotContain(routes, r => r.Template.StartsWith("/admin", StringComparison.Ordinal));
        Assert.All(routes.Where(r => r.Template.StartsWith("/orders", StringComparison.Ordinal)), r => Assert.Null(r.Url));
        Assert.Equal("docs/getting-started/install", Assert.Single(routes, r => r.Template == "/docs/{*Path}").Url);
    }

    [Fact]
    public async Task A_route_that_cannot_be_opened_is_a_failed_audit_and_the_others_still_run()
    {
        var routes = BlazorRoutes.Discover([typeof(RoutedHome).Assembly], new RouteDiscoveryOptions { Exclude = ["/orders/*", "/docs/*"] });
        await using var session = await browser.Browser.NewSessionAsync();

        // Nothing listens on port 9 (discard), so every navigation fails.
        var result = await session.Page.AuditRoutesAsync(new Uri("http://127.0.0.1:9/"), routes);

        Assert.Equal(routes.Count, result.Audits.Count);
        Assert.All(result.Audits, a => Assert.Equal(AuditStatus.Failed, a.Status));
        Assert.Throws<AccessibilityAuditFailedException>(() => result.ShouldHaveNoViolations());
    }
}
