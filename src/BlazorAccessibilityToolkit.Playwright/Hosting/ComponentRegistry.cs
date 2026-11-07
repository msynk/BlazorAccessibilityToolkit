using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;

namespace BlazorAccessibilityToolkit.Playwright.Hosting;

/// <summary>A cascading value supplied to a hosted component.</summary>
internal sealed record CascadingValueRegistration(Type ValueType, object? Value, string? Name);

/// <summary>A component registered for rendering by the host. Parameters are passed in-process (no serialization).</summary>
internal sealed class ComponentRegistration(Type componentType, IReadOnlyDictionary<string, object?> parameters, IReadOnlyList<CascadingValueRegistration> cascadingValues)
{
    private readonly TaskCompletionSource<(IComponent Instance, Func<Func<Task>, Task> Invoke)> _attached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Type ComponentType { get; } = componentType;

    public IReadOnlyDictionary<string, object?> Parameters { get; } = parameters;

    public IReadOnlyList<CascadingValueRegistration> CascadingValues { get; } = cascadingValues;

    public Task<(IComponent Instance, Func<Func<Task>, Task> Invoke)> Attached => _attached.Task;

    public void Attach(IComponent instance, Func<Func<Task>, Task> invoke) => _attached.TrySetResult((instance, invoke));
}

/// <summary>Holds components registered for rendering. Thread-safe; one per host.</summary>
internal sealed class ComponentRegistry
{
    private readonly ConcurrentDictionary<string, ComponentRegistration> _registrations = new(StringComparer.Ordinal);

    public string Register(ComponentRegistration registration)
    {
        var id = Guid.NewGuid().ToString("N");
        _registrations[id] = registration;
        return id;
    }

    public ComponentRegistration? Find(string? id) => id is not null && _registrations.TryGetValue(id, out var registration) ? registration : null;

    public void Remove(string id) => _registrations.TryRemove(id, out _);
}
