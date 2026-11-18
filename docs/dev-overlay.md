# Development overlay

`BlazorAccessibilityToolkit.DevTools` checks your app while you work on it, as `@axe-core/react` and `vue-axe` do for React and Vue. axe-core runs in the browser each time the page changes. A small button in a corner of the page shows how many elements fail. It opens a panel that lists the violations by rule, and new violations are also written to the browser console.

The overlay is a development aid. Tests and the CI gate remain the place to enforce accessibility; see [Getting started](getting-started.md).

## Set up

```xml
<PackageReference Include="BlazorAccessibilityToolkit.DevTools" Version="0.1.0-preview.1" />
```

Map the endpoints that serve the overlay script and axe-core, in development only:

```csharp
// Program.cs
using BlazorAccessibilityToolkit.DevTools;

if (app.Environment.IsDevelopment())
{
    app.MapAccessibilityDevTools();
}
```

Add the component to `App.razor`, after the Blazor script:

```razor
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <BlazorAccessibilityToolkit.DevTools.AccessibilityDevOverlay />
</body>
```

The component renders a module script, and only in the Development environment. Its URL is relative, so the page needs the `<base href="/" />` element that the Blazor templates include. Keep it in `App.razor`: browsers run a script element when the page loads, so the component must be rendered statically, not inside an interactive component. It works with every render mode, because `App.razor` is rendered on the server. The script stays loaded across enhanced and interactive navigation, and it notices the new content.

In a standalone Blazor WebAssembly app there is no server to map the endpoints. Add the script to `wwwroot/index.html` and point it at a copy of axe-core:

```html
<script type="module" src="lib/blazor-a11y/devtools.js" data-ba11y-devtools data-ba11y-axe-src="lib/axe-core/axe.min.js"></script>
```

Copy `devtools.js` from the toolkit's repository (`src/BlazorAccessibilityToolkit.DevTools/Scripts/devtools.js`) and `axe.min.js` from the `axe-core` npm package. Nothing in this setup checks the environment, so keep the tag and both files out of production builds yourself, for example by removing them in a publish step.

## Use it

- The button reads "3 a11y issues" (or "No a11y issues") and opens the panel. Escape closes the panel and returns focus to the button. When the number changes, a status message tells screen reader users.
- For each rule the panel shows the impact, the WCAG success criteria, a link to the rule's help page and the failing elements. **Show** scrolls an element into view and outlines it, and also logs the element to the console, where you can inspect it.
- **Check again** runs axe-core immediately. Otherwise a check runs when the page has not changed for one second.
- The console lists each new violation once, grouped by rule, and notes when earlier violations are gone.
- `window.blazorA11yDevTools` exposes `run()`, `open()`, `close()`, `status` and the latest axe-core `results` for the console.

The overlay itself is built to be accessible. It is a labeled `complementary` landmark, it works with the keyboard, it meets the contrast requirements in light and dark color schemes, and it supports forced colors. Its own markup lives in a shadow root and is never checked by its own runs.

## Settings

| Parameter | Default | Purpose |
| --- | --- | --- |
| `Enabled` | Development only | `false` turns the overlay off, `true` renders it in any environment. In other environments, map the endpoints there too, and protect them (see below). |
| `Tags` | WCAG 2.0 to 2.2 A and AA, and best practices | The axe-core tags to check, for example `["wcag2a", "wcag2aa"]`. |
| `Exclude` | none | CSS selectors of content not to check, such as a third-party chat widget. |
| `Position` | `BottomEnd` | The corner for the button and the panel. Start and end follow the writing direction. |
| `Delay` | 1 second | How long the page must stay unchanged before a check. |
| `LogToConsole` | `true` | Whether new violations are written to the console. |
| `ScriptUrl`, `AxeScriptUrl` | `_blazor-a11y/devtools.js`, `axe.js` next to it | Where the scripts come from, relative to the page's base address. |

```razor
<AccessibilityDevOverlay Exclude="@(["#chat-widget"])" Position="DevOverlayPosition.BottomStart" />
```

## Outside development

The overlay runs axe-core for everyone who loads the page, and `window.blazorA11yDevTools.results` contains the HTML of failing elements. On a shared test or staging server, show it only to your team: `MapAccessibilityDevTools()` returns the route group, so you can require authorization for the scripts, and render the component only for those users.

```csharp
app.MapAccessibilityDevTools().RequireAuthorization("Testers");
```

```razor
<AuthorizeView Policy="Testers">
    <AccessibilityDevOverlay Enabled="true" />
</AuthorizeView>
```

Do not map the endpoints in production.

## Turning it off

- Under browser automation (`navigator.webdriver`, which Playwright and Selenium set), the overlay stays off. A check in the page would otherwise run axe-core at the same time as a test's own audit, and its button would appear in the test's results.
- In the browser console, `localStorage.setItem('blazor-a11y-devtools', 'off')` turns it off for that site and browser. `'on'` turns it on even under automation, which is how the toolkit tests the overlay. `localStorage.removeItem('blazor-a11y-devtools')` goes back to the default.
- `Enabled="false"` turns it off for everybody.

## What it does not do

- It runs axe-core only. The toolkit's Razor-aware diagnostics, keyboard scenarios and pattern checks need a test that drives the page; see [Browser and interactive testing](interactive-testing.md).
- It does not name the Razor component that rendered an element. Component attribution comes from the render tree, which only the bUnit and test-host audits see.
- It checks the elements on the page at the time. Content behind a closed menu or a later step is checked when it appears.
- axe-core finds only some problems, as with any automated check. "No a11y issues" does not mean the page conforms.
