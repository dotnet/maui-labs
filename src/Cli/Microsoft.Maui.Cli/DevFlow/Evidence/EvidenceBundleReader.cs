using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>Everything a validated bundle yielded. Any field may be null if the entry was absent.</summary>
internal sealed class EvidenceReadResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Path { get; init; }
    public EvidenceManifest? Manifest { get; init; }
    public EvidenceEnvironment? Environment { get; init; }
    public EvidenceTreeDocument? Tree { get; init; }
    public EvidenceLayoutDocument? Layout { get; init; }
    public EvidenceProblemDocument? Problems { get; init; }
    public EvidenceLogDocument? Logs { get; init; }
    public EvidenceNetworkDocument? Network { get; init; }
    public string? Workflow { get; init; }
    public byte[]? Screenshot { get; init; }
    public List<string> Entries { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    public static EvidenceReadResult Fail(string error, string? path = null)
        => new() { Ok = false, Error = error, Path = path };
}

/// <summary>
/// Reads a <c>.mauitrace</c> bundle as HOSTILE input.
///
/// Every structural property is checked before any content is decompressed: entry names are
/// allow-listed and must be flat (no separators, traversal, or rooted paths), entries may not
/// repeat, and entry count / per-entry size / total size / compression ratio are all bounded.
/// Content is then parsed with a strict shape check. Nothing in a bundle is ever executed —
/// <c>workflow.md</c> is treated as inert text and the report is regenerated from scratch.
/// </summary>
internal static class EvidenceBundleReader
{
    public static EvidenceReadResult Read(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return EvidenceReadResult.Fail($"Bundle not found: {path}", path);
            if (info.Length == 0) return EvidenceReadResult.Fail("Bundle is empty.", path);
            if (info.Length > EvidenceFormat.MaxBundleFileBytes)
                return EvidenceReadResult.Fail("Bundle is larger than the supported maximum.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return EvidenceReadResult.Fail($"Could not open the bundle: {ex.Message}", path);
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream, path, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return EvidenceReadResult.Fail("Bundle is not a readable evidence archive.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return EvidenceReadResult.Fail($"Could not read the bundle: {ex.Message}", path);
        }
    }

    public static EvidenceReadResult Read(
        Stream stream,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return EvidenceReadResult.Fail("Bundle is not a readable evidence archive.", path);
        }

        using (archive)
        {
            try
            {
                return ReadArchive(archive, path, cancellationToken);
            }
            catch (EvidenceReadException ex)
            {
                return EvidenceReadResult.Fail(ex.Message, path);
            }
            catch (InvalidDataException)
            {
                // Corrupt deflate stream or CRC mismatch mid-entry.
                return EvidenceReadResult.Fail("Bundle is not a readable evidence archive.", path);
            }
        }
    }

