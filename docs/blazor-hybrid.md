# Blazor Hybrid apps

A Blazor Hybrid app renders its Razor components in a `BlazorWebView` inside a native window: WPF, Windows Forms or .NET MAUI. On Windows that web view is WebView2, which is Chromium. Playwright can connect to it through the Chrome DevTools Protocol, and then the app's content is a page like any other. Audits, keyboard scenarios, focus order, focus indicators, live regions and ARIA snapshots all work on it unchanged.

```csharp
await using var app = await BlazorHybridApp.LaunchAsync(@"bin\Release\net10.0-windows\Orders.exe");
await app.Page.GetByRole(AriaRole.Heading, new() { Name = "Orders" }).WaitForAsync();

var audit = await app.Page.AuditAccessibilityAsync(label: new AuditLabel { Name = "orders (hybrid)" });
audit.ShouldHaveNoViolations();

await KeyboardScenario.For(app.Page).StartFromDocument().PressTab().ExpectFocused("button", "Add order").RunAsync();
```

`LaunchAsync` starts the app with WebView2 remote debugging turned on, waits until the BlazorWebView has rendered, and connects. Disposing it closes the app.

- **Remote debugging:** it sets the environment variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<free port>` for the app's process. The app must not set `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments` itself, which would replace it.
- **Isolation:** each run gets its own WebView2 user data folder (`WEBVIEW2_USER_DATA_FOLDER`), deleted afterwards. Runs then share no cookies, storage or browser process with each other or with the app you use outside tests. Set `BlazorHybridAppOptions.UserDataFolder` to keep one.
- **Several web views:** `Page` is the first page. `BlazorHybridAppOptions.PageUrlPrefix` picks another; BlazorWebView serves its content from `https://0.0.0.1/`, or `https://0.0.0.0/` before .NET 10.
- **Running app:** `BlazorHybridApp.ConnectAsync(new Uri("http://127.0.0.1:9222"))` connects to an app you started yourself with that environment variable. Disposing it disconnects and leaves the app running.

## What it covers

Everything inside the web view: the Razor components, their HTML, styles, focus and live regions. The audits are the same as for a Blazor Server or WebAssembly app, because the content is the same.

It does not cover the native window around the web view: the title bar, native menus and dialogs, and controls outside the BlazorWebView. Those are exposed through Windows UI Automation, not the DOM. Check them with [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/overview/) or Axe.Windows.

## Platforms

- **Windows** (WPF, Windows Forms, .NET MAUI on Windows): supported. WebView2 ships with Windows 11 and recent Windows 10. Install the Evergreen runtime elsewhere.
- **macOS, iOS and Android** (.NET MAUI): not supported. Those web views (WKWebView and Android WebView) cannot be automated with Playwright. Test the same components with the [component audits](component-testing.md) or in a Blazor web host.

## Security

The debugging port gives full control of the web view to any process on the same computer that connects to it. WebView2 listens on 127.0.0.1 only, and the toolkit turns the port on only for the process it starts. Never ship an app with remote debugging enabled.

## In CI

The toolkit's own hybrid tests run on a `windows-latest` GitHub Actions runner, which has the WebView2 runtime. They build a Windows Forms test app (`tests/Hybrid`), start it and audit it. A Linux job builds the app too (`EnableWindowsTargeting`) and skips the tests.
