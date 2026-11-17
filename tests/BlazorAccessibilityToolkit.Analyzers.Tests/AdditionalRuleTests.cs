namespace BlazorAccessibilityToolkit.Analyzers.Tests;

public sealed class AdditionalRuleTests
{
    private static string[] Ids(string markup, AnalyzerSettings? settings = null) =>
        RazorAccessibilityAnalyzer.Analyze(markup, settings).Select(r => r.Descriptor.Id).ToArray();

    [Theory]
    [InlineData("<input type=\"image\" src=\"go.png\">", "BA11Y002")]
    [InlineData("<map name=\"m\"><area href=\"/a\" shape=\"rect\" coords=\"0,0,1,1\"></map>", "BA11Y002")]
    [InlineData("<svg role=\"img\"><path d=\"M0\" /></svg>", "BA11Y002")]
    [InlineData("<html lang=\"\"><body></body></html>", "BA11Y010")]
    [InlineData("<input id=\"q\" autofocus>", "BA11Y014")]
    [InlineData("<button accesskey=\"s\">Save</button>", "BA11Y015")]
    [InlineData("<marquee>News</marquee>", "BA11Y016")]
    [InlineData("<blink>Sale</blink>", "BA11Y016")]
    [InlineData("<video src=\"intro.mp4\" controls></video>", "BA11Y017")]
    [InlineData("<video controls><track kind=\"subtitles\" src=\"de.vtt\"></video>", "BA11Y017")]
    [InlineData("<td scope=\"row\">A</td>", "BA11Y018")]
    [InlineData("<th scope=\"column\">A</th>", "BA11Y018")]
    [InlineData("<input id=\"e\" autocomplete=\"e-mail\">", "BA11Y019")]
    [InlineData("<input id=\"n\" autocomplete=\"work given-name\">", "BA11Y019")]
    [InlineData("<input id=\"n\" autocomplete=\"email shipping\">", "BA11Y019")]
    [InlineData("<div role=\"listbox\" aria-label=\"Fruit\" aria-activedescendant=\"o1\"><div role=\"option\" id=\"o1\">Apple</div></div>", "BA11Y020")]
    [InlineData("<div role=\"checkbox\" tabindex=\"0\" @onkeydown=\"K\">Remember</div>", "BA11Y021")]
    [InlineData("<div role=\"slider\" tabindex=\"0\" aria-label=\"Volume\"></div>", "BA11Y021")]
    [InlineData("<button aria-pressed=\"yes\">Bold</button>", "BA11Y022")]
    [InlineData("<div aria-hidden=\"hidden\">x</div>", "BA11Y022")]
    [InlineData("<div aria-live=\"loud\">x</div>", "BA11Y022")]
    [InlineData("<div role=\"heading\" aria-level=\"two\">x</div>", "BA11Y022")]
    [InlineData("<div aria-relevant=\"additions everything\" aria-live=\"polite\">x</div>", "BA11Y022")]
    [InlineData("<button role=\"button\">Save</button>", "BA11Y023")]
    [InlineData("<h2 role=\"heading\">Title</h2>", "BA11Y023")]
    [InlineData("<a href=\"/\" role=\"link\">Home</a>", "BA11Y023")]
    [InlineData("<div tabindex=\"0\">Plain text</div>", "BA11Y024")]
    [InlineData("<span @onmouseover=\"Show\">Help</span>", "BA11Y025")]
    [InlineData("<div @onmouseleave=\"Hide\" @onfocus=\"Show\">Help</div>", "BA11Y025")]
    [InlineData("<button>Save <a href=\"/help\">help</a></button>", "BA11Y026")]
    [InlineData("<a href=\"/x\"><button>Go</button></a>", "BA11Y026")]
    [InlineData("<div role=\"button\" tabindex=\"0\" @onkeydown=\"K\" @onclick=\"C\">Row <input type=\"checkbox\" aria-label=\"Pick\"></div>", "BA11Y026")]
    [InlineData("<a href=\"/docs\">Click here</a>", "BA11Y027")]
    [InlineData("<a href=\"/docs\">Read more…</a>", "BA11Y027")]
    [InlineData("<p lang=\"en_US\">Hello</p>", "BA11Y028")]
    [InlineData("<html lang=\"english\"></html>", "BA11Y028")]
    [InlineData("<meta name=\"viewport\" content=\"width=device-width, user-scalable=no\">", "BA11Y029")]
    [InlineData("<meta name=\"viewport\" content=\"width=device-width, maximum-scale=1\">", "BA11Y029")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"30; url=/home\">", "BA11Y030")]
    [InlineData("<input id=\"p\" type=\"password\" autocomplete=\"off\">", "BA11Y033")]
    [InlineData("<input id=\"p\" type=\"password\" autocomplete=\"current-password\" @onpaste:preventDefault>", "BA11Y033")]
    [InlineData("<InputText id=\"code\" @bind-Value=\"Code\" @onpaste:preventDefault=\"true\" />", "BA11Y033")]
    [InlineData("<input id=\"p\" onpaste=\"return false\">", "BA11Y033")]
    [InlineData("<li draggable=\"true\">Item</li>", "BA11Y034")]
    [InlineData("<div @ondrop=\"Drop\">Drop files</div>", "BA11Y034")]
    [InlineData("<img src=\"a.png\" alt=\"Photo of the team\">", "BA11Y036")]
    [InlineData("<button role=\"presentation\">Save</button>", "BA11Y037")]
    [InlineData("<div role=\"none\" tabindex=\"0\">x</div>", "BA11Y037")]
    public void Reports_defects(string markup, string expected) => Assert.Contains(expected, Ids(markup));

    [Theory]
    [InlineData("<input type=\"image\" src=\"go.png\" alt=\"Search\">")]
    [InlineData("<svg role=\"img\" aria-label=\"Logo\"><path d=\"M0\" /></svg>")]
    [InlineData("<svg role=\"img\"><title>Logo</title><path d=\"M0\" /></svg>")]
    [InlineData("<svg aria-hidden=\"true\"><path d=\"M0\" /></svg>")]
    [InlineData("<video src=\"loop.mp4\" muted autoplay loop></video>")]
    [InlineData("<video controls><track kind=\"captions\" src=\"en.vtt\" srclang=\"en\"></video>")]
    [InlineData("<video controls>@foreach (var t in Tracks) { <track kind=\"@t.Kind\" src=\"@t.Src\"> }</video>")]
    [InlineData("<th scope=\"col\">Name</th>")]
    [InlineData("<th scope=\"@Scope\">Name</th>")]
    [InlineData("<input id=\"e\" autocomplete=\"email\">")]
    [InlineData("<input id=\"e\" autocomplete=\"section-a shipping work email\">")]
    [InlineData("<input id=\"n\" autocomplete=\"billing street-address\">")]
    [InlineData("<input id=\"u\" autocomplete=\"username webauthn\">")]
    [InlineData("<input id=\"x\" autocomplete=\"off\">")]
    [InlineData("<input id=\"x\" autocomplete=\"@Purpose\">")]
    [InlineData("<div role=\"listbox\" aria-label=\"Fruit\" tabindex=\"0\" aria-activedescendant=\"o1\"><div role=\"option\" id=\"o1\" aria-selected=\"true\">Apple</div></div>")]
    [InlineData("<input id=\"a\" type=\"checkbox\" role=\"switch\">")]
    [InlineData("<div role=\"checkbox\" aria-checked=\"@State\" tabindex=\"0\" @onkeydown=\"K\">Remember</div>")]
    [InlineData("<span role=\"heading\" aria-level=\"2\">Title</span>")]
    [InlineData("<div role=\"heading\">Title</div>")]
    [InlineData("<input id=\"k\" type=\"@Kind\" role=\"textbox\">")]
    [InlineData("<input id=\"p\" type=\"password\" autocomplete=\"current-password\" @onpaste:preventDefault=\"@blockPaste\">")]
    [InlineData("<h2>Title</h2>")]
    [InlineData("<button aria-pressed=\"true\">Bold</button>")]
    [InlineData("<button aria-pressed=\"@(bold ? \"true\" : \"false\")\">Bold</button>")]
    [InlineData("<div aria-hidden=\"TRUE\">x</div>")]
    [InlineData("<div aria-relevant=\"additions text\" aria-live=\"polite\">x</div>")]
    [InlineData("<div role=\"slider\" tabindex=\"0\" aria-label=\"Volume\" aria-valuenow=\"0.5\" aria-valuemin=\"0\" aria-valuemax=\"1\"></div>")]
    [InlineData("<ul role=\"list\"><li>One</li></ul>")]
    [InlineData("<nav role=\"navigation\"><a href=\"/\">Home</a></nav>")]
    [InlineData("<div tabindex=\"-1\">Focus target</div>")]
    [InlineData("<div tabindex=\"0\" role=\"region\" aria-label=\"Code sample\">Scrollable</div>")]
    [InlineData("<div role=\"tabpanel\" tabindex=\"0\">Panel</div>")]
    [InlineData("<span @onmouseover=\"Show\" @onfocus=\"Show\">Help</span>")]
    [InlineData("<span @onmouseenter=\"Show\" @onfocusin=\"Show\">Help</span>")]
    [InlineData("<button><span @onclick=\"X\">Save</span></button>")]
    [InlineData("<details><summary>More</summary><a href=\"/x\">x</a></details>")]
    [InlineData("<a href=\"/docs\">Read the installation guide</a>")]
    [InlineData("<a href=\"/docs\" aria-label=\"Read more about pricing\">Read more</a>")]
    [InlineData("<a href=\"/docs\">Read more<span class=\"visually-hidden\"> about pricing</span></a>")]
    [InlineData("<p lang=\"en-US\">Hello</p>")]
    [InlineData("<p lang=\"zh-Hant-TW\">Hello</p>")]
    [InlineData("<p lang=\"@Culture\">Hello</p>")]
    [InlineData("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">")]
    [InlineData("<meta name=\"viewport\" content=\"width=device-width, maximum-scale=5\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0; url=/home\">")]
    [InlineData("<input id=\"p\" type=\"password\" autocomplete=\"new-password\">")]
    [InlineData("<img src=\"a.png\" alt=\"Team at the 2026 offsite\">")]
    [InlineData("<div role=\"none\">x</div>")]
    public void Accepts_accessible_markup(string markup) => Assert.Empty(Ids(markup));

    [Fact]
    public void Aria_states_bound_to_bools_are_reported_because_blazor_drops_false_attributes()
    {
        const string markup = """
            <button aria-expanded="@expanded" aria-controls="panel" @onclick="Toggle">Filters</button>
            <button aria-pressed="@(!muted)">Sound</button>
            <div role="tab" aria-selected="@(Index == 1)" tabindex="0">One</div>
            <button aria-expanded="@Label">Label is a string</button>
            <button aria-pressed="@(bold ? "true" : "false")">Bold</button>
            <button aria-expanded="@expanded.ToString().ToLowerInvariant()">Fine</button>
            <button aria-expanded="@Open">Parameter</button>
            @code {
                [Parameter] public bool Open { get; set; }
                private bool expanded;
                private bool muted;
                private int Index { get; set; }
                [Parameter] public string Label { get; set; } = "";
                private bool bold;
            }
            """;
        var results = RazorAccessibilityAnalyzer.Analyze(markup).Where(r => r.Descriptor.Id == "BA11Y035").ToArray();
        Assert.Equal(4, results.Length);
        Assert.Equal(["aria-expanded", "aria-pressed", "aria-selected", "aria-expanded"], results.Select(r => (string)r.Args[0]));
        Assert.Contains("\"true\" : \"false\"", string.Format(System.Globalization.CultureInfo.InvariantCulture, results[0].Descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture), results[0].Args), StringComparison.Ordinal);
    }

    [Fact]
    public void Routable_pages_need_a_page_title()
    {
        var missing = RazorAccessibilityAnalyzer.Analyze("@page \"/orders\"\n<h1>Orders</h1>").ToArray();
        var result = Assert.Single(missing);
        Assert.Equal("BA11Y031", result.Descriptor.Id);
        Assert.Null(result.Element);
        Assert.Equal(0, result.Span.Start);

        Assert.Equal(["BA11Y031"], Ids("@attribute [Route(\"/orders\")]\n<h1>Orders</h1>"));
        Assert.Empty(Ids("@page \"/orders\"\n<PageTitle>Orders</PageTitle>\n<h1>Orders</h1>"));
        Assert.Empty(Ids("<h1>Not routable</h1>"));
        Assert.Empty(Ids("@* blazor-a11y-ignore-file BA11Y031 *@\n@page \"/orders\"\n<h1>Orders</h1>"));
    }

    [Fact]
    public void Router_needs_focus_on_navigate()
    {
        const string routes = """
            <Router AppAssembly="typeof(Program).Assembly">
                <Found Context="routeData">
                    <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
                </Found>
            </Router>
            """;
        Assert.Equal(["BA11Y032"], Ids(routes));
        Assert.Empty(Ids(routes.Replace("<RouteView", "<FocusOnNavigate RouteData=\"routeData\" Selector=\"h1\" />\n<RouteView", StringComparison.Ordinal)));
    }

    [Fact]
    public void Components_can_be_mapped_to_native_elements()
    {
        var settings = AnalyzerSettings.Create("MudIconButton:button, MudImage:img, MudLink:a, MudTextField:input");

        Assert.Equal(["BA11Y008"], Ids("<MudIconButton Icon=\"@Icons.Close\" />", settings));
        Assert.Empty(Ids("<MudIconButton Icon=\"@Icons.Close\" aria-label=\"Close\" />", settings));
        Assert.Equal(["BA11Y002"], Ids("<MudImage Src=\"logo.png\" />", settings));
        Assert.Empty(Ids("<MudImage Src=\"logo.png\" Alt=\"Company logo\" />", settings));
        Assert.Equal(["BA11Y027"], Ids("<MudLink Href=\"/docs\">here</MudLink>", settings));
        Assert.Equal(["BA11Y007"], Ids("<MudTextField @bind-Value=\"Name\" />", settings));
        Assert.Empty(Ids("<MudTextField @bind-Value=\"Name\" Label=\"Name\" />", settings));

        // Without the map, components are not judged.
        Assert.Empty(Ids("<MudIconButton Icon=\"@Icons.Close\" />"));

        // The parameters that name a mapped component are configurable.
        var custom = AnalyzerSettings.Create("RadzenButton:button", "Text");
        Assert.Empty(Ids("<RadzenButton Icon=\"close\" Text=\"Close\" />", custom));
        Assert.Equal(["BA11Y008"], Ids("<RadzenButton Icon=\"close\" />", custom));
    }

    [Fact]
    public void Every_rule_has_documentation_and_a_unique_id()
    {
        var descriptors = new RazorAccessibilityAnalyzer().SupportedDiagnostics;
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.Id).Distinct().Count());
        var docs = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "analyzers.md"));
        foreach (var descriptor in descriptors)
        {
            Assert.Contains($"<a id=\"{descriptor.Id.ToLowerInvariant()}\"></a>", docs, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
}
