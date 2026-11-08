# Getting started

## 1. Choose what to test

| You want to check | Use | Needs a browser |
| --- | --- | --- |
| Structure, names, roles, ARIA and Blazor event handlers of a component | `AccessibilityFixture` (bUnit), browserless | No |
| The same, plus axe-core rules and color contrast with your CSS | `AccessibilityFixture` + `PlaywrightMarkupAuditEngine` | Yes |
| Keyboard behavior, focus, live regions and dynamic states of one component | `BlazorTestHost` (Interactive Server, in process) | Yes |
| Complete pages, navigation, layout and WebAssembly | `page.AuditAccessibilityAsync()` against your running app | Yes |

Most suites combine all four. Fast markup audits cover every component. Interactive tests cover widgets with keyboard behavior. End-to-end audits cover each page.

## 2. Install

Test project for component and browser tests (xUnit v3 shown):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <!-- Only needed for BlazorTestHost when this project contains no .razor files. -->
    <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BlazorAccessibilityToolkit.ComponentTesting" Version="0.1.0-preview.1" />
    <PackageReference Include="BlazorAccessibilityToolkit.Playwright" Version="0.1.0-preview.1" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyComponents\MyComponents.csproj" />
  </ItemGroup>
</Project>
```

- NUnit or MSTest? Use the same project with your framework's packages (`NUnit` + `NUnit3TestAdapter` + `Microsoft.NET.Test.Sdk`, or the `MSTest` metapackage). Keep `<OutputType>Exe</OutputType>`; with VSTest, `Microsoft.NET.Test.Sdk` generates the entry point. [`tests/Compatibility`](../tests/Compatibility) has working NUnit and MSTest projects.
- Only bUnit markup audits without a browser? Reference just `BlazorAccessibilityToolkit.ComponentTesting`. A plain `Microsoft.NET.Sdk` or `Microsoft.NET.Sdk.Razor` project is enough.
- End-to-end tests of an app? Use `Microsoft.NET.Sdk` with `Microsoft.AspNetCore.Mvc.Testing`, and reference the app project. See `demo/BlazorAccessibilityToolkit.Demo.EndToEndTests`.

Install the CI tool:

```bash
dotnet tool install --global BlazorAccessibilityToolkit.Cli --version 0.1.0-preview.1
```

## 3. Install browsers (explicitly)

The toolkit never downloads browsers on its own. After building the test project, run one of these:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium            # add firefox webkit as needed
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps chromium  # Linux CI: also installs system dependencies
```

Or call `AccessibilityBrowser.Install("chromium")` once from setup code. Select the browser with `BLAZOR_A11Y_BROWSER=chromium|firefox|webkit`. Set `BLAZOR_A11Y_HEADED=1` to watch the browser.

## 4. Share infrastructure, isolate tests

Launch one browser (and one component host) per test assembly. Each test still gets its own browser context and its own bUnit context.

```csharp
using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;

[assembly: AssemblyFixture(typeof(A11yEnvironment))]

public sealed class A11yEnvironment : IAsyncLifetime
{
    public AccessibilityBrowser Browser { get; private set; } = null!;
    public BlazorTestHost Host { get; private set; } = null!;
    public ComponentAuditSettings Markup { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Browser = await AccessibilityBrowser.LaunchAsync();
        var hostOptions = new BlazorTestHostOptions();
        hostOptions.Stylesheets.Add("_content/MyComponents/MyComponents.bundle.scp.css");
        Host = await BlazorTestHost.StartAsync(hostOptions);
        Markup = ComponentAuditSettings.Default.WithEngine(new PlaywrightMarkupAuditEngine(Browser));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Browser.DisposeAsync();
    }
}
```

## 5. Write the first tests

```csharp
public class SaveButtonTests(A11yEnvironment env)
{
    [Fact]
    public async Task Markup_is_accessible()
    {
        await using var fixture = await AccessibilityFixture.For<SaveButton>().WithSettings(env.Markup).BuildAsync();
        (await fixture.AuditAsync()).ShouldHaveNoViolations();
    }

    [Fact]
    public async Task Saving_is_announced()
    {
        await using var component = await env.Host.RenderAsync<SaveButton>(env.Browser);
        await component.Keyboard()
            .StartFromDocument()
            .PressTab().ExpectFocused("button", "Save changes")
            .PressEnter().ExpectLiveRegionUpdate("Changes saved")
            .Audit("saved")
            .RunAsync();
    }
}
```

## 6. Add the CI gate

```bash
BLAZOR_A11Y_RESULTS_DIR=$PWD/a11y-results dotnet test
blazor-a11y report --input a11y-results --output a11y-report
```

`report` writes console, JSON, JUnit, SARIF and HTML reports. It exits with 1 when findings exceed the policy, and with 2 when audits did not run completely or no results were found. To adopt the toolkit on an existing app with known issues, commit a baseline so that only new findings fail the build. See [CI integration](ci-integration.md).

## Next steps

- [Component testing](component-testing.md)
- [Browser and interactive testing](interactive-testing.md)
- [Manual review](manual-review.md), for what automation cannot decide
