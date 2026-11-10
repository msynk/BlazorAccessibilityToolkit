using BlazorAccessibilityToolkit.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>
/// Audits of whole pages of this running app with axe-core and the toolkit's browser diagnostics, with exclusions and
/// documented suppressions. In a test project, the app runs in a fixture (see AppFixture in the end-to-end tests).
/// </summary>
public static class PageAuditExamples
{
    public static readonly LabExample LegacyPageAudit = new()
    {
        Id = "legacy-page-audit",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:legacy-page-audit
            await using var session = await env.Browser.NewSessionAsync();
            await session.Page.GotoAsync(env.Url("legacy"));                            // a page of this app
            await session.Page.Locator("main[data-interactive=true]").WaitForAsync();  // the circuit is connected

            var audit = await session.Page.AuditAccessibilityAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample LegacyPageSuppression = new()
    {
        Id = "legacy-page-suppression",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:legacy-page-suppression
            var options = new AccessibilityAuditOptions
            {
                Suppressions =
                [
                    new Suppression
                    {
                        RuleId = "button-name",
                        Selector = ".icon-button",
                        Reason = "Legacy toolbar button, replaced by IconButton in ticket 1234",
                        Owner = "checkout team",
                        Expires = DateOnly.FromDateTime(DateTime.Today).AddMonths(3),   // it stops applying after this day
                    },
                ],
            };

            await using var session = await env.Browser.NewSessionAsync();
            await session.Page.GotoAsync(env.Url("legacy"));
            await session.Page.Locator("main[data-interactive=true]").WaitForAsync();

            var audit = await session.Page.AuditAccessibilityAsync(options);
            env.Show(audit);

            audit.ShouldHaveNoViolations();                       // passes: the finding is still reported, as suppressed
            Assert.Equal(1, audit.Suppressed.Count);
            #endregion
        },
    };

    public static readonly LabExample ExpiredSuppression = new()
    {
        Id = "expired-suppression",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:expired-suppression
            var options = new AccessibilityAuditOptions
            {
                Suppressions =
                [
                    new Suppression
                    {
                        RuleId = "button-name",
                        Selector = ".icon-button",
                        Reason = "Legacy toolbar button, replaced by IconButton in ticket 1234",
                        Owner = "checkout team",
                        Expires = DateOnly.FromDateTime(DateTime.Today).AddDays(-1),    // yesterday
                    },
                ],
            };

            await using var session = await env.Browser.NewSessionAsync();
            await session.Page.GotoAsync(env.Url("legacy"));
            await session.Page.Locator("main[data-interactive=true]").WaitForAsync();

            var audit = await session.Page.AuditAccessibilityAsync(options);
            env.Show(audit);

            audit.ShouldHaveNoViolations();                       // fails: an expired suppression hides nothing
            #endregion
        },
    };

    public static readonly LabExample ShowcasePageAudit = new()
    {
        Id = "showcase-page-audit",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:showcase-page-audit
            // The Blazor-aware rules page renders four intentionally broken components inside [data-known-defect]
            // elements. Exclude them, and everything else on the page must pass: layout, navigation, the accessible
            // components, the tables and the code listings.
            var options = new AccessibilityAuditOptions { ExcludeSelectors = ["[data-known-defect]"] };

            await using var session = await env.Browser.NewSessionAsync();
            await session.Page.GotoAsync(env.Url("blazor-rules"));
            await session.Page.Locator("main[data-interactive=true]").WaitForAsync();

            var audit = await session.Page.AuditAccessibilityAsync(options);
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };
}
