# Razor analyzers

`BlazorAccessibilityToolkit.Analyzers` checks `.razor` (and `.cshtml`) markup while you build. Problems appear as compiler warnings with the Razor file and line, in the build output and in the IDE's error list, before any test runs. It is the Blazor counterpart of `eslint-plugin-jsx-a11y` and the `@angular-eslint/template` accessibility rules.

```xml
<PackageReference Include="BlazorAccessibilityToolkit.Analyzers" Version="0.1.0-preview.1" PrivateAssets="all" />
```

The Razor SDK passes component files to analyzers as additional files, so no other setup is needed.

```text
Pages/Cart.razor(14,6): warning BA11Y001: <div> has a Blazor @onclick handler, but it has no interactive role, it is not focusable (no tabindex), no @onkeydown or @onkeyup handler supports Enter or Space; use a <button>, or add an interactive role, tabindex="0" and an @onkeydown handler
```

## Rules

| Id | Title | WCAG |
| --- | --- | --- |
| <a id="ba11y001"></a>`BA11Y001` | Clickable element is not keyboard accessible: `@onclick`, `@onmousedown`, `@onmouseup`, `@onpointerdown`, `@onpointerup` or `@ondblclick` on an element that is not a button, link or form control, without an interactive role, `tabindex` or `@onkeydown`/`@onkeyup` | 2.1.1, 4.1.2 |
| <a id="ba11y002"></a>`BA11Y002` | `<img>`, `<input type="image">` or `<area href>` without `alt`, or `<svg role="img">` without `<title>` or `aria-label` (use `alt=""` for decorative images) | 1.1.1 |
| <a id="ba11y003"></a>`BA11Y003` | Positive `tabindex` | 2.4.3 |
| <a id="ba11y004"></a>`BA11Y004` | Unknown `aria-*` attribute, with a suggestion for typos such as `aria-labeledby` | 4.1.2 |
| <a id="ba11y005"></a>`BA11Y005` | `role` without any valid WAI-ARIA role | 4.1.2 |
| <a id="ba11y006"></a>`BA11Y006` | Focusable element inside `aria-hidden="true"` | 4.1.2 |
| <a id="ba11y007"></a>`BA11Y007` | `<input>`, `<select>`, `<textarea>` or Blazor's `InputText`, `InputTextArea`, `InputNumber`, `InputDate`, `InputSelect`, `InputCheckbox` or `InputFile` without a label: not inside a `<label>`, no `id` for a `<label for>`, no `aria-label`, `aria-labelledby`, `title` or `placeholder` | 1.3.1, 4.1.2 |
| <a id="ba11y008"></a>`BA11Y008` | `<button>` or `<a href>` without content or name (for example only an icon font element) | 4.1.2, 2.4.4 |
| <a id="ba11y009"></a>`BA11Y009` | Empty heading | 1.3.1, 2.4.6 |
| <a id="ba11y010"></a>`BA11Y010` | `<html>` without `lang`, or with an empty one (in `App.razor`) | 3.1.1 |
| <a id="ba11y011"></a>`BA11Y011` | `<iframe>` without `title` | 4.1.2 |
| <a id="ba11y012"></a>`BA11Y012` | `<a href="#">` or `javascript:` link with `@onclick`: an action that should be a `<button>` | 4.1.2 |
| <a id="ba11y013"></a>`BA11Y013` | Element with an interactive role (`button`, `checkbox`, `tab` ...) and no `tabindex` | 2.1.1 |
| <a id="ba11y014"></a>`BA11Y014` | `autofocus` (info). Move focus deliberately instead, for example with `ElementReference.FocusAsync()` when a dialog opens | best practice |
| <a id="ba11y015"></a>`BA11Y015` | `accesskey` (info): it conflicts with browser and screen reader shortcuts | best practice |
| <a id="ba11y016"></a>`BA11Y016` | `<marquee>` or `<blink>` | 2.2.2 |
| <a id="ba11y017"></a>`BA11Y017` | `<video>` without `<track kind="captions">`, unless `muted` | 1.2.2 |
| <a id="ba11y018"></a>`BA11Y018` | `scope` on an element other than `<th>`, or a value other than `row`, `col`, `rowgroup` or `colgroup` | 1.3.1 |
| <a id="ba11y019"></a>`BA11Y019` | `autocomplete` value that is not a valid autofill token list (for example `e-mail`, or `work` before a non-contact field) | 1.3.5 |
| <a id="ba11y020"></a>`BA11Y020` | `aria-activedescendant` on an element without `tabindex` that cannot receive focus | 2.1.1, 4.1.2 |
| <a id="ba11y021"></a>`BA11Y021` | Role without its required states. `checkbox`, `radio`, `switch`, `menuitemcheckbox` and `menuitemradio` need `aria-checked`. `slider` and `meter` need `aria-valuenow`. `scrollbar` needs `aria-controls` and `aria-valuenow`. `combobox` needs `aria-expanded`. (`heading` needs no `aria-level`: ARIA 1.2 defaults it to 2.) Native elements that provide the state, such as `<input type="checkbox" role="switch">`, are accepted | 4.1.2 |
| <a id="ba11y022"></a>`BA11Y022` | ARIA attribute with a value its type does not allow, for example `aria-pressed="yes"`, `aria-hidden="hidden"`, `aria-live="loud"` or `aria-level="two"` | 4.1.2 |
| <a id="ba11y023"></a>`BA11Y023` | Role that repeats the element's implicit role, such as `<button role="button">` (info). `<ul role="list">`, which restores list semantics in Safari, and `<nav role="navigation">` are accepted | best practice |
| <a id="ba11y024"></a>`BA11Y024` | `tabindex="0"` or higher on a non-interactive element without an interactive role, handler or accessible name (info). A named scrollable region is accepted | best practice |
| <a id="ba11y025"></a>`BA11Y025` | `@onmouseover` or `@onmouseenter` without `@onfocus`/`@onfocusin`, or `@onmouseout` or `@onmouseleave` without `@onblur`/`@onfocusout` | 2.1.1, 1.4.13 |
| <a id="ba11y026"></a>`BA11Y026` | Interactive control nested in a `<button>`, an `<a href>` or an element with a control role | 4.1.2 |
| <a id="ba11y027"></a>`BA11Y027` | Link whose whole text is "click here", "here", "read more", "learn more", "more" ... (info) | 2.4.4 |
| <a id="ba11y028"></a>`BA11Y028` | `lang` value that is not a well-formed BCP 47 tag, such as `en_US` or `english` | 3.1.1, 3.1.2 |
| <a id="ba11y029"></a>`BA11Y029` | Viewport meta tag with `user-scalable=no` or a `maximum-scale` below 2 | 1.4.4 |
| <a id="ba11y030"></a>`BA11Y030` | `<meta http-equiv="refresh">` with a delay | 2.2.1, 3.2.5 |
| <a id="ba11y031"></a>`BA11Y031` | Routable component (`@page` or `@attribute [Route]`) without `<PageTitle>` | 2.4.2 |
| <a id="ba11y032"></a>`BA11Y032` | `<Router>` without `<FocusOnNavigate>` (or the toolkit's [`<NavigationFocus>`](runtime-components.md#navigationfocus)), so focus stays on the clicked link after client-side navigation | 2.4.3 |
| <a id="ba11y033"></a>`BA11Y033` | Paste blocked with `@onpaste:preventDefault` (static, not `="@condition"`) or `onpaste="return false"`, or a password field with `autocomplete="off"` | 3.3.8 |
| <a id="ba11y034"></a>`BA11Y034` | `draggable="true"`, `@ondragstart`, `@ondrag` or `@ondrop` (info): check that a single-pointer alternative exists | 2.5.7 |
| <a id="ba11y035"></a>`BA11Y035` | `aria-expanded`, `aria-pressed`, `aria-checked` or `aria-selected` bound to a bool. Blazor leaves out an attribute whose value is `false`, so the collapsed, unpressed or unchecked state is never exposed. See [ARIA states bound to bools](#aria-states-bound-to-bools) | 4.1.2 |
| <a id="ba11y036"></a>`BA11Y036` | `alt` text that contains "image", "picture", "photo", "graphic" or "icon" (info) | 1.1.1 |
| <a id="ba11y037"></a>`BA11Y037` | `role="none"` or `role="presentation"` on a focusable element or on one with global ARIA attributes, where browsers ignore the role | 4.1.2 |
| <a id="ba11y038"></a>`BA11Y038` | In-page link with a bare fragment (`<a href="#main">`). Blazor apps set `<base href="/">`, so the link resolves to the home page and leaves the current page. Include the path (`href="/orders#main"`) or use the [`SkipLink`](runtime-components.md#skiplink) component. A link with `@onclick:preventDefault` is accepted, because Blazor cancels the navigation once the page is interactive (not while it is prerendered) | 2.4.1, 2.4.4 |

Rules marked *info* report suggestions (`DiagnosticSeverity.Info`). The others report warnings.

### ARIA states bound to bools

Blazor renders an attribute whose value is the bool `false` by leaving it out. That is right for `disabled` or `hidden`, but not for ARIA states, where a missing attribute means something else:

```razor
@* When collapsed, this renders <button aria-controls="filters">, which is not a disclosure at all. *@
<button aria-expanded="@expanded" aria-controls="filters">Filters</button>

@* This renders aria-expanded="false" when collapsed. *@
<button aria-expanded="@(expanded ? "true" : "false")" aria-controls="filters">Filters</button>
```

`BA11Y035` reports such an attribute when its value is clearly a bool: a negation, a comparison or logical expression, a `true` or `false` literal, or a field, property or parameter declared as `bool` in the same `.razor` file. Values that are strings, ternaries or `ToString()` calls are accepted. Rendered-markup audits cannot see this defect: the attribute is simply absent, as if it had never been written.

## What the analyzer assumes

The rules read the markup as written, so they only report what the markup states:

- Values computed at run time count as present and correct: `alt="@Alt"`, `role="@Role"`, `tabindex="@(selected ? 0 : -1)"`, `aria-label="@Label"`.
- Elements with `@attributes` splatting are not checked for missing attributes, because the dictionary may supply them.
- Content rendered by a Razor expression (`@Label`) or a child component counts as an accessible name.
- Dynamic boolean attributes (`hidden="@(!open)"`, `disabled="@busy"`) count as absent, because Blazor only renders them when true.
- A field with an `id` is assumed to be labelled by a `<label for>`, possibly in another component.
- Components are not expanded. `<MyButton>` is checked by the toolkit's [rendered-markup audits](component-testing.md), not here, unless you [map it to a native element](#component-libraries).

The bUnit and browser audits check the rendered result, including everything above. Use both: the analyzer for fast feedback while typing, the audits for the truth.

## Configuring

- **Severity:** `dotnet_diagnostic.BA11Y001.severity = error` (or `suggestion`, `none`) in `.editorconfig`. With `TreatWarningsAsErrors`, the warnings fail the build.
- **Turn the analyzer off for files or folders:**

  ```ini
  [Legacy/**.razor]
  blazor_a11y_analyzer.enabled = false
  ```

- **Ignore one element:** a Razor comment on the line before it. Without ids, every rule is ignored for that element.

  ```razor
  @* blazor-a11y-ignore BA11Y002 *@
  <img src="tracking-pixel.gif">
  ```

- **Ignore rules in a whole file:** `@* blazor-a11y-ignore-file BA11Y003 *@`. This also applies to the per-file rules `BA11Y031` and `BA11Y032`.

### Component libraries

Map components that render a native element to that element, and the analyzer checks them like the element. This is the counterpart of `settings.components` in `eslint-plugin-jsx-a11y`:

```ini
[*.razor]
blazor_a11y_analyzer.component_map = MudButton:button, MudIconButton:button, MudLink:a, MudImage:img, MudTextField:input, FluentButton:button, FluentAnchor:a
# Component parameters that give a mapped component its accessible name (default: Label, AriaLabel, Title, Alt, AlternateText).
blazor_a11y_analyzer.component_label_attributes = Label, AriaLabel, Title, Alt, Text
```

```text
Pages/Toolbar.razor(3,2): warning BA11Y008: <MudIconButton> has no text content and no aria-label, aria-labelledby or title, so screen readers announce it without a name
```

Attribute names are compared without regard to case, so `Href`, `Alt` and `Title` parameters count as `href`, `alt` and `title`. Only map components whose parameters really end up on that element. The rendered-markup audits remain the reference.

Prefer fixing over ignoring, and say why in the comment when you ignore.
