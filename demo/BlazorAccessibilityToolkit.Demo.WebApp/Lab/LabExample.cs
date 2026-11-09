namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>
/// A test that the showcase can run live. Its code is in <c>Lab/Examples</c> between <c>#region snippet:{Id}</c> and
/// <c>#endregion</c>; the page displays that region exactly as it runs.
/// </summary>
public sealed class LabExample
{
    /// <summary>Identifies the example and its snippet region.</summary>
    public required string Id { get; init; }

    /// <summary>The test code. <see cref="LabEnvironment"/> plays the role of a test fixture.</summary>
    public required Func<LabEnvironment, Task> Run { get; init; }

    /// <summary>True when the test needs a browser (axe-core, the interactive host, keyboard, focus, live regions, snapshots).</summary>
    public bool NeedsBrowser { get; init; }

    /// <summary>True when the test is expected to fail, because it checks a component with an intentional defect.</summary>
    public bool ExpectFailure { get; init; }

    /// <summary>The code shown on the page.</summary>
    public string Snippet => SourceCatalog.Snippet(Id);
}

/// <summary>How a live run ended.</summary>
public enum LabOutcome
{
    /// <summary>The test completed without an assertion failure.</summary>
    Passed,

    /// <summary>An assertion failed: the toolkit reported a problem.</summary>
    Failed,

    /// <summary>The test could not run, for example because no browser is installed.</summary>
    Unavailable,

    /// <summary>The test crashed or an audit could not execute.</summary>
    Error,
}

/// <summary>Something a test produced that the page can display (an audit, a pattern verification, a focus order ...).</summary>
public sealed record LabArtifact(object Value, string? Caption);

/// <summary>The result of a live run.</summary>
public sealed record LabResult(LabOutcome Outcome, string? Message, IReadOnlyList<LabArtifact> Artifacts, TimeSpan Duration)
{
    /// <summary>True when the outcome is the one the example expects: passing for accessible components, failing for defects.</summary>
    public bool AsExpected(LabExample example) => example.ExpectFailure ? Outcome == LabOutcome.Failed : Outcome == LabOutcome.Passed;
}

/// <summary>Formatted text an example wants to display, such as an ARIA snapshot.</summary>
public sealed record LabText(string Text, string Language = "text");
