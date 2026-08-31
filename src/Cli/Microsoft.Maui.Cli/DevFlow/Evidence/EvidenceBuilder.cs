using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>Everything a caller can influence about a capture. Defaults are the privacy defaults.</summary>
internal sealed record EvidenceCaptureOptions
{
    /// <summary>Screenshots are opt-in — never captured unless explicitly requested.</summary>
    public bool IncludeScreenshot { get; init; }

    /// <summary>Collect and project, but skip the screenshot read (used by preview).</summary>
    public bool PreviewOnly { get; init; }

    public string? WorkflowMarkdown { get; init; }
    public EvidenceFlowRunLink? FlowRun { get; init; }
    public string? CheckpointRoute { get; init; }
    public DateTimeOffset? CheckpointSavedUtc { get; init; }
    public string? CheckpointLastRestoreKind { get; init; }
    public string? SelectedElementId { get; init; }
    public int LogLimit { get; init; } = EvidenceFormat.DefaultLogLimit;
    public int NetworkLimit { get; init; } = EvidenceFormat.DefaultNetworkLimit;

    /// <summary>Originating surface: <c>cli</c>, <c>mcp</c>, or <c>inspector</c>.</summary>
    public string Source { get; init; } = "cli";

    /// <summary>Used to turn absolute source paths into project-relative ones.</summary>
    public string? ProjectRoot { get; init; }

    /// <summary>Destination reported in the plan (not written by the builder).</summary>
    public string? OutputPath { get; init; }

    public string ToolVersion { get; init; } = "";

    public DateTime? UtcNow { get; init; }
}

/// <summary>An in-memory bundle: the manifest, the preview plan, and the serialized entries.</summary>
internal sealed class EvidenceBundle
{
    public required EvidenceManifest Manifest { get; init; }
    public required EvidencePlan Plan { get; init; }
    /// <summary>Entries excluding <c>manifest.json</c>, in stable write order.</summary>
    public required IReadOnlyList<EvidenceBundleEntry> Entries { get; init; }
    public required byte[] ManifestBytes { get; init; }
}

internal sealed record EvidenceBundleEntry(string Name, byte[] Content);

/// <summary>
/// Collects evidence from a running app and projects it into the bundle's safe shapes.
///
/// Redaction happens HERE, at ingestion: nothing unredacted is ever handed to a serializer,
/// a renderer, or a browser. Each section is collected defensively — a section the agent cannot
/// serve becomes an explicit exclusion plus a warning rather than a failed capture.
/// </summary>
internal static class EvidenceBuilder
{
    public static async Task<EvidenceBundle> BuildAsync(
        IEvidenceDataSource source,
        EvidenceCaptureOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var utcNow = options.UtcNow ?? DateTime.UtcNow;
        var capturedUtc = utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var logLimit = Math.Clamp(options.LogLimit, 1, EvidenceFormat.MaxLogLimit);
        var networkLimit = Math.Clamp(options.NetworkLimit, 1, EvidenceFormat.MaxNetworkLimit);

        var warnings = new List<string>();
        var exclusions = new List<EvidenceExclusion>();
        var entries = new List<EvidenceBundleEntry>();
        var entryInfos = new List<EvidenceEntryInfo>();
        var counts = new EvidenceCounts();

        // ── environment ──────────────────────────────────────────────────────────────────────
        AgentStatus? status = null;
        try { status = await source.GetStatusAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"Agent status unavailable: {Describe(ex)}"); }

        var app = status?.App is null ? null : new EvidenceAppInfo
        {
            Name = EvidenceRedaction.SafeIdentifier(status.App.Name),
            Version = EvidenceRedaction.SafeIdentifier(status.App.Version),
            Build = EvidenceRedaction.SafeIdentifier(status.App.Build),
            PackageId = EvidenceRedaction.SafeIdentifier(status.App.PackageId),
        };

