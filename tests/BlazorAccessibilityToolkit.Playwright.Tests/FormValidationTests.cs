namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class FormValidationTests(BrowserFixture browser)
{
    private static readonly PatternOptions Fast = new() { Timeout = TimeSpan.FromMilliseconds(600) };

    // Errors linked with aria-describedby, focus moved to the first invalid field.
    private const string AccessibleForm = """
        <main><h1>Sign up</h1>
        <form id="f" novalidate>
          <label for="email">Email</label>
          <input id="email" name="email" autocomplete="email">
          <p id="email-error" hidden></p>
          <label for="name">Name</label>
          <input id="name" name="name" autocomplete="name">
          <p id="name-error" hidden></p>
          <button type="submit">Create account</button>
        </form></main>
        <script>
          document.getElementById('f').addEventListener('submit', e => {
            e.preventDefault();
            let first = null;
            for (const [id, message] of [['email', 'Enter your email address'], ['name', 'Enter your name']]) {
              const input = document.getElementById(id), error = document.getElementById(id + '-error');
              if (!input.value) {
                input.setAttribute('aria-invalid', 'true');
                input.setAttribute('aria-describedby', id + '-error');
                error.textContent = message; error.hidden = false;
                first = first || input;
              }
            }
            if (first) first.focus();
          });
        </script>
        """;

    // What a default Blazor EditForm with ValidationMessage renders: aria-invalid is set, the messages are not linked,
    // and focus stays on the submit button.
    private const string DefaultEditForm = """
        <main><h1>Sign up</h1>
        <form id="f" novalidate>
          <label for="email">Email</label>
          <input id="email" class="valid">
          <div class="validation-message" hidden></div>
          <button type="submit">Create account</button>
        </form></main>
        <script>
          document.getElementById('f').addEventListener('submit', e => {
            e.preventDefault();
            const input = document.getElementById('email');
            input.setAttribute('aria-invalid', 'true');
            input.className = 'invalid';
            const m = document.querySelector('.validation-message'); m.textContent = 'The Email field is required.'; m.hidden = false;
          });
        </script>
        """;

    [Fact]
    public async Task Form_that_links_errors_and_moves_focus_passes()
    {
        await using var session = await browser.PageAsync(AccessibleForm);
        var result = await AriaPatterns.VerifyFormValidationAsync(session.Page.Locator("#f"));
        Assert.True(result.Passed, result.Describe());
        Assert.Contains(result.Checks, c => c.Requirement.StartsWith("Submitting with errors", StringComparison.Ordinal) && c.Detail!.Contains("\"Email\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Default_edit_form_markup_fails_on_links_and_focus()
    {
        await using var session = await browser.PageAsync(DefaultEditForm);
        var result = await AriaPatterns.VerifyFormValidationAsync(session.Page.Locator("#f"), options: Fast);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement.StartsWith("Submitting with errors", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Each invalid field references", StringComparison.Ordinal) && c.Detail!.Contains("\"Email\"", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Focus moves", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Errors are conveyed", StringComparison.Ordinal));
        var failure = Assert.Throws<AccessibilityAssertionException>(() => result.ShouldPass());
        Assert.Contains("Form validation", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_summary_announced_in_a_live_region_is_accepted()
    {
        var html = DefaultEditForm
            .Replace("<div class=\"validation-message\" hidden></div>", "<div class=\"validation-message\" id=\"email-error\" hidden></div><div role=\"alert\" id=\"summary\"></div>", StringComparison.Ordinal)
            .Replace("m.hidden = false;", "m.hidden = false; input.setAttribute('aria-describedby', 'email-error'); document.getElementById('summary').textContent = 'There is a problem: The Email field is required.';", StringComparison.Ordinal);
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyFormValidationAsync(session.Page.Locator("#f"), options: Fast);

        Assert.Contains(result.Checks, c => c.Passed && c.Requirement.StartsWith("Errors are conveyed", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement.StartsWith("Each invalid field references", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Focus moves", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Browser_constraint_validation_is_accepted()
    {
        const string html = """
            <main><h1>x</h1><form id="f">
              <label for="q">Query</label><input id="q" required>
              <button>Search</button>
            </form></main>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyFormValidationAsync(session.Page.Locator("#f"), options: Fast);
        Assert.True(result.Passed, result.Describe());
        Assert.Contains(result.Checks, c => c.Requirement.Contains("browser constraint validation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Valid_form_is_reported_as_not_verified()
    {
        await using var session = await browser.PageAsync(AccessibleForm);
        await session.Page.FillAsync("#email", "ana@example.com");
        await session.Page.FillAsync("#name", "Ana");
        var result = await AriaPatterns.VerifyFormValidationAsync(session.Page.Locator("#f"), options: Fast);
        Assert.False(result.Passed);
        Assert.Contains("fill the form with invalid values", result.Describe(), StringComparison.Ordinal);
    }
}
