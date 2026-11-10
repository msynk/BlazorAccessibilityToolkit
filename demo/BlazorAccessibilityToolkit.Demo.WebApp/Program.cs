using BlazorAccessibilityToolkit.Demo.WebApp.Components;
using BlazorAccessibilityToolkit.Demo.WebApp.Lab;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Live runs of the toolkit (the "Run this test" buttons): see Lab/.
builder.Services.AddSingleton<LabBrowser>();
builder.Services.AddSingleton<LabRunner>();
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddScoped<LabPage>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

// Unknown URLs get the "Page not found" page (with status 404) instead of an empty response.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
ReportStore.MapEndpoints(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

/// <summary>Entry point, public so end-to-end tests can use WebApplicationFactory.</summary>
public partial class Program;
