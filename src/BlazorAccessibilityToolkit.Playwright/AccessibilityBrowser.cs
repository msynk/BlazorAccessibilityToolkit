using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Browser launch settings.</summary>
public sealed record AccessibilityBrowserOptions
{
    /// <summary>Environment variable selecting the browser (<c>chromium</c>, <c>firefox</c> or <c>webkit</c>).</summary>
    public const string BrowserEnvironmentVariable = "BLAZOR_A11Y_BROWSER";

    /// <summary>Environment variable that runs the browser headed when set to <c>1</c> or <c>true</c>.</summary>
    public const string HeadedEnvironmentVariable = "BLAZOR_A11Y_HEADED";

    /// <summary>Browser to launch. Default: <see cref="BrowserEnvironmentVariable"/> or <c>chromium</c>.</summary>
    public string? Browser { get; init; }

    /// <summary>Run headless. Default: headless unless <see cref="HeadedEnvironmentVariable"/> is set.</summary>
    public bool? Headless { get; init; }

    /// <summary>Optional browser channel (for example <c>chrome</c> or <c>msedge</c>).</summary>
    public string? Channel { get; init; }

    /// <summary>Viewport width in CSS pixels. Default 1280.</summary>
    public int ViewportWidth { get; init; } = 1280;

    /// <summary>Viewport height in CSS pixels. Default 720.</summary>
    public int ViewportHeight { get; init; } = 720;

    /// <summary>Browser locale. Default <c>en-US</c>.</summary>
    public string Locale { get; init; } = "en-US";

    /// <summary>Emulate <c>prefers-reduced-motion: reduce</c>. Default <c>true</c> for deterministic rendering.</summary>
    public bool ReducedMotion { get; init; } = true;

    /// <summary>Launch timeout.</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(60);

    internal string ResolveBrowser() =>
        (Browser ?? Environment.GetEnvironmentVariable(BrowserEnvironmentVariable) ?? "chromium").Trim().ToLowerInvariant();

