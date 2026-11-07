using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class LiveRegionTests(BrowserFixture browser)
{
    private const string Page = """
        <main><h1>Search</h1>
          <div role="status" id="status"></div>
          <div aria-live="assertive" id="errors"></div>
          <div role="alert" aria-live="polite" id="conflict"></div>
          <div id="host"></div>
          <div role="status" id="hidden" style="display:none"></div>
        </main>
        <script>
          window.say = (id, text) => { document.getElementById(id).textContent = text; };
          window.insertToast = text => { const d = document.createElement('div'); d.setAttribute('role', 'status'); d.textContent = text; document.getElementById('host').appendChild(d); };
        </script>
        """;

    [Fact]
    public async Task Records_updates_with_politeness_and_matches_text()
    {
        await using var session = await browser.PageAsync(Page);
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);
        await session.Page.EvaluateAsync("() => setTimeout(() => say('status', '12 results found'), 30)");
        var change = await monitor.ExpectUpdateAsync(new Regex(@"^\d+ results found$"));
        Assert.Equal("polite", change.Politeness);
        Assert.Equal("status", change.Role);
        Assert.True(change.RegionWasPresent);
        Assert.True(change.Atomic);

        await session.Page.EvaluateAsync("() => say('errors', 'Email is required')");
        var error = await monitor.ExpectUpdateAsync("email is required", politeness: "assertive");
        Assert.Equal(LiveRegionChangeKind.ContentAdded, error.Kind);
    }

    [Fact]
    public async Task Repeated_identical_messages_are_asserted_one_by_one()
    {
        await using var session = await browser.PageAsync(Page);
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);
        await session.Page.EvaluateAsync("() => { say('status', 'Saved'); }");
        await monitor.ExpectUpdateAsync("Saved", TextMatch.Exact);
        await session.Page.EvaluateAsync("() => { say('status', ''); setTimeout(() => say('status', 'Saved'), 20); }");
        await monitor.ExpectUpdateAsync("Saved", TextMatch.Exact);
        await Assert.ThrowsAsync<AccessibilityAssertionException>(() => monitor.ExpectUpdateAsync("Saved", TextMatch.Exact, timeout: TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Updates_in_one_mutation_batch_are_asserted_separately()
    {
        await using var session = await browser.PageAsync(Page);
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);

        // Both changes happen in the same task, so the MutationObserver reports them in one callback.
        await session.Page.EvaluateAsync("() => { const s = document.getElementById('status'); s.append('Saving…'); s.textContent = ''; s.append('Changes saved'); }");

        await monitor.ExpectUpdateAsync("Saving");
        var saved = await monitor.ExpectUpdateAsync("Changes saved");
        Assert.Equal("Changes saved", saved.AddedText);
    }

    [Fact]
    public async Task Missing_update_fails_with_observed_updates_and_limitation()
    {
        await using var session = await browser.PageAsync(Page);
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);
        await session.Page.EvaluateAsync("() => say('status', 'Loading…')");
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(() => monitor.ExpectUpdateAsync("Done", timeout: TimeSpan.FromMilliseconds(300)));
        Assert.Contains("Loading", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not screen-reader speech", ex.Message, StringComparison.Ordinal);
        await monitor.ResetAsync();
        await monitor.ExpectNoUpdateAsync(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task Text_outside_live_regions_is_not_an_update()
    {
        await using var session = await browser.PageAsync(Page + "<p id=\"plain\"></p>");
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);
        await session.Page.EvaluateAsync("() => document.getElementById('plain').textContent = 'Saved'");
        await Assert.ThrowsAsync<AccessibilityAssertionException>(() => monitor.ExpectUpdateAsync("Saved", timeout: TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Analysis_flags_inserted_hidden_and_conflicting_regions()
    {
        await using var session = await browser.PageAsync(Page);
        var monitor = await LiveRegionMonitor.StartAsync(session.Page);
        await session.Page.EvaluateAsync("() => { insertToast('Profile updated'); say('hidden', 'You cannot hear me'); }");
        var inserted = await monitor.ExpectUpdateAsync("Profile updated");
        Assert.Equal(LiveRegionChangeKind.Inserted, inserted.Kind);
        Assert.False(inserted.RegionWasPresent);

        var findings = await monitor.AnalyzeAsync();
        Assert.Contains(findings, f => f.RuleId == "blazor-live-region-inserted-with-content");
        Assert.Contains(findings, f => f.RuleId == "blazor-live-region-hidden-update");
        Assert.Contains(findings, f => f.RuleId == "blazor-live-region-politeness-conflict" && f.Target.Selector == "#conflict");
    }
}

public sealed class AriaSnapshotTests(BrowserFixture browser) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "a11y-snapshots-" + Guid.NewGuid().ToString("N"));

    private const string Menu = "<main><h1>x</h1><nav aria-label=\"Main\"><ul><li><a href=\"/\">Home</a></li><li><a href=\"/about\" aria-current=\"page\">About</a></li></ul></nav><p role=\"status\">3 results</p></main>";

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Inline_partial_template_with_regex_matches()
    {
        await using var session = await browser.PageAsync(Menu);
        await session.Page.Locator("main").ShouldMatchAriaSnapshotAsync("""
            - main:
              - navigation "Main":
                - list:
                  - listitem:
                    - link "About"
              - status: /\d+ results/
            """);
        var ex = await Assert.ThrowsAsync<AriaSnapshotMismatchException>(() => session.Page.Locator("nav").ShouldMatchAriaSnapshotAsync("- navigation \"Secondary\"", TimeSpan.FromMilliseconds(500)));
        Assert.Contains("+ - navigation \"Main\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stored_snapshot_lifecycle_create_match_detect_and_update()
    {
        var options = new AriaSnapshotOptions { Directory = _directory, Timeout = TimeSpan.FromMilliseconds(500) };
        await using (var session = await browser.PageAsync(Menu))
        {
            var missing = await Assert.ThrowsAsync<AriaSnapshotMismatchException>(() => session.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options));
            if (IsCi())
            {
                // CI servers (GitHub Actions, Azure Pipelines, ...) set CI=true: missing snapshots fail instead of being created.
                Assert.Contains("not created on CI", missing.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(_directory, "main-nav.aria.yml")));
                await session.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options with { Update = true });
            }
            else
            {
                Assert.Contains("was created", missing.Message, StringComparison.Ordinal);
            }

            Assert.True(File.Exists(Path.Combine(_directory, "main-nav.aria.yml")));
            await session.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options);
        }

        await using (var regressed = await browser.PageAsync(Menu.Replace("<a href=\"/about\" aria-current=\"page\">About</a>", "<a>About</a>", StringComparison.Ordinal)))
        {
            var ex = await Assert.ThrowsAsync<AriaSnapshotMismatchException>(() => regressed.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options));
            Assert.Contains("--- expected", ex.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(_directory, "main-nav.received.aria.yml")));

            await regressed.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options with { Update = true });
            await regressed.Page.Locator("nav").ShouldMatchStoredAriaSnapshotAsync("main-nav", options);
        }
    }

    private static bool IsCi() =>
        Environment.GetEnvironmentVariable("CI") is { } value && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Diff_shows_added_and_removed_lines()
    {
        var diff = AriaSnapshots.Diff("- a\n- b\n- c", "- a\n- c\n- d");
        Assert.Contains("- - b", diff, StringComparison.Ordinal);
        Assert.Contains("+ - d", diff, StringComparison.Ordinal);
        Assert.Contains("  - a", diff, StringComparison.Ordinal);
    }
}
