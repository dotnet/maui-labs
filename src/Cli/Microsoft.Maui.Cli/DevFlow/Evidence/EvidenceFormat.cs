namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// Wire constants for the <c>.mauitrace</c> evidence bundle: a small, versioned ZIP of
/// on-demand diagnostic evidence captured from a running app.
///
/// The format is intentionally closed: only the entries in <see cref="AllowedEntries"/> may be
/// written or read, and every limit here is enforced on BOTH the write path (so we never produce
/// an unbounded bundle) and the read path (so a hostile bundle can never expand into one).
/// </summary>
internal static class EvidenceFormat
{
    /// <summary>Schema discriminator written to (and required from) <c>manifest.json</c>.</summary>
    public const string SchemaId = "maui-devflow-evidence";

    /// <summary>
    /// Bundle format version. Bump when entry names or required manifest fields change.
    /// <para>
    /// The reader requires an exact match, so a bump rejects every previously captured bundle.
    /// Additive optional entries therefore do not bump it: keeping old evidence readable matters
    /// more than signalling an addition that older tools would reject anyway.
    /// </para>
    /// </summary>
    public const int Version = 1;

    public const string FileExtension = ".mauitrace";

    /// <summary>Project-local folder used when a project root can be resolved.</summary>
    public const string DefaultFolderName = "maui-traces";

    public const string ManifestEntry = "manifest.json";
    public const string EnvironmentEntry = "environment.json";
    public const string TreeEntry = "tree.json";
    public const string LayoutEntry = "layout.json";
    public const string ProblemsEntry = "problems.json";
    public const string LogsEntry = "logs.json";
    public const string NetworkEntry = "network.json";
    public const string ScreenshotEntry = "screenshot.png";
    public const string WorkflowEntry = "workflow.md";

    /// <summary>
    /// The device the app was running on. Optional and additive: it answers "what was this
    /// actually running on?", which is otherwise guesswork when reading someone else's bundle.
    /// </summary>
    public const string DeviceEntry = "device.json";

    /// <summary>The complete set of entry names a bundle may contain (ordinal match, flat names only).</summary>
    public static readonly IReadOnlyList<string> AllowedEntries =
    [
        ManifestEntry,
        EnvironmentEntry,
        TreeEntry,
        LayoutEntry,
        ProblemsEntry,
        LogsEntry,
        NetworkEntry,
        ScreenshotEntry,
        WorkflowEntry,
        DeviceEntry,
    ];

    // ── Capture-side bounds ──────────────────────────────────────────────────────────────────

    public const int DefaultLogLimit = 200;
    public const int MaxLogLimit = 500;
    public const int DefaultNetworkLimit = 100;
    public const int MaxNetworkLimit = 500;
    public const int MaxProblems = 500;
    public const int MaxTreeElements = 5_000;
    public const int MaxTreeDepth = 64;

    /// <summary>Elements the bundled layout scan may examine.</summary>
    public const int MaxLayoutElements = 2_000;

    /// <summary>Layout findings retained in a bundle. Findings are bounded metadata, but not unbounded.</summary>
    public const int MaxLayoutFindings = 500;

    /// <summary>Free-form strings inside a layout finding (message, explanation, limitation).</summary>
    public const int MaxLayoutTextChars = 600;
    public const int MaxLogMessageChars = 1_000;
    public const int MaxProblemMessageChars = 1_000;
    public const int MaxErrorChars = 400;
    public const int MaxIdentifierChars = 128;
    public const int MaxQueryKeys = 24;

    /// <summary>Workflow markdown is user-supplied; cap it before it ever reaches the bundle.</summary>
    public const long MaxWorkflowBytes = 1_048_576; // 1 MB

    public const long MaxScreenshotBytes = 16L * 1024 * 1024;

    // ── Read-side bounds (input is treated as hostile) ───────────────────────────────────────

    public const long MaxBundleFileBytes = 64L * 1024 * 1024;
    public const int MaxBundleEntries = 16;
    public const long MaxTotalUncompressedBytes = 128L * 1024 * 1024;
    public const long MaxEntryUncompressedBytes = 32L * 1024 * 1024;
    public const long MaxManifestBytes = 1L * 1024 * 1024;
    public const long MaxEnvironmentBytes = 1L * 1024 * 1024;
    public const long MaxTreeBytes = 16L * 1024 * 1024;
    public const long MaxLayoutBytes = 4L * 1024 * 1024;
    public const long MaxProblemsBytes = 4L * 1024 * 1024;
    public const long MaxLogsBytes = 4L * 1024 * 1024;
    public const long MaxNetworkBytes = 4L * 1024 * 1024;

    /// <summary>Maximum uncompressed:compressed ratio tolerated for a single entry (zip-bomb guard).</summary>
    public const int MaxCompressionRatio = 200;

    /// <summary>Entries smaller than this compress unpredictably, so the ratio guard ignores them.</summary>
    public const long RatioCheckMinCompressedBytes = 1_024;

    /// <summary>Data classes that are never captured, surfaced verbatim in previews and manifests.</summary>
    public static readonly IReadOnlyList<string> NeverIncluded =
    [
        "Element Text/Value content",
        "Native and framework property dictionaries",
        "BindingContext / view-model object graphs",
        "Preferences and secure storage values",
        "Geolocation",
        "File contents from app storage",
        "Absolute user/machine file paths",
        "HTTP headers, bodies, and full query strings",
    ];
}
