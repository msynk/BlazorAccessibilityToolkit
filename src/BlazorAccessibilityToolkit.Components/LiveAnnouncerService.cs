using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorAccessibilityToolkit.Components;

/// <summary>How urgently assistive technologies announce a message.</summary>
public enum Politeness
{
    /// <summary>Announced when the user is idle (<c>aria-live="polite"</c>): status messages, results, confirmations.</summary>
    Polite,

    /// <summary>Announced immediately, interrupting speech (<c>aria-live="assertive"</c>): errors and time-critical warnings only.</summary>
    Assertive,
}

/// <summary>
/// Announces messages to screen reader users through the live regions of a <see cref="LiveAnnouncer"/> component, like
/// Angular CDK's <c>LiveAnnouncer</c>. Register it with <see cref="AccessibilityServiceCollectionExtensions.AddAccessibilityComponents"/>
/// and render <c>&lt;LiveAnnouncer /&gt;</c> once, in the layout, in an interactive render mode.
/// </summary>
public interface ILiveAnnouncer
{
    /// <summary>True when a <see cref="LiveAnnouncer"/> component is rendered and can announce.</summary>
    bool IsAttached { get; }

    /// <summary>
    /// Announces <paramref name="message"/>. Repeating the same message announces it again. Returns <c>false</c> when
    /// no <see cref="LiveAnnouncer"/> is rendered (for example during prerendering), so nothing was announced.
    /// </summary>
    Task<bool> AnnounceAsync(string message, Politeness politeness = Politeness.Polite);

    /// <summary>Clears both live regions.</summary>
    Task ClearAsync();
}

/// <summary>The default <see cref="ILiveAnnouncer"/>, connected to the rendered <see cref="LiveAnnouncer"/> component.</summary>
public sealed class LiveAnnouncerService : ILiveAnnouncer
{
    private LiveAnnouncer? _host;

    /// <inheritdoc />
    public bool IsAttached => _host is not null;

    /// <inheritdoc />
    public async Task<bool> AnnounceAsync(string message, Politeness politeness = Politeness.Polite)
    {
        ArgumentNullException.ThrowIfNull(message);
        var host = _host;
        if (host is null || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        await host.AnnounceAsync(message.Trim(), politeness).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task ClearAsync() => _host?.ClearAsync() ?? Task.CompletedTask;

    // The most recent host wins: when the layout changes, the new layout's host is initialized before the old one is
    // disposed. Messages always go to exactly one host, so nothing is announced twice.
    internal void Attach(LiveAnnouncer host) => _host = host;

    internal void Detach(LiveAnnouncer host)
    {
        if (ReferenceEquals(_host, host))
        {
            _host = null;
        }
    }
}

/// <summary>Registers the runtime accessibility services.</summary>
public static class AccessibilityServiceCollectionExtensions
{
    /// <summary>Adds <see cref="ILiveAnnouncer"/> (scoped: one per circuit or WebAssembly app).</summary>
    public static IServiceCollection AddAccessibilityComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<LiveAnnouncerService>();
        services.TryAddScoped<ILiveAnnouncer>(sp => sp.GetRequiredService<LiveAnnouncerService>());
        return services;
    }
}

/// <summary>Which arrow keys move focus in a <see cref="RovingFocusGroup"/>.</summary>
public enum RovingOrientation
{
    /// <summary>ArrowLeft and ArrowRight (toolbars, tabs).</summary>
    Horizontal,

    /// <summary>ArrowUp and ArrowDown (menus, listboxes, vertical tabs).</summary>
    Vertical,

    /// <summary>All four arrow keys (radio groups).</summary>
    Both,
}
