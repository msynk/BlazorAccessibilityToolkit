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
| [`NavigationFocus`](#navigationfocus) | Focus moved to the new page after navigation, without taking it away from users while a prerendered page becomes interactive | interactive |
| [`ActiveDescendantGroup`](#activedescendantgroup) | `aria-activedescendant` navigation for listboxes and comboboxes that keep focus on one element | interactive |
| [`RovingFocusGroup`](#rovingfocusgroup) | One tab stop and arrow-key navigation for toolbars, tabs, radio groups, listboxes and menus | interactive |
| [`RouteAnnouncer`](#routeannouncer) | Client-side navigation announced to screen readers | static (enhanced navigation) and interactive |
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
- **Messages are not lost silently.** `AnnounceAsync` returns `false` when no host is rendered, for example during prerendering or in static rendering, and `IsAttached` tells you in advance. Render one host per app. If a layout change renders a new host before the old one is gone, the newest host receives the messages, so nothing is announced twice.

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

## RovingFocusGroup

```razor
<RovingFocusGroup aria-label="Formatting">                       @* role="toolbar", ArrowLeft/ArrowRight *@
    <button type="button" aria-pressed="@Pressed(bold)" @onclick="ToggleBold">Bold</button>
    <button type="button">Italic</button>
</RovingFocusGroup>

<RovingFocusGroup Role="radiogroup" Orientation="RovingOrientation.Both" SelectionFollowsFocus="true" aria-label="Size">
    @foreach (var size in sizes)
    {
        <div role="radio" aria-checked="@(size == selected ? "true" : "false")" @onclick="() => selected = size">@size</div>
    }
</RovingFocusGroup>
```

It implements the roving tabindex of the WAI-ARIA Authoring Practices, like Angular CDK's `FocusKeyManager`:

- The group is a single tab stop. That stop belongs to the selected, checked, pressed or current item, otherwise to the first item, and it follows focus.
- Arrow keys move focus (`Horizontal`, `Vertical` or `Both`) and wrap unless `Wrap="false"`. Home and End go to the first and last item. Arrow keys do not scroll the page.
- `SelectionFollowsFocus` clicks the item that receives focus, for tabs with automatic activation and radio groups.
- Items that Blazor adds, removes or re-renders are picked up. Hidden, `inert`, disabled and `aria-disabled` items are skipped.
- `ItemSelector` chooses the items. By default these are elements with an item role (radio, tab, option, menuitem, treeitem), or buttons and links.
- `Typeahead` moves focus to the item that starts with the letters typed within half a second. A repeated letter cycles through the items that start with it.

Check the result with `AriaPatterns.VerifyToolbarAsync`, `VerifyRadioGroupAsync`, `VerifyTabsAsync` or `VerifyListboxAsync`.

## ActiveDescendantGroup

```razor
<label for="fruit">Fruit</label>
<input id="fruit" role="combobox" aria-autocomplete="list" aria-expanded="@(open ? "true" : "false")"
       aria-controls="fruit-list" value="@value" @onkeydown="OpenOnArrowDown" />
<ActiveDescendantGroup id="fruit-list" OwnerId="fruit" aria-label="Fruits" hidden="@(!open)">
    @foreach (var fruit in fruits)
    {
        <div role="option" aria-selected="false" @onclick="() => Choose(fruit)">@fruit</div>
    }
</ActiveDescendantGroup>

<ActiveDescendantGroup aria-label="Colors">                      @* a focusable listbox *@
    <div role="option" aria-selected="false">Red</div> ...
</ActiveDescendantGroup>
```

DOM focus stays on one element, the owner: the group itself, or the element with `OwnerId`, such as a combobox input. Arrow keys move the active item, which gets an id if it has none and is referenced with `aria-activedescendant`. The active item is marked with `data-active` (style it) and, with `SelectionFollowsFocus` (the default), `aria-selected="true"`. Enter clicks the active item. When the group has focus itself, Home, End and type-to-search work too. While the popup is collapsed (no visible items), keys go to your own handlers, for example to open it with ArrowDown. When the active item disappears, `aria-activedescendant` is removed. Verify with `AriaPatterns.VerifyComboboxAsync` and `VerifyListboxAsync`.

## NavigationFocus

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)" />
        <NavigationFocus RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

A replacement for Blazor's `<FocusOnNavigate>` that fixes a problem with prerendering. A keyboard user can tab into a prerendered page before it becomes interactive. Then two things go wrong: Blazor replaces the prerendered DOM, so the focused element disappears and focus falls back to the body; and `FocusOnNavigate` runs on the router's first interactive render and moves focus to the heading. Either way, the user is sent back to the top of the page.

`NavigationFocus` renders a small script that remembers what the user focuses while the page is prerendered. When the page becomes interactive, it restores focus to the same element in the new DOM. After every later navigation it moves focus to `Selector`, as `FocusOnNavigate` does, and on the first load it does so only when the user had not focused anything. `NavigationChecks.VerifyFocusSurvivesInteractivityAsync` tests this behavior, and the analyzer rule `BA11Y032` accepts either component.

## RouteAnnouncer

```razor
@* MainLayout.razor *@
<RouteAnnouncer />                                 @* "Navigated to {0}" *@
<RouteAnnouncer MessageFormat="Seite: {0}" />       @* localized *@
```

After each client-side navigation it announces the page title (from `<PageTitle>`), or the first `h1`, or the path, through a polite live region. This is what the route announcers of Next.js and SvelteKit do.

- **Interactive:** it announces navigation by the interactive router.
- **Static server rendering:** it announces enhanced navigation. The component renders a small module that listens to Blazor's `enhancedload` event, because a static component cannot run .NET code after the page loads. Keep the component in the shared layout, so the region survives navigation.

Use it **or** `<FocusOnNavigate>`, not both. `FocusOnNavigate` moves focus to the new page's heading, and screen readers read the heading. `RouteAnnouncer` suits apps where focus should stay where it is, for example a persistent search field. Verify either with `NavigationChecks.VerifyNavigationAsync`.

## VisuallyHidden

```razor
<a href="/orders/42">Details<VisuallyHidden> of order 42</VisuallyHidden></a>
<button type="button"><svg aria-hidden="true" ...></svg><VisuallyHidden>Close</VisuallyHidden></button>
```

The content is clipped to a 1 pixel box, so it stays in the accessibility tree; `display: none` would remove it. `VisuallyHidden.Style` is available for your own elements.