        var platform = status is null ? null : new EvidencePlatformInfo
        {
            Name = EvidenceRedaction.SafeIdentifier(status.Device?.Platform),
            DeviceType = EvidenceRedaction.SafeIdentifier(status.Device?.DeviceType),
            Idiom = EvidenceRedaction.SafeIdentifier(status.Device?.Idiom),
            AgentVersion = EvidenceRedaction.SafeIdentifier(status.Agent?.Version),
            Framework = EvidenceRedaction.SafeIdentifier(status.Agent?.Framework),
            FrameworkVersion = EvidenceRedaction.SafeIdentifier(status.Agent?.FrameworkVersion),
        };

        var capabilities = new List<string>();
        try { capabilities = ProjectCapabilities(await source.GetCapabilitiesAsync(ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"Agent capabilities unavailable: {Describe(ex)}"); }

        EvidenceDeviceInfo? device = null;
        try { device = ProjectDevice(await source.GetPlatformInfoAsync("device-info", ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"Device info unavailable: {Describe(ex)}"); }

        EvidenceDisplayInfo? display = null;
        try { display = ProjectDisplay(await source.GetPlatformInfoAsync("device-display", ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"Display info unavailable: {Describe(ex)}"); }

        var environment = new EvidenceEnvironment
        {
            CapturedUtc = capturedUtc,
            App = app,
            Platform = platform,
            Device = device,
            Display = display,
            Capabilities = capabilities,
            Route = EvidenceRedaction.ScrubRoute(status?.Route),
            Checkpoint = options.CheckpointRoute is null ? null : new EvidenceCheckpointInfo
            {
                Saved = true,
                Route = EvidenceRedaction.ScrubRoute(options.CheckpointRoute),
                SavedUtc = options.CheckpointSavedUtc?.ToString("O"),
                LastRestoreKind = EvidenceRedaction.SafeIdentifier(options.CheckpointLastRestoreKind)
            }
        };
        AddEntry(entries, entryInfos, EvidenceFormat.EnvironmentEntry,
            "App, platform, device, display, and agent capability metadata", null,
            EvidenceJson.SerializeToUtf8(environment));

        // ── tree ─────────────────────────────────────────────────────────────────────────────
        try
        {
            var tree = await source.GetTreeAsync(ct);
            var projected = ProjectTree(tree, options.ProjectRoot);
            counts.TreeElements = projected.Count;
            if (projected.Truncated)
                warnings.Add($"Visual tree truncated at {EvidenceFormat.MaxTreeElements} elements.");
            AddEntry(entries, entryInfos, EvidenceFormat.TreeEntry,
                "Element structure: type, automation id, bounds, state, and source location (no text or property values)",
                projected.Count, EvidenceJson.SerializeToUtf8(projected));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.TreeEntry, $"Visual tree unavailable: {Describe(ex)}"));
        }

        // ── problems ─────────────────────────────────────────────────────────────────────────
        try
        {
            var batch = await source.GetProblemsAsync(EvidenceFormat.MaxProblems, ct);
            var projected = ProjectProblems(batch, options.ProjectRoot);
            counts.Problems = projected.Count;
            AddEntry(entries, entryInfos, EvidenceFormat.ProblemsEntry,
                "Binding and property diagnostics (metadata only, messages re-redacted)",
                projected.Count, EvidenceJson.SerializeToUtf8(projected));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.ProblemsEntry, $"Problems unavailable: {Describe(ex)}"));
        }

        // ── logs ─────────────────────────────────────────────────────────────────────────────
        try
        {
            var raw = await source.GetLogsAsync(logLimit, ct);
            var projected = ProjectLogs(raw, logLimit);
            counts.Logs = projected.Count;
            AddEntry(entries, entryInfos, EvidenceFormat.LogsEntry,
                $"Most recent {projected.Count} log entries (secrets and absolute paths scrubbed, messages truncated)",
                projected.Count, EvidenceJson.SerializeToUtf8(projected));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.LogsEntry, $"Logs unavailable: {Describe(ex)}"));
        }

        // ── network ──────────────────────────────────────────────────────────────────────────
        try
        {
            var requests = await source.GetNetworkAsync(networkLimit, ct);
            var projected = ProjectNetwork(requests, networkLimit);
            counts.NetworkRequests = projected.Count;
            AddEntry(entries, entryInfos, EvidenceFormat.NetworkEntry,
                "HTTP request summaries: method, host, path, status, timing, sizes (no headers, bodies, or query values)",
                projected.Count, EvidenceJson.SerializeToUtf8(projected));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.NetworkEntry, $"Network capture unavailable: {Describe(ex)}"));
        }

        // ── workflow (caller-supplied markdown) ──────────────────────────────────────────────
        // Scrubbed like every other free-form payload: a recorded or hand-written repro can still
        // carry a token or a machine path even though a human chose to attach it.
        var workflow = EvidenceRedaction.Scrub(
            SanitizeWorkflow(options.WorkflowMarkdown),
            (int)EvidenceFormat.MaxWorkflowBytes);
        if (!string.IsNullOrWhiteSpace(workflow))
        {
            var bytes = Encoding.UTF8.GetBytes(workflow!);
            if (bytes.LongLength > EvidenceFormat.MaxWorkflowBytes)
            {
                exclusions.Add(new EvidenceExclusion(EvidenceFormat.WorkflowEntry,
                    $"Workflow exceeds the {EvidenceFormat.MaxWorkflowBytes / 1024} KB limit."));
            }
            else
            {
                counts.WorkflowBytes = bytes.LongLength;
                AddEntry(entries, entryInfos, EvidenceFormat.WorkflowEntry,
                    "Reproduction steps supplied with the capture — they may quote text and values from the recorded steps",
                    null, bytes);
                warnings.Add("This bundle contains the reproduction steps you attached, which may quote text and values you typed.");
            }
        }
        else
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.WorkflowEntry,
                "No reproduction steps were attached."));
        }

        // ── screenshot (opt-in only) ─────────────────────────────────────────────────────────
        var screenshot = new EvidenceScreenshotStatus { Requested = options.IncludeScreenshot };
        if (!options.IncludeScreenshot)
        {
            screenshot.OmittedReason = "Screenshots are opt-in and were not requested.";
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.ScreenshotEntry, screenshot.OmittedReason));
        }
        else if (options.PreviewOnly)
        {
            // Preview never touches the camera path; it only states the intent.
            screenshot.Included = true;
            warnings.Add("The screenshot is captured when the bundle is created and may show on-screen data.");
        }
        else
        {
            try
            {
                var png = await source.GetScreenshotAsync(ct);
                if (png is null || png.Length == 0)
                {
                    screenshot.OmittedReason = "The agent returned no screenshot.";
                    exclusions.Add(new EvidenceExclusion(EvidenceFormat.ScreenshotEntry, screenshot.OmittedReason));
                    warnings.Add(screenshot.OmittedReason);
                }
                else if (png.LongLength > EvidenceFormat.MaxScreenshotBytes)
                {
                    screenshot.OmittedReason = "The screenshot exceeded the size limit.";
                    exclusions.Add(new EvidenceExclusion(EvidenceFormat.ScreenshotEntry, screenshot.OmittedReason));
                    warnings.Add(screenshot.OmittedReason);
                }
                else
                {
                    screenshot.Included = true;
                    counts.ScreenshotBytes = png.LongLength;
                    AddEntry(entries, entryInfos, EvidenceFormat.ScreenshotEntry,
                        "Screen capture — included at your explicit request; it may show on-screen data", null, png);
                    warnings.Add("This bundle contains a screenshot, which may show on-screen data.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                screenshot.OmittedReason = $"Screenshot unavailable: {Describe(ex)}";
                exclusions.Add(new EvidenceExclusion(EvidenceFormat.ScreenshotEntry, screenshot.OmittedReason));
                warnings.Add(screenshot.OmittedReason);
            }
        }

        var limits = new EvidenceLimits
        {
            Logs = logLimit,
            Network = networkLimit,
        };

        var manifest = new EvidenceManifest
        {
            CapturedUtc = capturedUtc,
            Source = NormalizeSource(options.Source),
            Tool = new EvidenceToolInfo { Version = options.ToolVersion },
            App = app,
            Platform = platform,
            Capabilities = capabilities,
            Entries = entryInfos,
            Excluded = exclusions,
            NeverIncluded = [.. EvidenceFormat.NeverIncluded],
            Counts = counts,
            Limits = limits,
            Screenshot = screenshot,
            Checkpoint = environment.Checkpoint,
            FlowRun = ProjectFlowRunLink(options.FlowRun, options.ProjectRoot),
            SelectedElementId = EvidenceRedaction.SafeIdentifier(options.SelectedElementId),
            Warnings = warnings,
        };

        var manifestBytes = EvidenceJson.SerializeToUtf8(manifest);

        var included = new List<EvidenceEntryInfo>
        {
            new()
            {
                Name = EvidenceFormat.ManifestEntry,
                Description = "Bundle description: schema, redaction ruleset, contents, and exclusions",
                Bytes = manifestBytes.LongLength,
            },
        };
        included.AddRange(entryInfos);

        var plan = new EvidencePlan
        {
            Source = manifest.Source,
            GeneratedUtc = capturedUtc,
            App = app,
            Platform = platform,
            Included = included,
            Excluded = exclusions,
            NeverIncluded = [.. EvidenceFormat.NeverIncluded],
            Screenshot = screenshot,
            Counts = counts,
            Limits = limits,
            Warnings = warnings,
            SuggestedFileName = EvidencePaths.BuildDefaultFileName(app?.Name, utcNow),
            OutputPath = options.OutputPath,
            EstimatedBytes = manifestBytes.LongLength + entries.Sum(e => e.Content.LongLength),
            SelectedElementId = manifest.SelectedElementId,
        };

        ct.ThrowIfCancellationRequested();
        return new EvidenceBundle
        {
            Manifest = manifest,
            Plan = plan,
            Entries = entries,
            ManifestBytes = manifestBytes,
        };
    }

    private static string NormalizeSource(string? source) => source?.ToLowerInvariant() switch
    {
        "mcp" => "mcp",
        "inspector" => "inspector",
        _ => "cli",
    };

    private static EvidenceFlowRunLink? ProjectFlowRunLink(EvidenceFlowRunLink? link, string? projectRoot)
    {
        if (link is null)
            return null;

        return new EvidenceFlowRunLink
        {
            RunId = EvidenceRedaction.SafeIdentifier(link.RunId),
            FailedStepId = EvidenceRedaction.SafeIdentifier(link.FailedStepId),
            FailureCode = EvidenceRedaction.SafeIdentifier(link.FailureCode),
            ReportDigest = EvidenceRedaction.SafeIdentifier(link.ReportDigest),
            ReportPath = string.IsNullOrWhiteSpace(link.ReportPath)
                ? null
                : EvidenceRedaction.NormalizeSourcePath(link.ReportPath, projectRoot),
            ReportReference = EvidenceRedaction.SafeIdentifier(link.ReportReference),
            CaptureCompleteness = EvidenceRedaction.SafeIdentifier(link.CaptureCompleteness),
        };
    }

    // Exception text can carry hosts, ports, and local paths — scrub before it reaches a manifest.
    private static string Describe(Exception ex)
        => EvidenceRedaction.Scrub(ex.Message, EvidenceFormat.MaxErrorChars) ?? ex.GetType().Name;

    private static string? SanitizeWorkflow(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown;

        var parsed = FlowMarkdown.Parse(markdown);
        if (!parsed.Ok || parsed.Flow is null)
        {
            if (markdown.Contains("```json maui-test", StringComparison.Ordinal))
                return null;
            return markdown;
        }

        foreach (var step in parsed.Flow.Steps)
        {
            RedactSelectorText(step.Target);
            var originalArgs = step.Args;
            RedactSelectorText(originalArgs?.Selector);
            step.Args = SanitizeStepArgs(step.Action, originalArgs);

            if (step.Action == FlowActions.Navigate)
                step.Value = EvidenceRedaction.ScrubRoute(step.Value);
            else if (step.Action == FlowActions.SetTheme)
                step.Value = SanitizeTheme(step.Value);
            else
                step.Value = step.Value is null ? null : "<redacted>";

            step.Page = EvidenceRedaction.ScrubRoute(step.Page);
            step.Screenshot = EvidenceRedaction.NormalizeSourcePath(step.Screenshot, projectRoot: null);

            foreach (var assertion in step.Asserts ?? [])
            {
                RedactSelectorText(assertion.Selector);
                assertion.Expected = assertion.Kind == "routeIs"
                    ? EvidenceRedaction.ScrubRoute(assertion.Expected)
                    : assertion.Expected is null ? null : "<redacted>";
            }
        }

        return FlowMarkdown.Serialize(parsed.Flow);
    }

    private static FlowStepArgs? SanitizeStepArgs(string action, FlowStepArgs? args)
    {
        if (args is null)
            return null;

        var safe = new FlowStepArgs { Selector = args.Selector };
        switch (action)
        {
            case FlowActions.Fill:
                safe.Text = args.Text is null ? null : "<redacted>";
                safe.SecretEnvironmentVariable = EvidenceRedaction.SafeIdentifier(args.SecretEnvironmentVariable, 96);
                break;
            case FlowActions.SetProperty:
                safe.Name = EvidenceRedaction.SafeIdentifier(args.Name);
                safe.Value = args.Value is null ? null : "<redacted>";
                safe.SecretEnvironmentVariable = EvidenceRedaction.SafeIdentifier(args.SecretEnvironmentVariable, 96);
                break;
            case FlowActions.Navigate:
                safe.Route = EvidenceRedaction.ScrubRoute(args.Route);
                break;
            case FlowActions.SetTheme:
                safe.Theme = SanitizeTheme(args.Theme);
                break;
            case FlowActions.Scroll:
                safe.Element = EvidenceRedaction.SafeIdentifier(args.Element);
                safe.Dx = args.Dx;
                safe.Dy = args.Dy;
                safe.ItemIndex = args.ItemIndex;
                safe.Position = EvidenceRedaction.SafeIdentifier(args.Position, 32);
                safe.Animated = args.Animated;
                break;
        }

        return safe;
    }

    private static string? SanitizeTheme(string? value)
        => value?.Trim().ToLowerInvariant() is "light" or "dark" or "system"
            ? value.Trim().ToLowerInvariant()
            : null;

    private static void RedactSelectorText(FlowSelector? selector)
    {
        if (selector?.Text is not null)
            selector.Text = "<redacted>";
    }

    private static void AddEntry(
        List<EvidenceBundleEntry> entries,
        List<EvidenceEntryInfo> infos,
        string name,
        string description,
        int? count,
        byte[] content)
    {
        entries.Add(new EvidenceBundleEntry(name, content));
        infos.Add(new EvidenceEntryInfo
        {
            Name = name,
            Description = description,
            Count = count,
            Bytes = content.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        });
    }

    // ── projections ──────────────────────────────────────────────────────────────────────────

    internal static List<string> ProjectCapabilities(JsonElement element)
    {
        var result = new List<string>();
        if (element.ValueKind != JsonValueKind.Object) return result;

        var container = element.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            ? caps
            : element;

        foreach (var property in container.EnumerateObject())
        {
            var name = EvidenceRedaction.SafeIdentifier(property.Name, 64);
            if (name is not null) result.Add(name);
            if (result.Count >= 64) break;
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    internal static EvidenceDeviceInfo? ProjectDevice(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var device = new EvidenceDeviceInfo
        {
            Manufacturer = SafeString(element, "manufacturer"),
            Model = SafeString(element, "model"),
            Platform = SafeString(element, "platform"),
            OsVersion = SafeString(element, "osVersion") ?? SafeString(element, "version"),
            Idiom = SafeString(element, "idiom"),
            DeviceType = SafeString(element, "deviceType"),
            Architecture = SafeString(element, "architecture"),
        };
        return device.Manufacturer is null && device.Model is null && device.Platform is null &&
               device.OsVersion is null && device.Idiom is null && device.DeviceType is null &&
               device.Architecture is null
            ? null
            : device;
    }

    internal static EvidenceDisplayInfo? ProjectDisplay(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var display = new EvidenceDisplayInfo
        {
            Width = SafeNumber(element, "width"),
            Height = SafeNumber(element, "height"),
            Density = SafeNumber(element, "density"),
            Orientation = SafeString(element, "orientation"),
            Rotation = SafeString(element, "rotation"),
            RefreshRate = SafeNumber(element, "refreshRate"),
        };
        return display.Width is null && display.Height is null && display.Density is null &&
               display.Orientation is null && display.Rotation is null && display.RefreshRate is null
            ? null
            : display;
    }

    private static string? SafeString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? EvidenceRedaction.SafeIdentifier(value.GetString())
            : null;

    private static double? SafeNumber(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out var number)
            ? number
            : null;

    /// <summary>
    /// Projects the agent's visual tree into structure-only nodes. Text, Value, native and
    /// framework property dictionaries, and absolute source paths never leave this method.
    /// </summary>
    internal static EvidenceTreeDocument ProjectTree(IEnumerable<ElementInfo>? roots, string? projectRoot)
    {
        var document = new EvidenceTreeDocument { MaxDepth = EvidenceFormat.MaxTreeDepth };
        if (roots is null) return document;

        var budget = EvidenceFormat.MaxTreeElements;
        document.Roots = ProjectNodes(roots, projectRoot, 0, ref budget, document);
        document.Count = EvidenceFormat.MaxTreeElements - budget;
        return document;
    }

    private static List<EvidenceTreeNode> ProjectNodes(
        IEnumerable<ElementInfo> elements,
        string? projectRoot,
        int depth,
        ref int budget,
        EvidenceTreeDocument document)
    {
        var result = new List<EvidenceTreeNode>();
        foreach (var element in elements)
        {
            if (budget <= 0)
            {
                document.Truncated = true;
                break;
            }
            if (depth >= EvidenceFormat.MaxTreeDepth)
            {
                document.Truncated = true;
                break;
            }

            budget--;
            var node = new EvidenceTreeNode
            {
                Id = EvidenceRedaction.SafeIdentifier(element.Id) ?? "",
                Type = EvidenceRedaction.SafeIdentifier(element.Type) ?? "",
                Framework = EvidenceRedaction.SafeIdentifier(element.Framework),
                AutomationId = EvidenceRedaction.SafeIdentifier(element.AutomationId),
                Role = EvidenceRedaction.SafeIdentifier(element.Role),
                Visible = element.IsVisible,
                Enabled = element.IsEnabled,
                Focused = element.IsFocused,
                Selected = element.IsSelected ? true : null,
                Bounds = element.Bounds is null ? null : new EvidenceBounds
                {
                    X = element.Bounds.X,
                    Y = element.Bounds.Y,
                    Width = element.Bounds.Width,
                    Height = element.Bounds.Height,
                },
                SourceFile = EvidenceRedaction.NormalizeSourcePath(element.SourceFile, projectRoot),
                SourceLine = element.SourceLine,
                SourceColumn = element.SourceColumn,
                SourceHash = EvidenceRedaction.SafeIdentifier(element.SourceHash, 64),
                ChildCount = element.Children?.Count ?? 0,
            };

            if (element.Children is { Count: > 0 })
            {
                var children = ProjectNodes(element.Children, projectRoot, depth + 1, ref budget, document);
                node.Children = children.Count > 0 ? children : null;
            }

            result.Add(node);
        }
        return result;
    }

    internal static EvidenceProblemDocument ProjectProblems(DiagnosticProblemBatch? batch, string? projectRoot)
    {
        var document = new EvidenceProblemDocument();
        if (batch is null) return document;

        document.Enabled = batch.Enabled;
        document.Revision = batch.Revision;
        document.Evicted = batch.Evicted;

        foreach (var problem in batch.Problems.Take(EvidenceFormat.MaxProblems))
        {
            document.Problems.Add(new EvidenceProblem
            {
                Id = EvidenceRedaction.SafeIdentifier(problem.Id) ?? "",
                Kind = EvidenceRedaction.SafeIdentifier(problem.Kind) ?? "",
                Severity = EvidenceRedaction.SafeIdentifier(problem.Severity) ?? "",
                Code = EvidenceRedaction.SafeIdentifier(problem.Code),
                Message = BuildEvidenceProblemMessage(problem),
                Count = problem.Count,
                FirstSeenUtc = FormatUtc(problem.FirstSeenUtc),
                LastSeenUtc = FormatUtc(problem.LastSeenUtc),
                ElementId = EvidenceRedaction.SafeIdentifier(problem.ElementId),
                ElementType = EvidenceRedaction.SafeIdentifier(problem.ElementType),
                Property = EvidenceRedaction.SafeIdentifier(problem.Property),
                BindingType = EvidenceRedaction.SafeIdentifier(problem.BindingType),
                BindingPath = EvidenceRedaction.SafeIdentifier(problem.BindingPath, 256),
                BindingMode = EvidenceRedaction.SafeIdentifier(problem.BindingMode),
                SourceType = EvidenceRedaction.SafeIdentifier(problem.SourceType, 256),
                ConverterType = EvidenceRedaction.SafeIdentifier(problem.ConverterType, 256),
                SourceFile = EvidenceRedaction.NormalizeSourcePath(problem.SourceFile, projectRoot),
                SourceLine = problem.SourceLine,
                SourceColumn = problem.SourceColumn,
            });
        }

        document.Count = document.Problems.Count;
        return document;
    }

    private static string BuildEvidenceProblemMessage(DiagnosticProblem problem)
    {
        var kind = EvidenceRedaction.SafeIdentifier(problem.Kind, 64) ?? "runtime";
        var code = EvidenceRedaction.SafeIdentifier(problem.Code, 64);
        if (string.Equals(kind, "binding", StringComparison.OrdinalIgnoreCase))
        {
            var path = EvidenceRedaction.SafeIdentifier(problem.BindingPath, 256) ?? "(unknown path)";
            var target = EvidenceRedaction.SafeIdentifier(problem.ElementType, 128) ?? "target";
            var property = EvidenceRedaction.SafeIdentifier(problem.Property, 128) ?? "property";
            var suffix = code is null ? "" : $" {code}";
            return EvidenceRedaction.Truncate(
                $"MAUI binding failure{suffix}: '{path}' could not update {target}.{property}.",
                EvidenceFormat.MaxProblemMessageChars);
        }

        return EvidenceRedaction.Truncate(
            code is null
                ? $"{kind} runtime diagnostic."
                : $"{kind} runtime diagnostic {code}.",
            EvidenceFormat.MaxProblemMessageChars);
    }

    private static string? FormatUtc(DateTime value)
        => value == default ? null : value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Parses the agent's compact log array (<c>{t,l,c,m,e,s}</c>) into bounded, scrubbed entries.</summary>
    internal static EvidenceLogDocument ProjectLogs(string? rawJson, int limit)
    {
        var document = new EvidenceLogDocument { Limit = limit };
        if (string.IsNullOrWhiteSpace(rawJson)) return document;

        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(rawJson!); }
        catch (JsonException) { return document; }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Array) return document;

            foreach (var item in parsed.RootElement.EnumerateArray())
            {
                if (document.Entries.Count >= limit)
                {
                    document.Truncated = true;
                    break;
                }
                if (item.ValueKind != JsonValueKind.Object) continue;

                document.Entries.Add(new EvidenceLogEntry
                {
                    Timestamp = SafeString(item, "t"),
                    Level = SafeString(item, "l"),
                    Category = EvidenceRedaction.SafeIdentifier(ReadString(item, "c"), 256),
                    Message = EvidenceRedaction.Scrub(ReadString(item, "m"), EvidenceFormat.MaxLogMessageChars) ?? "",
                    Exception = EvidenceRedaction.Scrub(ReadString(item, "e"), EvidenceFormat.MaxLogMessageChars),
                    Source = EvidenceRedaction.SafeIdentifier(ReadString(item, "s"), 32),
                });
            }
        }

        document.Count = document.Entries.Count;
        return document;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Projects captured HTTP traffic to summary metadata. Query VALUES, headers, and bodies are
    /// dropped entirely; only parameter names survive.
    /// </summary>
    internal static EvidenceNetworkDocument ProjectNetwork(IEnumerable<NetworkRequest>? requests, int limit)
    {
        var document = new EvidenceNetworkDocument { Limit = limit };
        if (requests is null) return document;

        var sequence = 0;
        foreach (var request in requests)
        {
            if (document.Requests.Count >= limit) break;
            sequence++;

            var (path, queryKeys) = SplitPath(request.Path, request.Url);
            document.Requests.Add(new EvidenceNetworkEntry
            {
                Sequence = sequence,
                Timestamp = request.Timestamp == default
                    ? null
                    : request.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Method = EvidenceRedaction.SafeIdentifier(request.Method, 16) ?? "",
                Host = EvidenceRedaction.SafeIdentifier(request.Host ?? TryHost(request.Url), 256),
                Path = path,
                QueryKeys = queryKeys.Count > 0 ? queryKeys : null,
                StatusCode = request.StatusCode,
                StatusText = EvidenceRedaction.SafeIdentifier(request.StatusText, 64),
                DurationMs = request.DurationMs,
                RequestBytes = request.RequestSize,
                ResponseBytes = request.ResponseSize,
                RequestContentType = EvidenceRedaction.SafeIdentifier(request.RequestContentType, 128),
                ResponseContentType = EvidenceRedaction.SafeIdentifier(request.ResponseContentType, 128),
                Error = EvidenceRedaction.Scrub(request.Error, EvidenceFormat.MaxErrorChars),
            });
        }

        document.Count = document.Requests.Count;
        return document;
    }

    private static string? TryHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>Returns the path without any query, plus the sorted set of query parameter NAMES.</summary>
    internal static (string? Path, List<string> QueryKeys) SplitPath(string? path, string? url)
    {
        var raw = path;
        if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(url))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                raw = uri.PathAndQuery;
            else
                raw = url;
        }
        if (string.IsNullOrWhiteSpace(raw)) return (null, []);

        var value = raw!;
        var fragment = value.IndexOf('#');
        if (fragment >= 0) value = value[..fragment];

        var keys = new List<string>();
        var question = value.IndexOf('?');
        if (question >= 0)
        {
            var query = value[(question + 1)..];
            value = value[..question];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                if (keys.Count >= EvidenceFormat.MaxQueryKeys) break;
                var equals = pair.IndexOf('=');
                var key = equals >= 0 ? pair[..equals] : pair;
                var safe = EvidenceRedaction.SafeIdentifier(key, 64);
                if (safe is not null && !keys.Contains(safe, StringComparer.Ordinal))
                    keys.Add(safe);
            }
            keys.Sort(StringComparer.Ordinal);
        }

        return (EvidenceRedaction.SafeIdentifier(value, 512), keys);
    }
}
