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

The native window around the web view (the title bar, native menus and dialogs, and controls outside the BlazorWebView) is not part of the page. Windows exposes it through UI Automation instead; see the next section.

## The native window

`BlazorAccessibilityToolkit.Windows` audits the native part with [Axe.Windows](https://github.com/microsoft/axe-windows), the engine of Accessibility Insights for Windows:

```xml
<PackageReference Include="BlazorAccessibilityToolkit.Windows" Version="0.1.0-preview.1" />
```

```csharp
await using var app = await BlazorHybridApp.LaunchAsync(@"bin\Release\net10.0-windows\Orders.exe");

var web = await app.Page.AuditAccessibilityAsync();       // the Razor components, in the web view
var native = await app.AuditNativeWindowAsync();          // the window and native controls around it
native.ShouldHaveNoViolations();
```

- The audit scans every top-level window of the app's process. Each Axe.Windows error becomes a violation with the engine `axe-windows` and the Axe.Windows rule id, such as `NameNotNull` for a native button without a name. The finding gives the element's path, for example `window "Orders" > button #refreshButton`, with its control type, automation id and framework. Axe.Windows groups its rules by the criteria they serve, and the findings carry the matching WCAG success criterion: 1.3.1, 2.1.1 or 4.1.2.
- Findings inside WebView2 are skipped and counted in a warning. The web content is audited in the browser, which knows the DOM, and WebView2's internal views are not the app's to fix. `NativeAuditOptions.IncludeWebContent` includes them.
- `DisabledRules` in `NativeAuditOptions.AuditOptions` skips Axe.Windows rules, and its results are saved and reported like any other audit.
- For an app you connected to with `ConnectAsync`, pass its process id to `NativeAudits.AuditProcessAsync(processId)`. The method works for any Windows desktop app, not only Blazor Hybrid apps.
- A scan that fails, for example because the process has no window, fails the audit; it never passes it.

The package depends on Axe.Windows 2.4.2 (MIT), and through it on `System.Drawing.Common`, which is why it is separate from the cross-platform Playwright package. Automated checks find only part of the problems in a native window: also tab through it, and use Accessibility Insights for Windows' FastPass and assessment.

## Platforms

- **Windows** (WPF, Windows Forms, .NET MAUI on Windows): supported. WebView2 ships with Windows 11 and recent Windows 10. Install the Evergreen runtime elsewhere.
- **macOS, iOS and Android** (.NET MAUI): not supported. Those web views (WKWebView and Android WebView) cannot be automated with Playwright. Test the same components with the [component audits](component-testing.md) or in a Blazor web host.

## Security

The debugging port gives full control of the web view to any process on the same computer that connects to it. WebView2 listens on 127.0.0.1 only, and the toolkit turns the port on only for the process it starts. Never ship an app with remote debugging enabled.

## In CI

The toolkit's own hybrid tests run on a `windows-latest` GitHub Actions runner, which has the WebView2 runtime. They build a Windows Forms test app (`tests/Hybrid`), start it, and audit both its web view and its native window. A Linux job builds the app too (`EnableWindowsTargeting`) and skips the tests.
