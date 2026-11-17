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
| <a id="ba11y002"></a>`BA11Y002` | `<img>` without `alt` (use `alt=""` for decorative images) | 1.1.1 |
| <a id="ba11y003"></a>`BA11Y003` | Positive `tabindex` | 2.4.3 |
| <a id="ba11y004"></a>`BA11Y004` | Unknown `aria-*` attribute, with a suggestion for typos such as `aria-labeledby` | 4.1.2 |
| <a id="ba11y005"></a>`BA11Y005` | `role` without any valid WAI-ARIA role | 4.1.2 |
| <a id="ba11y006"></a>`BA11Y006` | Focusable element inside `aria-hidden="true"` | 4.1.2 |
| <a id="ba11y007"></a>`BA11Y007` | `<input>`, `<select>`, `<textarea>` or Blazor's `InputText`, `InputTextArea`, `InputNumber`, `InputDate`, `InputSelect`, `InputCheckbox` or `InputFile` without a label: not inside a `<label>`, no `id` for a `<label for>`, no `aria-label`, `aria-labelledby`, `title` or `placeholder` | 1.3.1, 4.1.2 |
| <a id="ba11y008"></a>`BA11Y008` | `<button>` or `<a href>` without content or name (for example only an icon font element) | 4.1.2, 2.4.4 |
| <a id="ba11y009"></a>`BA11Y009` | Empty heading | 1.3.1, 2.4.6 |
| <a id="ba11y010"></a>`BA11Y010` | `<html>` without `lang` (in `App.razor`) | 3.1.1 |
| <a id="ba11y011"></a>`BA11Y011` | `<iframe>` without `title` | 4.1.2 |
| <a id="ba11y012"></a>`BA11Y012` | `<a href="#">` or `javascript:` link with `@onclick`: an action that should be a `<button>` | 4.1.2 |
| <a id="ba11y013"></a>`BA11Y013` | Element with an interactive role (`button`, `checkbox`, `tab` ...) and no `tabindex` | 2.1.1 |

## What the analyzer assumes

The rules read the markup as written, so they only report what the markup states:

- Values computed at run time count as present and correct: `alt="@Alt"`, `role="@Role"`, `tabindex="@(selected ? 0 : -1)"`, `aria-label="@Label"`.
- Elements with `@attributes` splatting are not checked for missing attributes, because the dictionary may supply them.
- Content rendered by a Razor expression (`@Label`) or a child component counts as an accessible name.
- Dynamic boolean attributes (`hidden="@(!open)"`, `disabled="@busy"`) count as absent, because Blazor only renders them when true.
- A field with an `id` is assumed to be labelled by a `<label for>`, possibly in another component.
- Components are not expanded. `<MyButton>` is checked by the toolkit's [rendered-markup audits](component-testing.md), not here.

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

- **Ignore rules in a whole file:** `@* blazor-a11y-ignore-file BA11Y003 *@`.

Prefer fixing over ignoring, and say why in the comment when you ignore.
