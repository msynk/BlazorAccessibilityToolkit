using System.Text.RegularExpressions;
using BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting.Tests;

public sealed class AccessibleQueryTests : IAsyncDisposable
{
    private readonly BunitContext _context = new();

    public ValueTask DisposeAsync() => _context.DisposeAsync();

    private IRenderedComponent<RawMarkup> Render(string html) => _context.Render<RawMarkup>(p => p.Add(c => c.Html, html));

    [Theory]
    [InlineData("<button>Save</button>", "button", "Save")]
    [InlineData("<button aria-label=\"Close dialog\">×</button>", "button", "Close dialog")]
    [InlineData("<span id=\"l1\">Billing</span><span id=\"l2\">address</span><input aria-labelledby=\"l1 l2\">", "textbox", "Billing address")]
    [InlineData("<label for=\"e\">Email</label><input id=\"e\" type=\"email\">", "textbox", "Email")]
    [InlineData("<label>Search <input type=\"search\"></label>", "searchbox", "Search")]
    [InlineData("<input type=\"submit\">", "button", "Submit")]
    [InlineData("<input type=\"button\" value=\"Go\">", "button", "Go")]
    [InlineData("<input type=\"image\" src=\"x.png\" alt=\"Find\">", "button", "Find")]
    [InlineData("<a href=\"/\"><img src=\"logo.png\" alt=\"Home\"></a>", "link", "Home")]
    [InlineData("<a href=\"/cart\"><span aria-hidden=\"true\">🛒</span> Cart</a>", "link", "Cart")]
    [InlineData("<button><span hidden>Hidden</span>Visible</button>", "button", "Visible")]
    [InlineData("<button><span style=\"display:none\">Hidden</span>Visible</button>", "button", "Visible")]
    [InlineData("<fieldset><legend>Shipping</legend></fieldset>", "group", "Shipping")]
    [InlineData("<table><caption>Orders</caption><tr><td>1</td></tr></table>", "table", "Orders")]
    [InlineData("<figure><img src=\"a.png\" alt=\"Chart\"><figcaption>Sales 2026</figcaption></figure>", "figure", "Sales 2026")]
    [InlineData("<input title=\"Quantity\" type=\"number\">", "spinbutton", "Quantity")]
    [InlineData("<input placeholder=\"Search docs\">", "textbox", "Search docs")]
    [InlineData("<div role=\"checkbox\" aria-checked=\"false\" tabindex=\"0\">Remember me</div>", "checkbox", "Remember me")]
    [InlineData("<span id=\"hidden-label\" hidden>Secret <b>label</b></span><button aria-labelledby=\"hidden-label\">x</button>", "button", "Secret label")]
    [InlineData("<label for=\"r\">Flash <select><option>1</option><option selected>3</option></select> times</label><input id=\"r\">", "textbox", "Flash 3 times")]
    [InlineData("<label>Rows <select><option>10</option></select> per page</label>", "combobox", "Rows per page")]
    [InlineData("<svg role=\"img\"><title>Logo</title></svg>", "img", "Logo")]
    [InlineData("<h3>Billing <small>(optional)</small></h3>", "heading", "Billing (optional)")]
    [InlineData("<button id=\"b\" aria-labelledby=\"b\" aria-label=\"Close\">×</button>", "button", "Close")]
    [InlineData("<span id=\"pre\">Delete</span><button id=\"d\" aria-labelledby=\"pre d\">file.txt</button>", "button", "Delete file.txt")]
    [InlineData("<div style=\"visibility:hidden\"><button style=\"visibility: visible\">Save</button></div>", "button", "Save")]
    [InlineData("<div><p>One</p><p>Two</p></div><button aria-labelledby=\"x\"><div id=\"x\"><p>One</p><p>Two</p></div></button>", "button", "One Two")]
    public void Computes_accessible_names(string html, string role, string name)
    {
        var cut = Render(html);
        var element = cut.FindByRole(role, new ByRoleOptions { Name = name });
        element.ShouldHaveAccessibleName(name);
    }

