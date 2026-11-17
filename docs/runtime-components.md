# Runtime components

`BlazorAccessibilityToolkit.Components` makes the accessible path the easy one in your app, as Angular CDK's a11y module and React Aria do for their frameworks. The other packages test accessibility; this one belongs in the app itself.

```xml
<PackageReference Include="BlazorAccessibilityToolkit.Components" Version="0.1.0-preview.1" />
```

```csharp
builder.Services.AddAccessibilityComponents();   // ILiveAnnouncer
```

`SkipLink` ships scoped CSS, which the app's `<link rel="stylesheet" href="@Assets["MyApp.styles.css"]" />` bundle includes automatically. The JavaScript module loads on first use from `_content/BlazorAccessibilityToolkit.Components/accessibility.js`.

| Component | What it solves | Render mode |
| --- | --- | --- |
| [`LiveAnnouncer`](#liveannouncer-and-iliveannouncer) and `ILiveAnnouncer` | Status messages that screen readers announce, including repeated ones | interactive |
| [`SkipLink`](#skiplink) | A skip link that works with Blazor's `<base href="/">` and moves focus | static and interactive |
| [`FocusTrap`](#focustrap) | Focus kept inside dialogs and drawers, then restored | interactive |
| [`RouteAnnouncer`](#routeannouncer) | Client-side navigation announced to screen readers | interactive |
| [`VisuallyHidden`](#visuallyhidden) | Text for screen readers only | any |

## LiveAnnouncer and ILiveAnnouncer

Render the host once, in the main layout, and announce from anywhere:

```razor
@* MainLayout.razor *@
<LiveAnnouncer />
```

```razor
@inject ILiveAnnouncer Announcer

<button type="button" @onclick="SaveAsync">Save</button>

@code {
    private async Task SaveAsync()
    {
        await repository.SaveAsync(model);
        await Announcer.AnnounceAsync("Changes saved");
        // Errors and time-critical warnings only:
        // await Announcer.AnnounceAsync("Connection lost", Politeness.Assertive);
    }
}
```

It solves the problems that make hand-written live regions fail:

- **Regions exist before the message.** A live region inserted together with its text is often not announced. The host renders two empty regions on the first render, one polite and one assertive.
- **Repeated messages are announced again.** Screen readers ignore a region whose text did not change. The host empties the region and inserts the message again after `RepeatDelay` (150 ms).
- **Old messages do not linger.** Messages are cleared after `ClearAfter` (7 seconds), so they are not found later when someone reads the page.
- **Messages are not lost silently.** `AnnounceAsync` returns `false` when no host is rendered, for example during prerendering or in static rendering, and `IsAttached` tells you in advance. Rendering a second host throws, because every message would be announced twice.

Test it with the toolkit's live-region monitor:

```csharp
var monitor = await component.MonitorLiveRegionsAsync();
await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
await monitor.ExpectUpdateAsync("Changes saved", politeness: "polite");
```

## SkipLink

```razor
<SkipLink />                                       @* "Skip to main content", target id "main-content" *@
<SkipLink Target="results">Skip to results</SkipLink>
...
<main id="main-content">...</main>
```

- The link is hidden until it receives focus. It then appears in the top left corner with a visible focus indicator, using system colors so that it works in forced colors mode.
- **It works with `<base href="/">`.** In Blazor apps, `<a href="#main">` resolves against the base URL and opens the home page from every other page. Interactive pages only avoid this when a click handler cancels the navigation, and not while they are prerendered. `SkipLink` renders the current path with the fragment, so it works before and after the page becomes interactive. The analyzer rule [`BA11Y038`](analyzers.md#ba11y038) and the browser rule `blazor-fragment-link-leaves-page` find bare fragment links.
- **It moves focus.** Blazor's router only scrolls to a fragment. When the page is interactive, `SkipLink` moves keyboard focus to the target, adding `tabindex="-1"` if the target cannot receive focus otherwise. Without interactivity, the browser's own fragment navigation applies.

## FocusTrap

```razor
@if (filtersOpen)
{
    <FocusTrap role="dialog" aria-modal="true" aria-labelledby="filters-title" InitialFocus="#first-filter">
        <h2 id="filters-title">Filters</h2>
        ...
        <button type="button" @onclick="() => filtersOpen = false">Close</button>
    </FocusTrap>
}
```

- While `Active` (default `true`), Tab and Shift+Tab wrap inside the content. Focus that leaves it, for example through a click elsewhere, is brought back.
- When the trap starts, focus moves to `InitialFocus` or to the first focusable element.
- When the trap is released (`Active="false"` or the component is removed), focus returns to the element that had it before, unless `RestoreFocus="false"`.
- Traps nest: only the innermost active trap handles keys.
- Escape handling and the dialog semantics (`role`, `aria-modal`, a name) are yours to add. Verify the result with `AriaPatterns.VerifyModalDialogAsync`.

## RouteAnnouncer

```razor
@* MainLayout.razor *@
<RouteAnnouncer />                                 @* "Navigated to {0}" *@
<RouteAnnouncer MessageFormat="Seite: {0}" />       @* localized *@
```

After each client-side navigation it announces the page title (from `<PageTitle>`), or the first `h1`, or the path, through a polite live region. This is what the route announcers of Next.js and SvelteKit do.

Use it **or** `<FocusOnNavigate>`, not both. `FocusOnNavigate` moves focus to the new page's heading, and screen readers read the heading. `RouteAnnouncer` suits apps where focus should stay where it is, for example a persistent search field. Verify either with `NavigationChecks.VerifyNavigationAsync`.

## VisuallyHidden

```razor
<a href="/orders/42">Details<VisuallyHidden> of order 42</VisuallyHidden></a>
<button type="button"><svg aria-hidden="true" ...></svg><VisuallyHidden>Close</VisuallyHidden></button>
```

The content is clipped to a 1 pixel box, so it stays in the accessibility tree; `display: none` would remove it. `VisuallyHidden.Style` is available for your own elements.
