using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Keyboard scenarios on components rendered by the interactive host in a real browser.</summary>
public static class KeyboardExamples
{
    public static readonly LabExample FocuslessDialogScenario = new()
    {
        Id = "focusless-dialog-scenario",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:focusless-dialog-scenario
            await using var component = await env.Host.RenderAsync<FocuslessDialog>(env.Browser, p => p
                .Add(c => c.TriggerText, "Delete account")
                .Add(c => c.Title, "Delete account?")
                .Add(c => c.ConfirmText, "Delete"));
            var dialog = component.Page.GetByRole(AriaRole.Dialog);

            var scenario = component.Keyboard(new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(2) })
                .StartFromDocument()
                .PressTab().ExpectFocused("button", "Delete account").RememberFocus("trigger")
                .PressEnter().ExpectFocusWithin(dialog)
                .ExpectFocusContainedWithin(dialog)
                .Audit("dialog open")
                .PressEscape().ExpectFocusRestored("trigger");
            env.Show(scenario);

            await scenario.RunAsync();
            #endregion
        },
    };

    public static readonly LabExample ModalDialogScenario = new()
    {
        Id = "modal-dialog-scenario",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:modal-dialog-scenario
            await using var component = await env.Host.RenderAsync<ModalDialog>(env.Browser, p => p
                .Add(c => c.TriggerText, "Delete account")
                .Add(c => c.Title, "Delete account?")
                .Add(c => c.ConfirmText, "Delete"));
            var dialog = component.Page.GetByRole(AriaRole.Dialog);

            var scenario = component.Keyboard(new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(2) })
                .StartFromDocument()
                .PressTab().ExpectFocused("button", "Delete account").RememberFocus("trigger")
                .PressEnter().ExpectFocusWithin(dialog)
                .ExpectFocusContainedWithin(dialog)
                .Audit("dialog open")
                .PressEscape().ExpectFocusRestored("trigger");
            env.Show(scenario);

            await scenario.RunAsync();
            #endregion
        },
    };

    public static readonly LabExample LegacySignupScenario = new()
    {
        Id = "legacy-signup-scenario",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:legacy-signup-scenario
            await using var component = await env.Host.RenderAsync<LegacySignupForm>(env.Browser);
            var email = component.Page.GetByLabel("Email");

            var scenario = component.Keyboard(new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(2) })
                .StartFromDocument()
                .PressTab(3).ExpectFocused("button", "Create account")
                .PressEnter()
                .ExpectState(email, "invalid", "true")
                .ExpectFocused("textbox", "Full name");          // focus moves to the first invalid field
            env.Show(scenario);
            await scenario.RunAsync();

            var info = await email.GetAccessibilityInfoAsync();   // what assistive technologies get for the field
            env.Show(info, "The email field, as exposed to assistive technologies");
            Assert.Equal("Enter your email address.", info.Description);
            #endregion
        },
    };

    public static readonly LabExample SignupScenario = new()
    {
        Id = "signup-scenario",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:signup-scenario
            await using var component = await env.Host.RenderAsync<SignupForm>(env.Browser);
            var email = component.Page.GetByLabel("Email");

            var scenario = component.Keyboard(new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(2) })
                .StartFromDocument()
                .PressTab(3).ExpectFocused("button", "Create account")
                .PressEnter()
                .ExpectState(email, "invalid", "true")
                .ExpectFocused("textbox", "Full name");          // focus moves to the first invalid field
            env.Show(scenario);
            await scenario.RunAsync();

            var info = await email.GetAccessibilityInfoAsync();   // what assistive technologies get for the field
            env.Show(info, "The email field, as exposed to assistive technologies");
            Assert.Equal("Enter your email address.", info.Description);
            #endregion
        },
    };
}
