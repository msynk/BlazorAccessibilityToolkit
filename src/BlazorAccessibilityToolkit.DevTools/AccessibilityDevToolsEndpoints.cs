using System.Security.Cryptography;
using System.Text;
using Deque.AxeCore.Commons;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BlazorAccessibilityToolkit.DevTools;

/// <summary>Maps the endpoints that serve the development overlay and axe-core.</summary>
public static class AccessibilityDevToolsEndpoints
{
    /// <summary>The path, relative to the app's base path, under which the scripts are served.</summary>
    public const string BasePath = "_blazor-a11y";

    private static readonly Lazy<Script> Overlay = new(() =>
    {
        using var stream = typeof(AccessibilityDevToolsEndpoints).Assembly.GetManifestResourceStream("BlazorAccessibilityToolkit.DevTools.devtools.js")
            ?? throw new InvalidOperationException("The overlay script resource is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return new Script(reader.ReadToEnd());
    });

    private static readonly Lazy<Script> Axe = new(() => new Script(new BundledAxeScriptProvider().GetScript()));

    /// <summary>
    /// Serves <c>_blazor-a11y/devtools.js</c> (the overlay) and <c>_blazor-a11y/axe.js</c> (the axe-core version this
    /// package bundles). Map them in development only:
    /// <code>
    /// if (app.Environment.IsDevelopment())
    /// {
    ///     app.MapAccessibilityDevTools();
    /// }
    /// </code>
    /// </summary>
    /// <returns>The route group, for further conventions such as authorization.</returns>
    public static RouteGroupBuilder MapAccessibilityDevTools(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/" + BasePath);
        group.MapGet("/devtools.js", (HttpContext context) => Serve(context, Overlay.Value));
        group.MapGet("/axe.js", (HttpContext context) => Serve(context, Axe.Value));
        group.ExcludeFromDescription();
        return group;
    }

    private static IResult Serve(HttpContext context, Script script)
    {
        // Revalidated on every load, so that a package update is picked up without clearing the cache.
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.ETag = script.ETag;
        if (context.Request.Headers.IfNoneMatch.Contains(script.ETag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(script.Content, "text/javascript; charset=utf-8");
    }

    private sealed class Script
    {
        public Script(string text)
        {
            Content = Encoding.UTF8.GetBytes(text);
            ETag = "\"" + Convert.ToHexString(SHA256.HashData(Content), 0, 16) + "\"";
        }

        public byte[] Content { get; }

        public string ETag { get; }
    }
}