    internal bool ResolveHeadless()
    {
        if (Headless is { } headless)
        {
            return headless;
        }

        var headed = Environment.GetEnvironmentVariable(HeadedEnvironmentVariable);
        return !(headed == "1" || string.Equals(headed, "true", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Thrown when the requested browser cannot be launched (typically because it is not installed).</summary>
public sealed class BrowserNotAvailableException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public BrowserNotAvailableException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public BrowserNotAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public BrowserNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Owns a Playwright instance and one launched browser. Share one instance per test class or assembly fixture and
/// create an isolated <see cref="BrowserSession"/> (new browser context) per test, so no cookies, storage or focus
/// state leaks between tests.
/// </summary>
/// <remarks>
/// If the browser process exits (for example a browser crash under heavy load), the next
/// <see cref="NewSessionAsync"/> launches it again. Work that was running in the crashed browser fails with an
/// execution error; it is never reported as passed.
/// </remarks>
public sealed class AccessibilityBrowser : IAsyncDisposable
{
    private readonly AccessibilityBrowserOptions _options;
    private readonly IBrowserType _type;
    private readonly SemaphoreSlim _relaunch = new(1, 1);
    private int _disposed;

    private AccessibilityBrowser(IPlaywright playwright, IBrowserType type, IBrowser browser, AccessibilityBrowserOptions options)
    {
        Playwright = playwright;
        _type = type;
        Browser = browser;
        Name = type.Name;
        _options = options;
    }

    /// <summary>The Playwright instance.</summary>
    public IPlaywright Playwright { get; }

    /// <summary>The launched browser (replaced when it has to be relaunched).</summary>
    public IBrowser Browser { get; private set; }

    /// <summary>How many times the browser was relaunched after it disconnected.</summary>
    public int Relaunches { get; private set; }

    /// <summary>Browser name (<c>chromium</c>, <c>firefox</c> or <c>webkit</c>).</summary>
    public string Name { get; }

    /// <summary>Browser version.</summary>
    public string Version => Browser.Version;

    /// <summary>Launches a browser. Throws <see cref="BrowserNotAvailableException"/> with installation guidance when it is missing.</summary>
    public static async Task<AccessibilityBrowser> LaunchAsync(AccessibilityBrowserOptions? options = null)
    {
        options ??= new AccessibilityBrowserOptions();
        var name = options.ResolveBrowser();
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        try
        {
            var type = name switch
            {
                "chromium" or "chrome" or "msedge" => playwright.Chromium,
                "firefox" => playwright.Firefox,
                "webkit" => playwright.Webkit,
                _ => throw new ArgumentException($"Unknown browser '{name}'. Use chromium, firefox or webkit.", nameof(options)),
            };
            var browser = await type.LaunchAsync(LaunchOptions(options)).ConfigureAwait(false);
            return new AccessibilityBrowser(playwright, type, browser, options);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase))
        {
            playwright.Dispose();
            throw new BrowserNotAvailableException(
                $"The '{name}' browser is not installed for Microsoft.Playwright. Install it explicitly, for example with " +
                $"'pwsh bin/Debug/net10.0/playwright.ps1 install {name}' from the test project directory, or by calling " +
                $"AccessibilityBrowser.Install(\"{name}\") once (for example in CI setup). Original error: {ex.Message}", ex);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Installs browsers with the Playwright CLI bundled in Microsoft.Playwright (downloads only what is missing).
    /// Returns the CLI exit code. Call it explicitly from setup code or CI; the toolkit never installs browsers implicitly.
    /// </summary>
    public static int Install(params string[] browsers)
    {
        ArgumentNullException.ThrowIfNull(browsers);
        return Microsoft.Playwright.Program.Main(["install", .. (browsers.Length == 0 ? ["chromium"] : browsers)]);
    }

    /// <summary>Default context options (viewport, locale, reduced motion).</summary>
    public BrowserNewContextOptions CreateContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = _options.ViewportWidth, Height = _options.ViewportHeight },
        Locale = _options.Locale,
        ReducedMotion = _options.ReducedMotion ? Microsoft.Playwright.ReducedMotion.Reduce : Microsoft.Playwright.ReducedMotion.NoPreference,
    };

    /// <summary>Creates an isolated browser context with one page (relaunching the browser if it has exited).</summary>
    public async Task<BrowserSession> NewSessionAsync(BrowserNewContextOptions? contextOptions = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var browser = await EnsureConnectedAsync().ConfigureAwait(false);
        var context = await browser.NewContextAsync(contextOptions ?? CreateContextOptions()).ConfigureAwait(false);
        try
        {
            var page = await context.NewPageAsync().ConfigureAwait(false);
            return new BrowserSession(context, page);
        }
        catch
        {
            await context.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await Browser.CloseAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The browser process is already gone.
        }
        finally
        {
            Playwright.Dispose();
            _relaunch.Dispose();
        }
    }

    private static BrowserTypeLaunchOptions LaunchOptions(AccessibilityBrowserOptions options) => new()
    {
        Headless = options.ResolveHeadless(),
        Channel = options.Channel,
        Timeout = (float)options.LaunchTimeout.TotalMilliseconds,
    };

    private async Task<IBrowser> EnsureConnectedAsync()
    {
        var current = Browser;
        if (current.IsConnected)
        {
            return current;
        }

        await _relaunch.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Browser.IsConnected)
            {
                Browser = await _type.LaunchAsync(LaunchOptions(_options)).ConfigureAwait(false);
                Relaunches++;
            }

            return Browser;
        }
        finally
        {
            _relaunch.Release();
        }
    }
}

/// <summary>An isolated browser context and page. Disposing closes the context.</summary>
public sealed class BrowserSession : IAsyncDisposable
{
    internal BrowserSession(IBrowserContext context, IPage page)
    {
        Context = context;
        Page = page;
    }

    /// <summary>The browser context.</summary>
    public IBrowserContext Context { get; }

    /// <summary>The page.</summary>
    public IPage Page { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Context.CloseAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The browser may already be gone; nothing left to clean up.
        }
    }
}