    [Theory]
    [InlineData("<header>Site</header>", "banner")]
    [InlineData("<main><article><header>Post</header></article></main>", "article")]
    [InlineData("<footer>©</footer>", "contentinfo")]
    [InlineData("<section aria-label=\"Filters\">x</section>", "region")]
    [InlineData("<form aria-label=\"Signup\"></form>", "form")]
    [InlineData("<select multiple><option>a</option></select>", "listbox")]
    [InlineData("<input type=\"text\" list=\"l\"><datalist id=\"l\"></datalist>", "combobox")]
    [InlineData("<input type=\"range\">", "slider")]
    [InlineData("<hr>", "separator")]
    [InlineData("<output>3</output>", "status")]
    [InlineData("<table><thead><tr><th>Name</th></tr></thead><tbody><tr><th>Ana</th><td>1</td></tr></tbody></table>", "rowheader")]
    [InlineData("<button role=\"presentation\">Still a button</button>", "button")]
    [InlineData("<div role=\"tab\" tabindex=\"0\">One</div>", "tab")]
    [InlineData("<div role=\"foo switch\" tabindex=\"0\" aria-checked=\"true\">On</div>", "switch")]
    public void Computes_roles(string html, string role) => Assert.NotEmpty(Render(html).FindAllByRole(role));

    [Fact]
    public void Transcript_announces_items_named_by_their_content_once()
    {
        var cut = Render("<ul role=\"tree\" aria-label=\"Files\"><li role=\"treeitem\" aria-expanded=\"true\">Docs<ul role=\"group\"><li role=\"treeitem\">Guide</li></ul></li></ul><div style=\"visibility:hidden\"><span>Gone</span></div>");
        cut.ShouldMatchTranscript("""
            tree, Files, 1 item
            treeitem, Docs Guide, expanded
            group
            treeitem, Guide
            """);
    }

    [Fact]
    public void Generic_elements_and_unnamed_sections_have_no_role()
    {
        var cut = Render("<section>Text</section><div>x</div><img src=\"spacer.gif\" alt=\"\">");
        Assert.Null(cut.QueryByRole("region"));
        Assert.Null(cut.QueryByRole("img"));
        Assert.Equal("none", cut.Find("img").GetRole());
        Assert.Null(cut.Find("div").GetRole());
    }

    [Fact]
    public void Filters_by_state_level_and_current()
    {
        var cut = _context.Render<SettingsPanel>();

        Assert.Equal("General", cut.FindByRole("link", new ByRoleOptions { Current = "page" }).GetAccessibleName());
        Assert.Equal("Privacy", cut.FindByRole("link", new ByRoleOptions { Current = "false" }).GetAccessibleName());
        Assert.Equal("Bold", cut.FindByRole("button", new ByRoleOptions { Pressed = false }).GetAccessibleName());
        Assert.Equal("Advanced", cut.FindByRole("button", new ByRoleOptions { Expanded = false }).GetAccessibleName());
        Assert.Null(cut.QueryByRole("button", new ByRoleOptions { Expanded = true }));
        cut.FindByRole("checkbox", new ByRoleOptions { Checked = true, Name = "Email me updates" });
        cut.FindByRole("heading", new ByRoleOptions { Level = 2, NamePattern = new Regex("^Sett") });
        cut.FindByRole("region", "Settings");

        cut.FindByRole("button", "Bold").Click();
        cut.FindByRole("button", new ByRoleOptions { Name = "Bold", Pressed = true }).ShouldBePressed();
    }

