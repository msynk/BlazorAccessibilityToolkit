using Microsoft.JSInterop;

namespace BlazorAccessibilityToolkit.Components;

/// <summary>Loads the package's JavaScript module once per component and disposes it safely.</summary>
internal sealed class AccessibilityModule(IJSRuntime js) : IAsyncDisposable
{
    internal const string Path = "./_content/BlazorAccessibilityToolkit.Components/accessibility.js";

    private Task<IJSObjectReference>? _module;

    public async ValueTask<T> InvokeAsync<T>(string identifier, params object?[] args)
    {
        var module = await (_module ??= js.InvokeAsync<IJSObjectReference>("import", Path).AsTask()).ConfigureAwait(false);
        return await module.InvokeAsync<T>(identifier, args).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await (await _module.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
        {
            // The circuit or page is gone, or the module never loaded: nothing to clean up.
        }
    }
}
