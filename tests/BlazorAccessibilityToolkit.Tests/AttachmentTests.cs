using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Tests;

public sealed class AttachmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a11y-attach-" + Guid.NewGuid().ToString("N"));

    public AttachmentTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // A 1 x 1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Html_report_embeds_image_attachments_with_their_text_alternative()
    {
        var image = Path.Combine(_root, "tab-stops.png");
        File.WriteAllBytes(image, Png);
        var audit = TestData.Audit().WithAttachment("tab stops", image, description: "Tab stops of the checkout page: 7 stops");

        var html = new HtmlReportFormatter().Format(AccessibilityReport.Create([audit], []));

        Assert.Contains("<img src=\"data:image/png;base64," + Convert.ToBase64String(Png) + "\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Tab stops of the checkout page: 7 stops\"", html, StringComparison.Ordinal);
        Assert.Contains("<figcaption>tab stops</figcaption>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_or_large_attachments_are_linked_not_embedded()
    {
        var image = Path.Combine(_root, "big.png");
        File.WriteAllBytes(image, Png);
        var audit = TestData.Audit()
            .WithAttachment("big", image)
            .WithAttachment("gone", Path.Combine(_root, "missing.png"));

        var html = new HtmlReportFormatter { MaxEmbeddedAttachmentBytes = 10 }.Format(AccessibilityReport.Create([audit], []));

        Assert.DoesNotContain("data:image/png", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{new Uri(image).AbsoluteUri}\">big</a>", html, StringComparison.Ordinal);
        Assert.Contains("gone", html, StringComparison.Ordinal);
        Assert.Contains("(file not found)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Attachments_survive_the_result_store()
    {
        var image = Path.Combine(_root, "tab-stops.png");
        File.WriteAllBytes(image, Png);
        var store = new AccessibilityResultStore(Path.Combine(_root, "results"));
        store.Save(TestData.Audit().WithAttachment("tab stops", image, description: "Tab stops"));

        var (audits, errors) = ReportLoader.LoadAudits([Path.Combine(_root, "results")]);

        Assert.Empty(errors);
        var attachment = Assert.Single(Assert.Single(audits).Attachments);
        Assert.Equal(("tab stops", image, "image/png", "Tab stops"), (attachment.Name, attachment.Path, attachment.ContentType, attachment.Description));
    }
}