    [Fact]
    public void Descriptions_error_messages_and_states()
    {
        var cut = _context.Render<SettingsPanel>();
        var nickname = cut.FindByLabelText("Nickname");

        nickname
            .ShouldHaveRole("textbox")
            .ShouldHaveAccessibleName("Nickname")
            .ShouldHaveAccessibleDescription("Shown to other users.")
            .ShouldHaveAccessibleErrorMessage("Nickname is taken.")
            .ShouldBeInvalid()
            .ShouldBeRequired()
            .ShouldBeEnabled();
        Assert.Same(nickname, cut.FindByRole("textbox", new ByRoleOptions { Description = "Shown to other users." }));

        var advanced = cut.FindByRole("button", "Advanced").ShouldBeExpanded(false);
        var failure = Assert.Throws<AccessibilityAssertionException>(() => advanced.ShouldBeExpanded());
        Assert.Contains("<button> \"Advanced\"", failure.Message, StringComparison.Ordinal);
        Assert.Contains("\"false\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_message_is_only_exposed_while_invalid()
    {
        var cut = Render("<input id=\"a\" aria-label=\"Age\" aria-errormessage=\"err\"><span id=\"err\">Too young</span>");
        var input = cut.FindByRole("textbox", "Age");
        Assert.Equal(string.Empty, input.GetAccessibleErrorMessage());
        var failure = Assert.Throws<AccessibilityAssertionException>(() => input.ShouldHaveAccessibleErrorMessage("Too young"));
        Assert.Contains("not marked aria-invalid", failure.Message, StringComparison.Ordinal);
        input.ShouldBeValid();
    }

    [Fact]
    public void Disabled_state_includes_fieldsets_and_aria_disabled()
    {
        var cut = Render("<fieldset disabled><legend><button>In legend</button></legend><button>Inside</button></fieldset><div role=\"button\" aria-disabled=\"true\" tabindex=\"0\">Fake</div>");
        cut.FindByRole("button", "Inside").ShouldBeDisabled();
        cut.FindByRole("button", "In legend").ShouldBeEnabled();
        cut.FindByRole("button", new ByRoleOptions { Name = "Fake", Disabled = true });
    }

    [Fact]
    public void Hidden_elements_are_excluded_unless_requested()
    {
        var cut = Render("<div aria-hidden=\"true\"><button>Ghost</button></div><details><summary>More</summary><button>Inner</button></details>");
        Assert.Null(cut.QueryByRole("button", new ByRoleOptions { Name = "Ghost" }));
        Assert.Null(cut.QueryByRole("button", new ByRoleOptions { Name = "Inner" }));
        cut.FindByRole("button", new ByRoleOptions { Name = "Ghost", Hidden = true });
    }

    [Fact]
    public void Failures_list_the_available_roles()
    {
        var cut = _context.Render<SettingsPanel>();
        var missing = Assert.Throws<AccessibilityAssertionException>(() => cut.FindByRole("button", "Save"));
        Assert.Contains("No element with role \"button\" and name \"Save\"", missing.Message, StringComparison.Ordinal);
        Assert.Contains("\"Bold\" <button>", missing.Message, StringComparison.Ordinal);

        var several = Assert.Throws<AccessibilityAssertionException>(() => cut.FindByRole("link"));
        Assert.Contains("2 elements with role \"link\"", several.Message, StringComparison.Ordinal);
        Assert.Contains("link \"Privacy\"", several.Message, StringComparison.Ordinal);
        Assert.Contains("heading:", cut.DescribeRoles(), StringComparison.Ordinal);
    }

    [Fact]
    public void Container_queries_search_descendants()
    {
        var cut = _context.Render<SettingsPanel>();
        var nav = cut.FindByRole("navigation", "Sections");
        Assert.Equal("Privacy", nav.FindByRole("link", new ByRoleOptions { Name = "Privacy" }).GetAccessibleName());
        Assert.Throws<AccessibilityAssertionException>(() => nav.FindByRole("button"));
    }

    [Fact]
    public void Query_fallbacks_match_later_role_tokens()
    {
        var cut = Render("<div role=\"switch checkbox\" tabindex=\"0\" aria-checked=\"true\">Wi-Fi</div>");
        Assert.Null(cut.QueryByRole("checkbox"));
        cut.FindByRole("checkbox", new ByRoleOptions { QueryFallbacks = true });
    }

    [Fact]
    public void Transcript_reads_roles_names_and_states_in_order()
    {
        var cut = _context.Render<SettingsPanel>();
        cut.ShouldMatchTranscript("""
            region, Settings
            heading, Settings, level 2
            navigation, Sections
            list, 2 items
            link, General, current page
            link, Privacy
            button, Bold, not pressed
            button, Advanced, collapsed
            checkbox, Email me updates, checked
            Nickname
            textbox, Nickname, required, invalid, Shown to other users.
            Shown to other users.
            Nickname is taken.
            img, Your avatar
            """);

        cut.FindByRole("button", "Bold").Click();
        Assert.Contains("button, Bold, pressed", cut.GetScreenReaderTranscript(), StringComparison.Ordinal);

        var failure = Assert.Throws<AccessibilityAssertionException>(() => cut.ShouldMatchTranscript("region, Settings\nbutton, Bold, not pressed"));
        Assert.Contains("+ heading, Settings, level 2", failure.Message, StringComparison.Ordinal);
        Assert.Contains("- button, Bold, not pressed", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accessibility_tree_exposes_nodes()
    {
        var cut = _context.Render<SettingsPanel>();
        var region = Assert.Single(cut.GetAccessibilityTree());
        Assert.Equal("region", region.Role);
        var heading = region.Children[0];
        Assert.Equal(("heading", "Settings", "2"), (heading.Role, heading.Name, heading.States["level"]));
        Assert.Equal("h2", heading.Element!.LocalName);
        Assert.Empty(heading.Children);
    }
}
