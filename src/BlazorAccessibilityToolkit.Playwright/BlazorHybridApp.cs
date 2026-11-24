using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for <see cref="BlazorHybridApp.LaunchAsync"/>.</summary>
public sealed record BlazorHybridAppOptions
{
    /// <summary>Command-line arguments for the app.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Working directory of the app. Default: the folder of the executable.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Environment variables for the app, in addition to the test process's.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The remote debugging port for WebView2. Default 0: a free port. Only processes on this computer can connect
    /// (WebView2 listens on 127.0.0.1).
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// A WebView2 user data folder for this run (<c>WEBVIEW2_USER_DATA_FOLDER</c>), so that runs do not share cookies,
    /// storage or a browser process with each other or with the app outside tests. Default: a new temporary folder,
    /// deleted afterwards.
    /// </summary>
    public string? UserDataFolder { get; init; }

    /// <summary>How long to wait for the app to show its BlazorWebView. Default 60 seconds.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Selects the page to audit when the app has several WebViews: the first page whose URL starts with this.
    /// Default: the first page (BlazorWebView serves its content from <c>https://0.0.0.1/</c>, <c>0.0.0.0</c> before .NET 10).
    /// </summary>
    public string? PageUrlPrefix { get; init; }
}

/// <summary>
/// A Blazor Hybrid app (BlazorWebView in WPF, Windows Forms or .NET MAUI on Windows) whose WebView2 content is
/// connected to Playwright through the Chrome DevTools Protocol. <see cref="Page"/> is the app's Blazor page, so every
/// audit, keyboard scenario and focus check of the toolkit runs on it as on a page in a browser. The native window
/// around the WebView (title bar, menus, native controls) is not part of the page: check it with a UI Automation tool
/// such as Accessibility Insights for Windows.
/// </summary>
public sealed class BlazorHybridApp : IAsyncDisposable
{
    /// <summary>WebView2 reads extra browser arguments from this environment variable unless the app sets its own.</summary>
    public const string AdditionalArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    private readonly Process? _process;
    private readonly string? _ownedUserDataFolder;

    private BlazorHybridApp(IPlaywright playwright, IBrowser browser, IPage page, Process? process, string? ownedUserDataFolder)
    {
        Playwright = playwright;
        Browser = browser;
        Page = page;
        _process = process;
        _ownedUserDataFolder = ownedUserDataFolder;
    }

    /// <summary>The Playwright instance.</summary>
    public IPlaywright Playwright { get; }

    /// <summary>The WebView2 browser, connected over the Chrome DevTools Protocol.</summary>
    public IBrowser Browser { get; }

    /// <summary>The app's Blazor page.</summary>
    public IPage Page { get; }

    /// <summary>The app's process, when <see cref="LaunchAsync"/> started it.</summary>
    public Process? Process => _process;

    /// <summary>
    /// Starts a Blazor Hybrid app with WebView2 remote debugging turned on, waits until its BlazorWebView has rendered,
    /// and connects to it. WebView2 is Windows only. The app must not set
    /// <c>CoreWebView2EnvironmentOptions.AdditionalBrowserArguments</c> itself (it would replace the debugging port).
    /// Disposing the result closes the app.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    /// <exception cref="TimeoutException">The app did not open a WebView within <see cref="BlazorHybridAppOptions.StartupTimeout"/>.</exception>
    public static async Task<BlazorHybridApp> LaunchAsync(string executablePath, BlazorHybridAppOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Blazor Hybrid apps are connected through WebView2, which runs on Windows only.");
        }

        options ??= new BlazorHybridAppOptions();
        var path = Path.GetFullPath(executablePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Blazor Hybrid app was not found.", path);
        }

        var port = options.Port > 0 ? options.Port : FreePort();
        var ownedFolder = options.UserDataFolder is null ? Path.Combine(Path.GetTempPath(), "blazor-a11y-webview2-" + Guid.NewGuid().ToString("N")) : null;
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(path)!,
        };
        foreach (var argument in options.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in options.EnvironmentVariables)
        {
            start.Environment[name] = value;
        }

        start.Environment[AdditionalArgumentsVariable] = string.Create(CultureInfo.InvariantCulture, $"--remote-debugging-port={port}");
        start.Environment["WEBVIEW2_USER_DATA_FOLDER"] = options.UserDataFolder ?? ownedFolder!;

        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{path}'.");
        try
        {
            var endpoint = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}"));
            await WaitForEndpointAsync(endpoint, process, options.StartupTimeout).ConfigureAwait(false);
            var app = await ConnectCoreAsync(endpoint, options, process, ownedFolder).ConfigureAwait(false);
            return app;
        }
        catch
        {
            Stop(process);
            DeleteFolder(ownedFolder);
            throw;
        }
    }

    /// <summary>
    /// Connects to a Blazor Hybrid app that is already running with WebView2 remote debugging turned on, for example one
    /// started with the environment variable <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222</c>.
    /// Disposing the result disconnects without closing the app.
    /// </summary>
    public static Task<BlazorHybridApp> ConnectAsync(Uri endpoint, BlazorHybridAppOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return ConnectCoreAsync(endpoint, options ?? new BlazorHybridAppOptions(), null, null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Browser.CloseAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The app may already be gone.
        }

        Playwright.Dispose();
        if (_process is not null)
        {
            Stop(_process);
            _process.Dispose();
        }

        DeleteFolder(_ownedUserDataFolder);
    }

    private static async Task<BlazorHybridApp> ConnectCoreAsync(Uri endpoint, BlazorHybridAppOptions options, Process? process, string? ownedFolder)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        try
        {
            var browser = await playwright.Chromium.ConnectOverCDPAsync(endpoint.ToString(), new BrowserTypeConnectOverCDPOptions { Timeout = (float)options.StartupTimeout.TotalMilliseconds }).ConfigureAwait(false);
            var deadline = DateTime.UtcNow + options.StartupTimeout;
            while (true)
            {
                var page = browser.Contexts.SelectMany(c => c.Pages)
                    .FirstOrDefault(p => options.PageUrlPrefix is null ? !p.Url.StartsWith("about:", StringComparison.Ordinal) : p.Url.StartsWith(options.PageUrlPrefix, StringComparison.OrdinalIgnoreCase));
                if (page is not null)
                {
                    // Blazor has rendered when the WebView's root component has content.
                    await page.WaitForFunctionAsync(
                        "() => document.readyState === 'complete' && !!window.Blazor && document.body && document.body.innerText.trim().length > 0",
                        null,
                        new PageWaitForFunctionOptions { Timeout = (float)Math.Max(1000, (deadline - DateTime.UtcNow).TotalMilliseconds), PollingInterval = 100 }).ConfigureAwait(false);
                    return new BlazorHybridApp(playwright, browser, page, process, ownedFolder);
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"No WebView page{(options.PageUrlPrefix is null ? string.Empty : $" starting with '{options.PageUrlPrefix}'")} appeared at {endpoint}.");
                }

                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static async Task WaitForEndpointAsync(Uri endpoint, Process process, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The app exited with code {process.ExitCode} before its WebView started."));
            }

            try
            {
                using var response = await http.GetAsync(new Uri(endpoint, "/json/version")).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new TimeoutException($"The app did not open WebView2 remote debugging at {endpoint} within {timeout.TotalSeconds:0} seconds. Check that it shows a BlazorWebView and does not set AdditionalBrowserArguments itself.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited.
        }
    }

    private static void DeleteFolder(string? folder)
    {
        if (folder is null)
        {
            return;
        }

        // WebView2 processes may hold files for a moment after the app exits.
        for (var attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }
    }
}
