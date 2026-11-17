namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class TranscriptTests(BrowserFixture browser)
{
    private const string Settings = """
        <main>
          <section aria-labelledby="t">
            <h2 id="t">Settings</h2>
            <nav aria-label="Sections"><ul><li><a href="/g" aria-current="page">General</a></li><li><a href="/p">Privacy</a></li></ul></nav>
            <button type="button" aria-pressed="false" id="bold">Bold</button>
            <button type="button" aria-expanded="false" aria-controls="adv"><span aria-hidden="true">▸</span> Advanced</button>
            <label><input type="checkbox" checked> Email me updates</label>
            <label for="nick">Nickname</label>
            <input id="nick" aria-describedby="hint" aria-invalid="true" required>
            <p id="hint">Shown to other users.</p>
            <img src="data:image/gif;base64,R0lGODlhAQABAAAAACw=" alt="Your avatar">
            <span style="display:none">Hidden text</span>
          </section>
        </main>
        <script>document.getElementById('bold').addEventListener('click', e => e.target.setAttribute('aria-pressed', 'true'));</script>
        """;

    [Fact]
    public async Task Transcript_of_a_region_reads_roles_names_states_and_descriptions()
    {
        await using var session = await browser.PageAsync(Settings);
        var section = session.Page.Locator("section");

        await section.ShouldMatchTranscriptAsync("""
            region, Settings
            heading, Settings, level 2
            navigation, Sections
            list, 2 items
            link, General, current page
            link, Privacy
            button, Bold, not pressed
            button, Advanced, collapsed
            checkbox, Email me updates, checked
            Nickname
            textbox, Nickname, required, invalid, Shown to other users.
            Shown to other users.
            img, Your avatar
            """);

        await session.Page.ClickAsync("#bold");
        await section.ShouldMatchTranscriptAsync("""
            region, Settings
            heading, Settings, level 2
            navigation, Sections
            list, 2 items
            link, General, current page
            link, Privacy
            button, Bold, pressed
            button, Advanced, collapsed
            checkbox, Email me updates, checked
            Nickname
            textbox, Nickname, required, invalid, Shown to other users.
            Shown to other users.
            img, Your avatar
            """);
    }

    [Fact]
    public async Task Transcript_root_is_the_located_element_even_with_duplicate_ids()
    {
        await using var session = await browser.PageAsync("""
            <main><h1>x</h1>
              <div id="card"><button>First</button></div>
              <div id="card"><button>Second</button></div>
            </main>
            """);
        Assert.Equal("button, Second", await session.Page.Locator("#card >> nth=1").GetScreenReaderTranscriptAsync());
    }

    [Fact]
    public async Task Page_transcript_and_failure_diff()
    {
        await using var session = await browser.PageAsync(Settings);
        var transcript = await session.Page.GetScreenReaderTranscriptAsync();
        Assert.StartsWith("main\nregion, Settings", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden text", transcript, StringComparison.Ordinal);

        var tree = await session.Page.GetAccessibilityTreeAsync();
        Assert.Equal("main", Assert.Single(tree).Role);

        var failure = await Assert.ThrowsAsync<AccessibilityAssertionException>(() => session.Page.Locator("nav").ShouldMatchTranscriptAsync("navigation, Sections\nlink, General", TimeSpan.FromMilliseconds(200)));
        Assert.Contains("+ list, 2 items", failure.Message, StringComparison.Ordinal);
    }
}
