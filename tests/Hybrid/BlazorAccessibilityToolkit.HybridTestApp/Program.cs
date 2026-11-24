using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorAccessibilityToolkit.HybridTestApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();

        using var form = new Form { Text = "Orders (hybrid test app)", Width = 1000, Height = 760 };
        var view = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot/index.html",
            Services = services.BuildServiceProvider(),
        };
        view.RootComponents.Add<Main>("#app");
        form.Controls.Add(view);

        // A native toolbar button with an icon only and no accessible name: a deliberate defect for the native audit.
        var refresh = new Button { Name = "refreshButton", Text = string.Empty, Width = 40, Height = 32, Dock = DockStyle.Top };
        form.Controls.Add(refresh);
        Application.Run(form);
    }
}
