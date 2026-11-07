namespace BlazorAccessibilityToolkit.Engines;

/// <summary>
/// An engine that audits static rendered markup (for example markup produced by bUnit). Engines receive markup in which
/// every element carries a <see cref="MarkupAuditRequest.OrdinalAttribute"/> attribute and must report the ordinal of
/// each finding's element in <see cref="FindingTarget.ElementOrdinal"/> so the caller can attribute it to a component.
/// </summary>
/// <remarks>
/// The Playwright package provides a browser-backed implementation that runs axe-core and the toolkit's browser
/// diagnostics. Implementations must report problems they could not check as <see cref="ExecutionError"/>s instead
/// of returning an empty (passing) result.
/// </remarks>
public interface IMarkupAuditEngine
{
    /// <summary>Engine display name.</summary>
    string Name { get; }

    /// <summary>Audits the markup.</summary>
    Task<MarkupEngineResult> AuditAsync(MarkupAuditRequest request, CancellationToken cancellationToken = default);
}

/// <summary>How the static markup is wrapped into a document before it is audited.</summary>
public sealed record MarkupDocumentOptions
{
    /// <summary>Default document: <c>lang="en"</c>, a title, no styles.</summary>
    public static MarkupDocumentOptions Default { get; } = new();

    /// <summary>Value of the <c>lang</c> attribute of the html element.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Document title.</summary>
    public string Title { get; init; } = "Component under test";

    /// <summary>Paths of CSS files inlined into the document (needed for meaningful color-contrast results).</summary>
    public IReadOnlyList<string> StylesheetFiles { get; init; } = [];

    /// <summary>CSS text inlined into the document.</summary>
    public IReadOnlyList<string> Styles { get; init; } = [];

    /// <summary>Optional element that wraps the component markup, for example <c>main</c>. Default: none (markup is the body content).</summary>
    public string? WrapperElement { get; init; }
}

/// <summary>Input for <see cref="IMarkupAuditEngine"/>.</summary>
public sealed record MarkupAuditRequest
{
    /// <summary>Attribute that carries each element's document-order ordinal.</summary>
    public const string OrdinalAttribute = "data-a11y-ord";

    /// <summary>Component markup (body content) with ordinal attributes.</summary>
    public required string BodyHtml { get; init; }

    /// <summary>Rule selection.</summary>
    public AccessibilityAuditOptions Options { get; init; } = AccessibilityAuditOptions.Default;

    /// <summary>What the markup represents (component markup disables page-level rules).</summary>
    public AuditTargetKind Kind { get; init; } = AuditTargetKind.Component;

    /// <summary>Document wrapper settings.</summary>
    public MarkupDocumentOptions Document { get; init; } = MarkupDocumentOptions.Default;
}

/// <summary>Output of <see cref="IMarkupAuditEngine"/>.</summary>
public sealed record MarkupEngineResult
{
    /// <summary>Findings with <see cref="FindingTarget.ElementOrdinal"/> set where possible.</summary>
    public IReadOnlyList<AccessibilityFinding> Findings { get; init; } = [];

    /// <summary>Rules that passed.</summary>
    public IReadOnlyList<string> PassedRules { get; init; } = [];

    /// <summary>Rules that did not apply.</summary>
    public IReadOnlyList<string> InapplicableRules { get; init; } = [];

    /// <summary>Engines that ran.</summary>
    public IReadOnlyList<EngineInfo> Engines { get; init; } = [];

    /// <summary>Errors that prevented a complete audit.</summary>
    public IReadOnlyList<ExecutionError> Errors { get; init; } = [];

    /// <summary>Warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Browser name, when a browser was used.</summary>
    public string? Browser { get; init; }

    /// <summary>Browser version, when a browser was used.</summary>
    public string? BrowserVersion { get; init; }
}
