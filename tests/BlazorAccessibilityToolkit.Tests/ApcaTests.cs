namespace BlazorAccessibilityToolkit.Tests;

public sealed class ApcaTests
{
    // The test values of apca-w3 (test/index.js), which compares them exactly.
    [Theory]
    [InlineData("#888", "#FFF", 63.056469930209424)]
    [InlineData("#FFF", "#888", -68.54146436644962)]
    [InlineData("#000", "#aaa", 58.146262578561334)]
    [InlineData("#aaa", "#000", -56.24113336839742)]
    [InlineData("#123", "#def", 91.66830811481631)]
    [InlineData("#def", "#123", -93.06770049484275)]
    [InlineData("#123", "#444", 8.32326136957393)]
    [InlineData("#444", "#123", -7.526878460278154)]
    [InlineData("#0006", "#fff", 54.62184067441377)]
    public void Matches_the_apca_w3_test_values(string text, string background, double expected) =>
        Assert.Equal(expected, Apca.Contrast(text, background), 12);

    [Fact]
    public void Documented_example_holds() => Assert.Equal(71.1, Math.Round(Apca.Contrast("#777777", "#ffffff"), 1));

    [Fact]
    public void Identical_colors_have_no_contrast()
    {
        Assert.Equal(0, Apca.Contrast("#777777", "#777777"));
        Assert.Equal(0, Apca.Contrast(double.NaN, 0.5));
    }

    [Theory]
    [InlineData(16, 400, 90, false)]
    [InlineData(18, 400, 75, false)]
    [InlineData(14, 700, 75, false)]
    [InlineData(16, 700, 60, false)]
    [InlineData(24, 400, 60, false)]
    [InlineData(21, 500, 60, false)]
    [InlineData(36, 400, 45, false)]
    [InlineData(24, 700, 45, false)]
    [InlineData(28, 600, 60, false)] // Lc 45 lists 400 and 700 only, so 600 uses the 36 px of 400; Lc 60 asks 18 px at 600
    [InlineData(17, 300, 90, true)]
    [InlineData(12, 400, 90, true)]
    public void Required_contrast_follows_the_font_size_guidance(double size, int weight, int expected, bool tooSmall)
    {
        Assert.Equal(expected, Apca.RequiredLc(size, weight, out var small));
        Assert.Equal(tooSmall, small);
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("red")]
    [InlineData("#gggggg")]
    public void Rejects_colors_that_are_not_hex(string color) => Assert.Throws<FormatException>(() => Apca.Contrast(color, "#fff"));
}