    private static EvidenceReadResult ReadArchive(
        ZipArchive archive,
        string? path,
        CancellationToken cancellationToken)
    {
        {
            if (archive.Entries.Count == 0)
                return EvidenceReadResult.Fail("Bundle contains no entries.", path);
            if (archive.Entries.Count > EvidenceFormat.MaxBundleEntries)
                return EvidenceReadResult.Fail("Bundle contains too many entries.", path);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long declaredTotal = 0;

            // Cheap pre-filter over the central directory. Everything here is attacker-declared, so
            // the real limits are enforced against the actual decompressed bytes in ReadBounded.
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nameError = ValidateEntryName(entry.FullName);
                if (nameError is not null) return EvidenceReadResult.Fail(nameError, path);
                if (!seen.Add(entry.FullName))
                    return EvidenceReadResult.Fail($"Bundle contains duplicate entry '{entry.FullName}'.", path);

                var entryLimit = EntryLimit(entry.FullName);
                if (entry.Length < 0 || entry.Length > entryLimit)
                    return EvidenceReadResult.Fail($"Entry '{entry.FullName}' is larger than the supported maximum.", path);

                declaredTotal += entry.Length;
                if (declaredTotal > EvidenceFormat.MaxTotalUncompressedBytes)
                    return EvidenceReadResult.Fail("Bundle expands beyond the supported maximum size.", path);
            }

            var budget = new ReadBudget();

            var manifestEntry = archive.GetEntry(EvidenceFormat.ManifestEntry);
            if (manifestEntry is null)
                return EvidenceReadResult.Fail("Bundle is missing manifest.json.", path);

            var manifestJson = DecodeUtf8(ReadBounded(
                manifestEntry,
                budget,
                EntryLimit(EvidenceFormat.ManifestEntry),
                cancellationToken));
            if (manifestJson is null) return EvidenceReadResult.Fail("manifest.json is not valid UTF-8 text.", path);

            var manifestError = ValidateManifestShape(manifestJson);
            if (manifestError is not null) return EvidenceReadResult.Fail(manifestError, path);

            EvidenceManifest? manifest;
            try { manifest = EvidenceJson.Deserialize<EvidenceManifest>(manifestJson); }
            catch (JsonException) { return EvidenceReadResult.Fail("manifest.json could not be parsed.", path); }
            if (manifest is null) return EvidenceReadResult.Fail("manifest.json could not be parsed.", path);

            var warnings = new List<string>();
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName == EvidenceFormat.ManifestEntry)
                    continue;
                contents[entry.FullName] = ReadBounded(
                    entry,
                    budget,
                    EntryLimit(entry.FullName),
                    cancellationToken);
            }

            var integrityError = ValidateManifestEntries(manifest, contents);
            if (integrityError is not null)
                return EvidenceReadResult.Fail(integrityError, path);

            var environment = ReadJsonEntry<EvidenceEnvironment>(contents, EvidenceFormat.EnvironmentEntry, warnings);
            var tree = ReadJsonEntry<EvidenceTreeDocument>(contents, EvidenceFormat.TreeEntry, warnings);
            var layout = ReadJsonEntry<EvidenceLayoutDocument>(contents, EvidenceFormat.LayoutEntry, warnings);
            var problems = ReadJsonEntry<EvidenceProblemDocument>(contents, EvidenceFormat.ProblemsEntry, warnings);
            var logs = ReadJsonEntry<EvidenceLogDocument>(contents, EvidenceFormat.LogsEntry, warnings);
            var network = ReadJsonEntry<EvidenceNetworkDocument>(contents, EvidenceFormat.NetworkEntry, warnings);

            string? workflow = null;
            if (contents.TryGetValue(EvidenceFormat.WorkflowEntry, out var workflowBytes))
            {
                if (workflowBytes.LongLength > EvidenceFormat.MaxWorkflowBytes)
                {
                    warnings.Add("workflow.md was ignored: it exceeds the workflow size limit.");
                }
                else
                {
                    workflow = DecodeUtf8(workflowBytes);
                    if (workflow is null) warnings.Add("workflow.md was ignored: it is not valid UTF-8 text.");
                }
            }

            byte[]? screenshot = null;
            if (contents.TryGetValue(EvidenceFormat.ScreenshotEntry, out var screenshotBytes))
            {
                if (screenshotBytes.LongLength > EvidenceFormat.MaxScreenshotBytes)
                    warnings.Add("screenshot.png was ignored: it exceeds the screenshot size limit.");
                else if (!IsPng(screenshotBytes)) warnings.Add("screenshot.png was ignored: it is not a PNG image.");
                else screenshot = screenshotBytes;
            }

            return new EvidenceReadResult
            {
                Ok = true,
                Path = path,
                Manifest = manifest,
                Environment = environment,
                Tree = tree,
                Layout = layout,
                Problems = problems,
                Logs = logs,
                Network = network,
                Workflow = workflow,
                Screenshot = screenshot,
                Entries = [.. archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal)],
                Warnings = warnings,
            };
        }
    }

    /// <summary>
    /// Entry names must be one of the allow-listed flat names. This rejects traversal
    /// (<c>../</c>), rooted paths, drive letters, nested directories, and unknown files outright.
    /// </summary>
    internal static string? ValidateEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "Bundle contains an entry with no name.";
        if (name.Length > 128)
            return "Bundle contains an entry name that is too long.";
        if (name.Contains('\0') || name.Any(char.IsControl))
            return "Bundle contains an entry name with invalid characters.";
        if (name.Contains('/') || name.Contains('\\'))
            return $"Bundle contains a nested or traversing entry '{name}'.";
        if (name.Contains("..", StringComparison.Ordinal))
            return $"Bundle contains a traversing entry '{name}'.";
        if (name.Length >= 2 && name[1] == ':')
            return $"Bundle contains a rooted entry '{name}'.";
        if (!EvidenceFormat.AllowedEntries.Contains(name, StringComparer.Ordinal))
            return $"Bundle contains an unexpected entry '{name}'.";
        return null;
    }

    /// <summary>Structural check of manifest.json before it is bound to the typed model.</summary>
    internal static string? ValidateManifestShape(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = EvidenceJson.MaxJsonDepth }); }
        catch (JsonException) { return "manifest.json is not valid JSON."; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "manifest.json must be a JSON object.";

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String ||
                !string.Equals(schema.GetString(), EvidenceFormat.SchemaId, StringComparison.Ordinal))
            {
                return "manifest.json is not a MAUI DevFlow evidence manifest.";
            }

            if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var formatVersion))
            {
                return "manifest.json is missing a numeric formatVersion.";
            }
            if (formatVersion != EvidenceFormat.Version)
                return $"Unsupported evidence format version {formatVersion} (this tool reads version {EvidenceFormat.Version}).";

            if (!root.TryGetProperty("capturedUtc", out var captured) || captured.ValueKind != JsonValueKind.String)
                return "manifest.json is missing capturedUtc.";

            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind != JsonValueKind.Array)
                return "manifest.json entries must be an array.";
        }

        return null;
    }

    private static string? ValidateManifestEntries(
        EvidenceManifest manifest,
        IReadOnlyDictionary<string, byte[]> contents)
    {
        if (manifest.Entries is null)
            return "manifest.json entries are missing.";
        if (manifest.Capabilities is null || manifest.Excluded is null ||
            manifest.NeverIncluded is null || manifest.Warnings is null)
        {
            return "manifest.json contains null collections.";
        }
        if (manifest.Capabilities.Count > 64 || manifest.Excluded.Count > EvidenceFormat.MaxBundleEntries ||
            manifest.NeverIncluded.Count > 64 || manifest.Warnings.Count > 256)
        {
            return "manifest.json contains an oversized collection.";
        }
        if (InvalidText(manifest.CapturedUtc, 64) ||
            !DateTimeOffset.TryParse(
                manifest.CapturedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _) ||
            manifest.Source is not ("cli" or "mcp" or "inspector") ||
            InvalidText(manifest.SelectedElementId, EvidenceFormat.MaxIdentifierChars))
        {
            return "manifest.json contains invalid source, timestamp, or text metadata.";
        }
        if (manifest.Excluded.Any(static exclusion =>
                exclusion is null ||
                InvalidText(exclusion.Name, 128) ||
                InvalidText(exclusion.Reason, 1_000)) ||
            manifest.Capabilities.Any(static value => InvalidText(value, 128)) ||
            manifest.NeverIncluded.Any(static value => InvalidText(value, 256)) ||
            manifest.Warnings.Any(static value => InvalidText(value, 2_000)))
        {
            return "manifest.json contains oversized metadata.";
        }
        if (!ValidFlowRunLink(manifest.FlowRun))
            return "manifest.json contains invalid flow-run linkage metadata.";

        var manifestEntries = new Dictionary<string, EvidenceEntryInfo>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null)
                return "manifest.json describes a null evidence entry.";
            if (string.IsNullOrWhiteSpace(entry.Name)
                || !EvidenceFormat.AllowedEntries.Contains(entry.Name, StringComparer.Ordinal)
                || entry.Name == EvidenceFormat.ManifestEntry)
            {
                return "manifest.json describes an invalid evidence entry.";
            }
            if (!manifestEntries.TryAdd(entry.Name, entry))
                return $"manifest.json describes duplicate entry '{entry.Name}'.";
            if (entry.Bytes < 0 || entry.Bytes > EntryLimit(entry.Name))
                return $"manifest.json describes an oversized entry '{entry.Name}'.";
            if (entry.Count is < 0)
                return $"manifest.json describes an invalid count for '{entry.Name}'.";
            if (entry.Description?.Length > 1_000)
                return $"manifest.json describes an entry with an oversized description.";
        }

        if (manifestEntries.Count != contents.Count
            || contents.Keys.Any(name => !manifestEntries.ContainsKey(name)))
        {
            return "Bundle contents do not match the entries declared by manifest.json.";
        }

        foreach (var pair in contents)
        {
            var declared = manifestEntries[pair.Key];
            if (declared.Bytes != pair.Value.LongLength)
                return $"Entry '{pair.Key}' size does not match manifest.json.";
            if (declared.Sha256 is not { Length: 64 } ||
                declared.Sha256.Any(static character => !Uri.IsHexDigit(character)))
                return $"Entry '{pair.Key}' has an invalid integrity hash.";

            var actualHash = Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant();
            if (!string.Equals(actualHash, declared.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"Entry '{pair.Key}' integrity hash does not match manifest.json.";
        }

        return null;
    }

    private static bool ValidFlowRunLink(EvidenceFlowRunLink? link)
    {
        if (link is null)
            return true;
        if (InvalidText(link.RunId, 128) ||
            InvalidText(link.FailedStepId, 128) ||
            InvalidText(link.FailureCode, 128) ||
            InvalidText(link.ReportDigest, 160) ||
            InvalidText(link.ReportReference, 160) ||
            InvalidText(link.CaptureCompleteness, 128) ||
            InvalidText(link.ReportPath, 512))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(link.ReportPath))
            return true;
        return !Path.IsPathRooted(link.ReportPath) &&
            !link.ReportPath.Contains("..", StringComparison.Ordinal) &&
            !link.ReportPath.Contains(':', StringComparison.Ordinal);
    }

    private static string? ValidateSection(object? value) => value switch
    {
        null => "it could not be deserialized.",
        EvidenceEnvironment environment => ValidateEnvironment(environment),
        EvidenceTreeDocument tree => ValidateTree(tree),
        EvidenceLayoutDocument layout => ValidateLayout(layout),
        EvidenceProblemDocument problems => ValidateProblems(problems),
        EvidenceLogDocument logs => ValidateLogs(logs),
        EvidenceNetworkDocument network => ValidateNetwork(network),
        _ => null,
    };

    private static string? ValidateEnvironment(EvidenceEnvironment environment)
    {
        if (environment.Capabilities is null)
            return "capabilities must be an array.";
        if (environment.Capabilities.Count > 64 ||
            environment.Capabilities.Any(static value => TooLong(value, 128)))
        {
            return "capabilities exceed the supported bounds.";
        }
        if (TooLong(environment.CapturedUtc, 64) || TooLong(environment.Route, 512) ||
            TooLong(environment.Checkpoint?.Route, 512))
        {
            return "a string exceeds the supported bounds.";
        }
        var display = environment.Display;
        if (display is not null &&
            (!IsFinite(display.Width) || !IsFinite(display.Height) || !IsFinite(display.Density) ||
             !IsFinite(display.RefreshRate)))
        {
            return "display metrics must be finite.";
        }
        return null;
    }

    private static string? ValidateTree(EvidenceTreeDocument tree)
    {
        if (tree.Roots is null)
            return "roots must be an array.";
        if (tree.Count < 0 || tree.Count > EvidenceFormat.MaxTreeElements ||
            tree.MaxDepth < 0 || tree.MaxDepth > EvidenceFormat.MaxTreeDepth)
        {
            return "tree counts exceed the supported bounds.";
        }

        var count = 0;
        var stack = new Stack<(EvidenceTreeNode Node, int Depth)>();
        for (var index = tree.Roots.Count - 1; index >= 0; index--)
        {
            var root = tree.Roots[index];
            if (root is null) return "roots contains a null element.";
            stack.Push((root, 1));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            count++;
            if (count > EvidenceFormat.MaxTreeElements || depth > EvidenceFormat.MaxTreeDepth)
                return "tree depth or element count exceeds the supported bounds.";
            if (TooLong(node.Id, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(node.Type, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(node.Framework, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(node.AutomationId, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(node.Role, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(node.SourceFile, 512) ||
                TooLong(node.SourceHash, 128) ||
                !ValidBounds(node.Bounds))
            {
                return "a tree node exceeds the supported bounds.";
            }
            if (node.Children is null)
                continue;
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                var child = node.Children[index];
                if (child is null) return "a tree node contains a null child.";
                stack.Push((child, depth + 1));
            }
        }
        return null;
    }

    private static string? ValidateLayout(EvidenceLayoutDocument layout)
    {
        if (layout.Rules is null || layout.Findings is null ||
            layout.Limitations is null || layout.NeverCaptured is null)
        {
            return "required collections must be arrays.";
        }
        if (layout.Rules.Count > 64 || layout.Findings.Count > EvidenceFormat.MaxLayoutFindings ||
            layout.Limitations.Count > 256 || layout.NeverCaptured.Count > 128)
        {
            return "a collection exceeds the supported bounds.";
        }
        if (layout.ElementsExamined < 0 || layout.ElementsExamined > EvidenceFormat.MaxLayoutElements ||
            layout.Violations < 0 || layout.Observations < 0 || layout.Incomplete < 0 ||
            layout.Passes < 0 || layout.NotApplicable < 0 || layout.Suppressed < 0 ||
            layout.FindingCount < 0 || layout.FindingCount > EvidenceFormat.MaxLayoutFindings)
        {
            return "layout counts exceed the supported bounds.";
        }
        if (layout.Rules.Any(static rule =>
                rule is null ||
                TooLong(rule.RuleId, 128) ||
                TooLong(rule.Support, 32) ||
                TooLong(rule.Confidence, 32) ||
                rule.Evaluated < 0 ||
                rule.Skipped < 0))
        {
            return "a layout rule is invalid.";
        }
        foreach (var finding in layout.Findings)
        {
            if (finding is null || finding.Limitations is null ||
                finding.RelatedElementIds is null || finding.FixCategories is null ||
                finding.Limitations.Count > 64 ||
                finding.RelatedElementIds.Count > 64 ||
                finding.FixCategories.Count > 64 ||
                TooLong(finding.Id, 256) ||
                TooLong(finding.RuleId, 128) ||
                TooLong(finding.Outcome, 32) ||
                TooLong(finding.Confidence, 32) ||
                TooLong(finding.Severity, 32) ||
                TooLong(finding.Actionability, 32) ||
                TooLong(finding.SuppressionKey, 256) ||
                TooLong(finding.SuppressionReason, EvidenceFormat.MaxLayoutTextChars) ||
                TooLong(finding.Message, EvidenceFormat.MaxLayoutTextChars) ||
                TooLong(finding.Explanation, EvidenceFormat.MaxLayoutTextChars) ||
                TooLong(finding.ElementId, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(finding.ElementType, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(finding.AutomationId, EvidenceFormat.MaxIdentifierChars) ||
                TooLong(finding.SourceFile, 512) ||
                !ValidBounds(finding.Bounds) ||
                finding.RelatedElementIds.Any(static id => TooLong(id, EvidenceFormat.MaxIdentifierChars)) ||
                finding.FixCategories.Any(static category => TooLong(category, 128)) ||
                finding.Limitations.Any(static text => TooLong(text, EvidenceFormat.MaxLayoutTextChars)))
            {
                return "a layout finding exceeds the supported bounds.";
            }
        }
        if (TooLong(layout.SnapshotId, 256) ||
            TooLong(layout.TreeRevision, 256) ||
            TooLong(layout.DiagnosticsRevision, 256) ||
            layout.Limitations.Any(static text => TooLong(text, EvidenceFormat.MaxLayoutTextChars)) ||
            layout.NeverCaptured.Any(static text => TooLong(text, 256)))
        {
            return "layout metadata exceeds the supported bounds.";
        }
        if (layout.SystemEvidence is { } system &&
            (system.Limitations is null ||
             system.Limitations.Count > 128 ||
             TooLong(system.Status, 32) ||
             TooLong(system.DeviceId, 256) ||
             TooLong(system.CapturedUtc, 64) ||
             TooLong(system.ForegroundOwner, 256) ||
             TooLong(system.ScreenshotDigest, 128) ||
             system.ExternalElementCount < 0 ||
             system.ExternalElementCount > 10_000 ||
             system.Limitations.Any(static text => TooLong(text, EvidenceFormat.MaxLayoutTextChars))))
        {
            return "layout system evidence exceeds the supported bounds.";
        }
        return null;
    }

    private static string? ValidateProblems(EvidenceProblemDocument document)
    {
        if (document.Problems is null)
            return "problems must be an array.";
        if (document.Problems.Count > EvidenceFormat.MaxProblems)
            return "problem count exceeds the supported bounds.";
        foreach (var problem in document.Problems)
        {
            if (problem is null ||
                TooLong(problem.Id, 256) ||
                TooLong(problem.Kind, 128) ||
                TooLong(problem.Severity, 32) ||
                TooLong(problem.Code, 128) ||
                TooLong(problem.Message, EvidenceFormat.MaxProblemMessageChars) ||
                TooLong(problem.BindingPath, 512) ||
                TooLong(problem.SourceFile, 512) ||
                problem.Count < 0)
            {
                return "a problem exceeds the supported bounds.";
            }
        }
        return null;
    }

    private static string? ValidateLogs(EvidenceLogDocument document)
    {
        if (document.Entries is null)
            return "entries must be an array.";
        if (document.Entries.Count > EvidenceFormat.MaxLogLimit)
            return "log count exceeds the supported bounds.";
        foreach (var entry in document.Entries)
        {
            if (entry is null ||
                TooLong(entry.Timestamp, 64) ||
                TooLong(entry.Level, 32) ||
                TooLong(entry.Category, 256) ||
                TooLong(entry.Message, EvidenceFormat.MaxLogMessageChars) ||
                TooLong(entry.Exception, EvidenceFormat.MaxLogMessageChars) ||
                TooLong(entry.Source, 128))
            {
                return "a log entry exceeds the supported bounds.";
            }
        }
        return null;
    }

    private static string? ValidateNetwork(EvidenceNetworkDocument document)
    {
        if (document.Requests is null)
            return "requests must be an array.";
        if (document.Requests.Count > EvidenceFormat.MaxNetworkLimit)
            return "network request count exceeds the supported bounds.";
        foreach (var request in document.Requests)
        {
            if (request is null ||
                TooLong(request.Timestamp, 64) ||
                TooLong(request.Method, 32) ||
                TooLong(request.Host, 256) ||
                TooLong(request.Path, 2_048) ||
                TooLong(request.StatusText, 256) ||
                TooLong(request.RequestContentType, 256) ||
                TooLong(request.ResponseContentType, 256) ||
                TooLong(request.Error, EvidenceFormat.MaxErrorChars) ||
                request.QueryKeys is { Count: > EvidenceFormat.MaxQueryKeys } ||
                request.QueryKeys?.Any(static key => TooLong(key, 64)) == true ||
                request.DurationMs < 0 ||
                request.RequestBytes < 0 ||
                request.ResponseBytes < 0)
            {
                return "a network request exceeds the supported bounds.";
            }
        }
        return null;
    }

    private static bool ValidBounds(EvidenceBounds? bounds)
        => bounds is null ||
           (double.IsFinite(bounds.X) &&
            double.IsFinite(bounds.Y) &&
            double.IsFinite(bounds.Width) &&
            double.IsFinite(bounds.Height));

    private static bool IsFinite(double? value)
        => value is null || double.IsFinite(value.Value);

    private static bool TooLong(string? value, int max)
        => value?.Length > max;

    private static bool InvalidText(string? value, int max)
        => TooLong(value, max) ||
           value?.Any(static character =>
               char.IsControl(character) ||
               char.GetUnicodeCategory(character) == UnicodeCategory.Format) == true;

    private static T? ReadJsonEntry<T>(
        IReadOnlyDictionary<string, byte[]> contents,
        string name,
        List<string> warnings)
        where T : class
    {
        if (!contents.TryGetValue(name, out var bytes))
            return null;

        var json = DecodeUtf8(bytes);
        if (json is null)
        {
            warnings.Add($"{name} was ignored: it is not valid UTF-8 text.");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = EvidenceJson.MaxJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{name} was ignored: unexpected JSON shape.");
                return null;
            }
            var value = EvidenceJson.Deserialize<T>(json);
            var semanticError = ValidateSection(value);
            if (semanticError is not null)
            {
                warnings.Add($"{name} was ignored: {semanticError}");
                return null;
            }
            return value;
        }
        catch (JsonException)
        {
            warnings.Add($"{name} was ignored: it could not be parsed.");
            return null;
        }
    }

    private sealed class ReadBudget
    {
        public long TotalBytes;
    }

    /// <summary>Signals a structural violation that must fail the whole read.</summary>
    private sealed class EvidenceReadException(string message) : Exception(message);

    /// <summary>
    /// Copies an entry while enforcing the real limits against the ACTUAL decompressed bytes: the
    /// declared entry length in the central directory is attacker-controlled, so a bundle that lies
    /// about its sizes cannot use the cheap pre-filter to smuggle a decompression bomb past the
    /// per-entry cap, the cumulative cap, or the ratio guard.
    /// </summary>
    private static byte[] ReadBounded(
        ZipArchiveEntry entry,
        ReadBudget budget,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var source = entry.Open();
        using var buffer = new MemoryStream();

        var chunk = new byte[81_920];
        long actual = 0;
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actual += read;
            budget.TotalBytes += read;
            if (actual > maxBytes)
                throw new EvidenceReadException($"Entry '{entry.FullName}' is larger than the supported maximum.");
            if (budget.TotalBytes > EvidenceFormat.MaxTotalUncompressedBytes)
                throw new EvidenceReadException("Bundle expands beyond the supported maximum size.");
            buffer.Write(chunk, 0, read);
        }

        if (entry.CompressedLength >= EvidenceFormat.RatioCheckMinCompressedBytes &&
            actual / Math.Max(entry.CompressedLength, 1) > EvidenceFormat.MaxCompressionRatio)
        {
            throw new EvidenceReadException($"Entry '{entry.FullName}' has a suspicious compression ratio.");
        }

        return buffer.ToArray();
    }

    private static long EntryLimit(string name) => name switch
    {
        EvidenceFormat.ManifestEntry => EvidenceFormat.MaxManifestBytes,
        EvidenceFormat.EnvironmentEntry => EvidenceFormat.MaxEnvironmentBytes,
        EvidenceFormat.TreeEntry => EvidenceFormat.MaxTreeBytes,
        EvidenceFormat.LayoutEntry => EvidenceFormat.MaxLayoutBytes,
        EvidenceFormat.ProblemsEntry => EvidenceFormat.MaxProblemsBytes,
        EvidenceFormat.LogsEntry => EvidenceFormat.MaxLogsBytes,
        EvidenceFormat.NetworkEntry => EvidenceFormat.MaxNetworkBytes,
        EvidenceFormat.WorkflowEntry => EvidenceFormat.MaxWorkflowBytes,
        EvidenceFormat.ScreenshotEntry => EvidenceFormat.MaxScreenshotBytes,
        _ => EvidenceFormat.MaxEntryUncompressedBytes,
    };

    private static string? DecodeUtf8(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsPng(byte[] bytes)
        => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
}
