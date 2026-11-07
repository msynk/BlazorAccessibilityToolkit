using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class AriaPatternTests(BrowserFixture browser)
{
    private const string MenuScript = """
        <script>
          const b = document.getElementById('mb'), m = document.getElementById('menu');
          const items = () => Array.from(m.querySelectorAll('[role=menuitem]'));
          const open = () => { m.hidden = false; b.setAttribute('aria-expanded', 'true'); items()[0].focus(); };
          const close = () => { m.hidden = true; b.setAttribute('aria-expanded', 'false'); b.focus(); };
          b.addEventListener('click', open);
          m.addEventListener('keydown', e => {
            const list = items(), i = list.indexOf(document.activeElement);
            if (e.key === 'ArrowDown') { e.preventDefault(); list[(i + 1) % list.length].focus(); }
            if (e.key === 'ArrowUp') { e.preventDefault(); list[(i - 1 + list.length) % list.length].focus(); }
            if (e.key === 'Escape') close();
          });
        </script>
        """;

    private const string Menu = """
        <main><h1>x</h1>
          <button id="mb" aria-haspopup="menu" aria-expanded="false" aria-controls="menu">Actions</button>
          <ul id="menu" role="menu" hidden aria-label="Actions">
            <li role="menuitem" tabindex="-1">Edit</li><li role="menuitem" tabindex="-1">Copy</li><li role="menuitem" tabindex="-1">Delete</li>
          </ul>
        </main>
        """;

    [Fact]
    public async Task Menu_button_following_the_pattern_passes()
    {
        await using var session = await browser.PageAsync(Menu + MenuScript);
        var result = await AriaPatterns.VerifyMenuButtonAsync(session.Page.Locator("#mb"));
        result.ShouldPass();
        Assert.True(result.Checks.Count >= 10);
    }

    [Fact]
    public async Task Menu_button_without_escape_and_wrapping_fails_with_a_checklist()
    {
        var broken = MenuScript.Replace("if (e.key === 'Escape') close();", string.Empty, StringComparison.Ordinal)
            .Replace("(i - 1 + list.length) % list.length", "Math.max(0, i - 1)", StringComparison.Ordinal);
        await using var session = await browser.PageAsync(Menu + broken);
        var result = await AriaPatterns.VerifyMenuButtonAsync(session.Page.Locator("#mb"), options: new PatternOptions { Timeout = TimeSpan.FromMilliseconds(300) });

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "Escape closes the menu");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.Contains("wraps", StringComparison.Ordinal));
        var ex = Assert.Throws<AccessibilityAssertionException>(() => result.ShouldPass());
        Assert.Contains("[FAIL] Escape closes the menu", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disclosure_that_does_not_update_aria_expanded_fails()
    {
        const string html = """
            <main><h1>x</h1><button id="d" aria-expanded="false" aria-controls="p">Details</button><div id="p" hidden>More</div></main>
            <script>document.getElementById('d').addEventListener('click', () => { const p = document.getElementById('p'); p.hidden = !p.hidden; });</script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyDisclosureAsync(session.Page.Locator("#d"), new PatternOptions { Timeout = TimeSpan.FromMilliseconds(300) });
        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Enter toggles aria-expanded", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement.StartsWith("Content is shown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Correct_disclosure_passes()
    {
        const string html = """
            <main><h1>x</h1><button id="d" aria-expanded="false" aria-controls="p">Details</button><div id="p" hidden>More</div></main>
            <script>document.getElementById('d').addEventListener('click', e => { const p = document.getElementById('p'); p.hidden = !p.hidden; e.target.setAttribute('aria-expanded', String(!p.hidden)); });</script>
            """;
        await using var session = await browser.PageAsync(html);
        (await AriaPatterns.VerifyDisclosureAsync(session.Page.Locator("#d"))).ShouldPass();
    }

    [Fact]
    public async Task Tabs_without_roving_tabindex_or_arrow_keys_fail()
    {
        const string html = """
            <main><h1>x</h1>
              <div role="tablist" aria-label="Sections">
                <button role="tab" aria-selected="true" aria-controls="p1" id="t1">One</button>
                <button role="tab" aria-selected="false" aria-controls="p2" id="t2">Two</button>
              </div>
              <div role="tabpanel" id="p1" aria-labelledby="t1">First</div>
              <div role="tabpanel" id="p2" aria-labelledby="t2" hidden>Second</div>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyTabsAsync(session.Page.Locator("[role=tablist]"), new PatternOptions { Timeout = TimeSpan.FromMilliseconds(300) });
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.Contains("roving tabindex", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "ArrowRight moves focus to the next tab");
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement == "Exactly one tab is selected");
    }

    [Fact]
    public async Task Tab_selection_mismatch_is_diagnosed()
    {
        const string html = """
            <main><h1>x</h1>
              <div role="tablist" aria-label="Sections">
                <button role="tab" aria-selected="true" aria-controls="p1" id="t1">One</button>
                <button role="tab" aria-selected="true" aria-controls="p2" id="t2" tabindex="-1">Two</button>
              </div>
              <div role="tabpanel" id="p1" aria-labelledby="t1">First</div>
              <div role="tabpanel" id="p2" aria-labelledby="t2" hidden>Second</div>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-tab-selection-mismatch").ToArray();
        Assert.Equal(2, findings.Length);
        Assert.Contains(findings, f => f.Data.TryGetValue("selectedTabs", out var n) && n == "2");
        Assert.Contains(findings, f => f.Target.AccessibleName == "Two");
    }

    [Fact]
    public async Task Dialog_without_focus_management_fails()
    {
        const string html = """
            <main><h1>x</h1><button id="open">Open</button></main>
            <div role="dialog" aria-label="Settings" id="dlg" hidden><button>Close</button></div>
            <script>document.getElementById('open').addEventListener('click', () => document.getElementById('dlg').hidden = false);</script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyModalDialogAsync(session.Page.Locator("#open"), session.Page.Locator("#dlg"), new PatternOptions { Timeout = TimeSpan.FromMilliseconds(300) });
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Dialog is modal", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "Focus moves into the dialog when it opens");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "Escape closes the dialog");
    }
}
