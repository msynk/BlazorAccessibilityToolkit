namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>A live test on the current page, as the "run all" panel sees it (implemented by the LabPanel component).</summary>
public interface ILabPanel
{
    LabExample Example { get; }

    /// <summary>The title of the example card.</summary>
    string Title { get; }

    /// <summary>The id of the example card's heading, for links to it.</summary>
    string AnchorId { get; }

    /// <summary>Runs the test as its button does. Returns <c>null</c> when it is already running or was cancelled.</summary>
    Task<LabResult?> RunAsync();
}

/// <summary>
/// The live tests of the current page, in page order, so that one button can run them all. Scoped, so each circuit (each
/// browser tab) has its own. Panels register when they render and unregister when the page is left.
/// </summary>
public sealed class LabPage
{
    private readonly List<ILabPanel> panels = [];

    /// <summary>Raised when a panel registers or unregisters.</summary>
    public event Action? Changed;

    public IReadOnlyList<ILabPanel> Panels => panels;

    public void Register(ILabPanel panel)
    {
        panels.Add(panel);
        Changed?.Invoke();
    }

    public void Unregister(ILabPanel panel)
    {
        if (panels.Remove(panel))
        {
            Changed?.Invoke();
        }
    }
}
