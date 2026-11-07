using System.Net;
using System.Text;
using System.Text.Json;
using BlazorAccessibilityToolkit.Engines;
using BlazorAccessibilityToolkit.Playwright.Internal;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>
/// Audits static component markup (for example bUnit output) in a real browser engine with axe-core and the toolkit's
/// browser diagnostics.
/// </summary>
/// <remarks>
/// The markup is loaded as a static document: Blazor event handlers, JavaScript interop and component CSS that was not
/// supplied through <see cref="MarkupDocumentOptions"/> are absent. Use it for structure, names, roles and ARIA; use the
/// interactive component host for keyboard, focus and dynamic behavior, and supply stylesheets for meaningful
/// color-contrast results.
/// <para>
/// Pages are pooled: each concurrent audit borrows its own page in its own browser context, and every audit replaces
/// the entire document, so no markup, styles or focus state carry over. A page that fails is discarded rather than
/// reused. Set <see cref="ReusePages"/> to <c>false</c> to create a fresh context for every audit.
/// </para>
/// </remarks>
public sealed class PlaywrightMarkupAuditEngine : IMarkupAuditEngine, IAsyncDisposable
{
    private readonly AccessibilityBrowser _browser;
    private readonly System.Collections.Concurrent.ConcurrentBag<BrowserSession> _pool = [];
    private int _disposed;

    /// <summary>Creates the engine on a shared browser.</summary>
    public PlaywrightMarkupAuditEngine(AccessibilityBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        _browser = browser;
    }

    /// <summary>Reuse pooled pages between audits (each audit replaces the whole document). Default <c>true</c>.</summary>
    public bool ReusePages { get; init; } = true;

    /// <inheritdoc />
    public string Name => "playwright:" + _browser.Name;

    /// <inheritdoc />
    public async Task<MarkupEngineResult> AuditAsync(MarkupAuditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ExecutionError>();
        var output = new EngineOutput();
        BrowserSession? session = null;
        var reusable = false;
        try
        {
            var (head, body) = await BuildDocumentAsync(request, cancellationToken).ConfigureAwait(false);
            var pooled = ReusePages && _pool.TryTake(out var taken) && !taken.Page.IsClosed ? taken : null;
            session = pooled ?? await _browser.NewSessionAsync().ConfigureAwait(false);
            await RunAsync(session.Page, pooled is not null, request.Document.Language, head, body, request, output).WaitAsync(request.Options.Timeout, cancellationToken).ConfigureAwait(false);
            reusable = ReusePages;
        }
        catch (TimeoutException ex)
        {
            errors.Add(new ExecutionError { Source = Name, Message = $"The markup audit did not finish within {request.Options.Timeout.TotalSeconds:0.#} s ({ex.Message}).", ExceptionType = ex.GetType().FullName });
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException or IOException or OperationCanceledException)
        {
            errors.Add(ExecutionError.FromException(Name, ex));
        }
        finally
        {
            if (session is not null)
            {
                if (reusable && Volatile.Read(ref _disposed) == 0)
                {
                    _pool.Add(session);
                }
                else
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        return new MarkupEngineResult
        {
            Findings = output.Findings,
            PassedRules = output.Passed,
            InapplicableRules = output.Inapplicable,
            Engines = output.Engines,
            Warnings = output.Warnings,
            Errors = errors,
            Browser = _browser.Name,
            BrowserVersion = _browser.Version,
        };
    }

    /// <summary>Closes pooled pages.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (_pool.TryTake(out var session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task RunAsync(IPage page, bool reused, string language, string head, string body, MarkupAuditRequest request, EngineOutput output)
    {
        if (reused)
        {
            // Replace the whole document in place; scripts already loaded in the window (axe-core, toolkit) stay available.
            await page.EvaluateAsync(
                "a => { document.documentElement.setAttribute('lang', a.lang); document.documentElement.innerHTML = a.inner; window.scrollTo(0, 0); return true; }",
                new { lang = language, inner = head + body }).ConfigureAwait(false);
        }
        else
        {
            await page.SetContentAsync($"<!DOCTYPE html><html lang=\"{WebUtility.HtmlEncode(language)}\">{head}{body}</html>", new PageSetContentOptions { WaitUntil = WaitUntilState.Load }).ConfigureAwait(false);
        }

        await ToolkitScript.EnsureAsync(page, includeAxe: false).ConfigureAwait(false);
        var adopted = await page.EvaluateAsync<int>("() => window.__blazorA11y.adoptOrdinals()").ConfigureAwait(false);
        if (adopted == 0)
        {
            output.Warnings.Add("The markup contains no elements; nothing was audited.");
        }

        var (engines, _) = await PlaywrightAccessibility.RunEnginesAsync(page, null, request.Options, request.Kind, request.Document.WrapperElement is null ? null : "body > " + request.Document.WrapperElement).ConfigureAwait(false);
        output.Findings.AddRange(engines.Findings);
        output.Passed.AddRange(engines.Passed);
        output.Inapplicable.AddRange(engines.Inapplicable);
        output.Engines.AddRange(engines.Engines);
        output.Warnings.AddRange(engines.Warnings);
    }

    private static async Task<(string Head, string Body)> BuildDocumentAsync(MarkupAuditRequest request, CancellationToken cancellationToken)
    {
        var document = request.Document;
        if (document.WrapperElement is { } wrapperName && !wrapperName.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new InvalidOperationException($"Invalid wrapper element name '{wrapperName}'. Use a lower-case tag name such as 'main'.");
        }

        var sb = new StringBuilder();
        sb.Append("<head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(document.Title)).Append("</title>");
        foreach (var file in document.StylesheetFiles)
        {
            var css = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            sb.Append("<style>").Append(css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase)).Append("</style>");
        }

        foreach (var css in document.Styles)
        {
            sb.Append("<style>").Append(css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase)).Append("</style>");
        }

        sb.Append("</head>");
        var head = sb.ToString();
        sb.Clear().Append("<body>");
        if (document.WrapperElement is { } wrapper)
        {
            sb.Append('<').Append(wrapper).Append('>').Append(request.BodyHtml).Append("</").Append(wrapper).Append('>');
        }
        else
        {
            sb.Append(request.BodyHtml);
        }

        sb.Append("</body>");
        return (head, sb.ToString());
    }
}
