using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using Bunit;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>bUnit queries by role and accessible name, accessible-name assertions and virtual transcripts.</summary>
public static class QueryExamples
{
    public static readonly LabExample LegacySignupQueries = new()
    {
        Id = "legacy-signup-queries",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:legacy-signup-queries
            await using var fixture = await AccessibilityFixture.For<LegacySignupForm>().BuildAsync();
            var cut = fixture.Component;

            cut.Find("form").Submit();
            env.Show(new LabText(cut.GetScreenReaderTranscript()), "Virtual screen-reader transcript after submitting");

            cut.FindByLabelText("Full name")
                .ShouldBeInvalid()
                .ShouldHaveAccessibleDescription("Enter your full name.");
            #endregion
        },
    };

    public static readonly LabExample SignupQueries = new()
    {
        Id = "signup-queries",
        Run = async env =>
        {
            #region snippet:signup-queries
            await using var fixture = await AccessibilityFixture.For<SignupForm>().BuildAsync();
            var cut = fixture.Component;

            cut.Find("form").Submit();
            env.Show(new LabText(cut.GetScreenReaderTranscript()), "Virtual screen-reader transcript after submitting");

            cut.FindByLabelText("Full name")
                .ShouldBeInvalid()
                .ShouldHaveAccessibleDescription("Enter your full name.");
            cut.FindByRole("textbox", new ByRoleOptions { Name = "Email", Description = "Enter your email address." });
            #endregion
        },
    };
}
