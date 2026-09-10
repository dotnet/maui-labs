using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>Everything a caller can influence about a capture. Defaults are the privacy defaults.</summary>
internal sealed record EvidenceCaptureOptions
{
    /// <summary>Screenshots are opt-in — never captured unless explicitly requested.</summary>
    public bool IncludeScreenshot { get; init; }

    /// <summary>Collect and project, but skip the screenshot read (used by preview).</summary>
    public bool PreviewOnly { get; init; }

    public string? WorkflowMarkdown { get; init; }
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
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Agent status unavailable: {Describe(ex)}"); }
        if (status is null)
            warnings.Add("Agent status was unavailable; this capture is incomplete.");

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
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Agent capabilities unavailable: {Describe(ex)}"); }

        EvidenceDeviceInfo? device = null;
        try { device = ProjectDevice(await source.GetPlatformInfoAsync("device-info", ct)); }
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Device info unavailable: {Describe(ex)}"); }

        EvidenceDisplayInfo? display = null;
        try { display = ProjectDisplay(await source.GetPlatformInfoAsync("device-display", ct)); }
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Display info unavailable: {Describe(ex)}"); }

        EvidenceThemeInfo? theme = null;
        try { theme = ProjectTheme(await source.GetPlatformInfoAsync("app-info", ct)); }
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Theme unavailable: {Describe(ex)}"); }

        EvidenceConnectivityInfo? connectivity = null;
        try { connectivity = ProjectConnectivity(await source.GetPlatformInfoAsync("connectivity", ct)); }
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex)) { warnings.Add($"Connectivity unavailable: {Describe(ex)}"); }

        var viewport = status?.Device is { } metrics ? new EvidenceViewportInfo
        {
            Width = PositiveNumber(metrics.WindowWidth),
            Height = PositiveNumber(metrics.WindowHeight),
            Density = PositiveNumber(metrics.DisplayDensity),
            WindowCount = metrics.WindowCount is > 0 ? metrics.WindowCount : null,
        } : null;

        var environment = new EvidenceEnvironment
        {
            CapturedUtc = capturedUtc,
            App = app,
            Platform = platform,
            Device = device,
            Display = display,
            Viewport = viewport,
            Theme = theme,
            Connectivity = connectivity,
            Capabilities = capabilities,
            Route = EvidenceRedaction.ScrubRoute(status?.Route),
        };
        environment.Unavailable = EnvironmentGaps(environment);
        AddEntry(entries, entryInfos, EvidenceFormat.EnvironmentEntry,
            "App build, OS/device, display, app viewport, theme, connectivity, and explicit environment gaps", null,
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
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.TreeEntry, $"Visual tree unavailable: {Describe(ex)}"));
        }

        exclusions.Add(new EvidenceExclusion(EvidenceFormat.ProblemsEntry,
            "The current agent API does not expose runtime Problems. Layout findings and logs are captured separately."));

        // ── layout ───────────────────────────────────────────────────────────────────────────
        // Additive and capability-gated: an agent that predates layout diagnostics returns null,
        // which becomes an explicit exclusion instead of a failed capture.
        try
        {
            var report = await source.GetLayoutDiagnosticsAsync(ct);
            if (report is null)
            {
                exclusions.Add(new EvidenceExclusion(EvidenceFormat.LayoutEntry,
                    "The connected agent does not support layout diagnostics."));
            }
            else
            {
                var projected = ProjectLayout(report, options.ProjectRoot);
                counts.LayoutFindings = projected.FindingCount;
                counts.LayoutViolations = projected.Violations;
                if (projected.FindingsTruncated)
                    warnings.Add($"Layout findings truncated at {EvidenceFormat.MaxLayoutFindings}.");
                AddEntry(entries, entryInfos, EvidenceFormat.LayoutEntry,
                    "Layout diagnostics: rule outcomes, coverage, and element identity/geometry (no text or property values)",
                    projected.FindingCount, EvidenceJson.SerializeToUtf8(projected));
            }
        }
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.LayoutEntry, $"Layout diagnostics unavailable: {Describe(ex)}"));
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
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
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
        catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.NetworkEntry, $"Network capture unavailable: {Describe(ex)}"));
        }

        // ── workflow (caller-supplied markdown) ──────────────────────────────────────────────
        // Scrubbed like every other free-form payload: a recorded or hand-written repro can still
        // carry a token or a machine path even though a human chose to attach it.
        var workflowTooLarge = options.WorkflowMarkdown is not null &&
            Encoding.UTF8.GetByteCount(options.WorkflowMarkdown) > EvidenceFormat.MaxWorkflowBytes;
        var workflow = workflowTooLarge ? null : EvidenceRedaction.Scrub(
            SanitizeWorkflow(options.WorkflowMarkdown), (int)EvidenceFormat.MaxWorkflowBytes);
        if (workflowTooLarge)
        {
            exclusions.Add(new EvidenceExclusion(EvidenceFormat.WorkflowEntry,
                $"Workflow exceeds the {EvidenceFormat.MaxWorkflowBytes / 1024} KB limit."));
        }
        else if (!string.IsNullOrWhiteSpace(workflow))
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
                string.IsNullOrWhiteSpace(options.WorkflowMarkdown)
                    ? "No reproduction steps were attached."
                    : "The structured workflow could not be safely parsed and was omitted."));
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
            catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
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
        if (options.PreviewOnly && options.IncludeScreenshot)
        {
            included.Add(new EvidenceEntryInfo
            {
                Name = EvidenceFormat.ScreenshotEntry,
                Description = "App screenshot requested for capture; pixels and size are not collected during preview",
            });
        }

        var plan = new EvidencePlan
        {
            Source = manifest.Source,
            GeneratedUtc = capturedUtc,
            App = app,
            Platform = platform,
            Environment = environment,
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
        if (parsed.Flow.Schema != MauiFlow.CurrentSchema ||
            parsed.Flow.Steps.Any(step => step is null || step.Asserts?.Any(assertion => assertion is null) == true))
            return null;

        foreach (var step in parsed.Flow.Steps)
        {
            RedactSelectorText(step.Target);
            var originalArgs = step.Args;
            RedactSelectorText(originalArgs?.Selector);
            step.Args = SanitizeStepArgs(step.Action, originalArgs);

            if (step.Action == FlowActions.Navigate)
            {
                step.Value = EvidenceRedaction.ScrubRoute(step.Value);
            }
            else if (step.Action == FlowActions.SetTheme)
            {
                step.Value = SanitizeTheme(step.Value);
            }
            else
            {
                step.Value = step.Value is null ? null : "<redacted>";
            }
            step.Page = EvidenceRedaction.ScrubRoute(step.Page);
            step.Screenshot = EvidenceRedaction.NormalizeSourcePath(step.Screenshot, projectRoot: null);

            foreach (var assertion in step.Asserts ?? [])
            {
                RedactSelectorText(assertion.Selector);
                if (assertion.Kind == "routeIs")
                    assertion.Expected = EvidenceRedaction.ScrubRoute(assertion.Expected);
                else
                    assertion.Expected = assertion.Expected is null ? null : "<redacted>";
            }
        }

        return FlowMarkdown.Serialize(parsed.Flow);
    }

    private static FlowStepArgs? SanitizeStepArgs(string action, FlowStepArgs? args)
    {
        if (args is null)
            return null;

        var safe = new FlowStepArgs
        {
            Selector = args.Selector,
        };
        switch (action)
        {
            case FlowActions.Fill:
                safe.Text = args.Text is null ? null : "<redacted>";
                break;
            case FlowActions.SetProperty:
                safe.Name = EvidenceRedaction.SafeIdentifier(args.Name);
                safe.Value = args.Value is null ? null : "<redacted>";
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
        if (element.ValueKind != JsonValueKind.Object || element.TryGetProperty("error", out _))
            throw new InvalidDataException("The agent did not return capability metadata.");

        var container = element.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            ? caps
            : element;

        foreach (var property in container.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.Object) ||
                (property.Value.ValueKind == JsonValueKind.Object &&
                 property.Value.TryGetProperty("supported", out var supported) && supported.ValueKind == JsonValueKind.False))
                continue;
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
            Width = PositiveNumber(SafeNumber(element, "width")),
            Height = PositiveNumber(SafeNumber(element, "height")),
            Density = PositiveNumber(SafeNumber(element, "density")),
            Orientation = SafeString(element, "orientation"),
            Rotation = SafeString(element, "rotation"),
            RefreshRate = PositiveNumber(SafeNumber(element, "refreshRate")),
        };
        return display.Width is null && display.Height is null && display.Density is null &&
               display.Orientation is null && display.Rotation is null && display.RefreshRate is null
            ? null
            : display;
    }

    internal static EvidenceThemeInfo? ProjectTheme(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var theme = new EvidenceThemeInfo
        {
            Effective = SanitizeTheme(ReadString(element, "effectiveTheme")) ??
                SanitizeTheme(ReadString(element, "theme")),
            Requested = SanitizeTheme(ReadString(element, "requestedTheme")),
            AppOverride = SanitizeTheme(ReadString(element, "userAppTheme")),
        };
        return theme.Effective is null && theme.Requested is null && theme.AppOverride is null ? null : theme;
    }

    internal static EvidenceConnectivityInfo? ProjectConnectivity(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var access = ReadString(element, "networkAccess");
        var connectivity = new EvidenceConnectivityInfo
        {
            NetworkAccess = access is "Unknown" or "None" or "Local" or "ConstrainedInternet" or "Internet" ? access : null,
        };
        if (element.TryGetProperty("connectionProfiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
        {
            connectivity.ConnectionProfiles = profiles.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .OfType<string>()
                .Where(value => value is "Unknown" or "Bluetooth" or "Cellular" or "Ethernet" or "WiFi")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        return connectivity.NetworkAccess is null && connectivity.ConnectionProfiles.Count == 0 ? null : connectivity;
    }

    private static double? PositiveNumber(double? value)
        => value is > 0 && double.IsFinite(value.Value) ? value : null;

    private static List<EvidenceExclusion> EnvironmentGaps(EvidenceEnvironment environment)
    {
        var gaps = new List<EvidenceExclusion>
        {
            new("runtime", "The target runtime version is not exposed by this agent API. Framework version is not a runtime version."),
            new("locale", "The target app locale is not exposed by this agent API."),
            new("fontScale", "The target font scale and accessibility text settings are not exposed by this agent API."),
            new("permissions", "Permission states are not collected. Capture never requests or changes permissions."),
            new("networkConditions", "Connectivity is an observation, not a latency, bandwidth, packet-loss, proxy, or offline-test contract."),
        };
        if (MissingFact(environment.App?.Build))
            gaps.Add(new("appBuild", "The agent did not report an app build."));
        if (MissingFact(environment.Device?.OsVersion))
            gaps.Add(new("osVersion", "The agent did not report the target OS version."));
        if (MissingFact(environment.Device?.Model))
            gaps.Add(new("deviceModel", "The agent did not report a device model."));
        if (environment.Device?.DeviceType is null && environment.Platform?.DeviceType is null)
            gaps.Add(new("deviceType", "The agent did not distinguish a physical device from a virtual device."));
        if (environment.Display?.Width is null || environment.Display.Height is null)
            gaps.Add(new("display", "The agent did not report valid display dimensions."));
        if (environment.Display?.Orientation is null)
            gaps.Add(new("orientation", "The agent did not report display orientation."));
        if (environment.Display?.Rotation is null)
            gaps.Add(new("rotation", "The agent did not report display rotation."));
        if (environment.Viewport?.Width is null || environment.Viewport.Height is null)
            gaps.Add(new("viewport", "The agent did not report valid app-window dimensions. Display dimensions are not a viewport."));
        if (environment.Viewport?.Density is null)
            gaps.Add(new("density", "The agent did not report app-window density. Display density is not substituted."));
        if (environment.Viewport?.WindowCount is > 1)
            gaps.Add(new("windowSelection", "Multiple app windows were reported. This capture observes the agent's default window."));
        if (environment.Theme?.Effective is not ("light" or "dark"))
            gaps.Add(new("theme", "The agent did not report an effective light or dark app theme."));
        if (environment.Connectivity?.NetworkAccess is null or "Unknown")
            gaps.Add(new("connectivity", "The agent did not report a known network-access state."));
        return gaps;
    }

    private static bool MissingFact(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Projects a layout report into the bundle's safe shape.
    ///
    /// The agent's report already carries no text or property values, but this projection is still
    /// default-deny: only the fields listed here are copied, identifiers are re-checked, source
    /// paths are made project-relative (or reduced to a file name), and free-form strings are
    /// scrubbed and truncated exactly like every other bundle payload.
    /// </summary>
    internal static EvidenceLayoutDocument ProjectLayout(LayoutInspectionResult? report, string? projectRoot)
    {
        var document = new EvidenceLayoutDocument();
        if (report is null) return document;

        document.SchemaVersion = EvidenceRedaction.SafeIdentifier(report.SchemaVersion, 32) ?? "";
        document.RuleSetVersion = EvidenceRedaction.SafeIdentifier(report.RuleSetVersion, 32) ?? "";
        document.CapturedUtc = EvidenceRedaction.SafeIdentifier(report.Snapshot?.CapturedAt, 40);
        document.Platform = EvidenceRedaction.SafeIdentifier(report.Snapshot?.Platform, 64);
        document.SnapshotId = EvidenceRedaction.SafeIdentifier(report.Snapshot?.Id, 160);
        document.TreeRevision = EvidenceRedaction.SafeIdentifier(report.Snapshot?.TreeRevision, 160);
        document.DiagnosticsRevision = EvidenceRedaction.SafeIdentifier(report.Snapshot?.DiagnosticsRevision, 160);
        document.Stable = report.Snapshot?.Stable ?? false;
        document.ElementsExamined = report.Snapshot?.NodeCount ?? 0;
        document.Violations = report.Summary?.Violations ?? 0;
        document.Observations = report.Summary?.Observations ?? 0;
        document.Incomplete = report.Summary?.Incomplete ?? 0;
        document.Passes = report.Summary?.Passes ?? 0;
        document.NotApplicable = report.Summary?.NotApplicable ?? 0;
        document.Suppressed = report.Summary?.Suppressed ?? 0;
        document.Coverage = EvidenceRedaction.SafeIdentifier(report.Coverage?.Overall, 32) ?? "unavailable";

        foreach (var rule in (report.Coverage?.Rules ?? []).Take(64))
        {
            document.Rules.Add(new EvidenceLayoutRule
            {
                RuleId = EvidenceRedaction.SafeIdentifier(rule.RuleId, 64) ?? "",
                Support = EvidenceRedaction.SafeIdentifier(rule.Support, 32) ?? "unavailable",
                Confidence = EvidenceRedaction.SafeIdentifier(rule.Confidence, 32) ?? "medium",
            });
        }

        var findings = report.Findings ?? [];
        document.FindingsTruncated = findings.Count > EvidenceFormat.MaxLayoutFindings;
        foreach (var finding in findings.Take(EvidenceFormat.MaxLayoutFindings))
        {
            document.Findings.Add(new EvidenceLayoutFinding
            {
                Id = EvidenceRedaction.SafeIdentifier(finding.Id, 160) ?? "",
                RuleId = EvidenceRedaction.SafeIdentifier(finding.RuleId, 64) ?? "",
                Outcome = EvidenceRedaction.SafeIdentifier(finding.Outcome, 32) ?? "observation",
                Confidence = EvidenceRedaction.SafeIdentifier(finding.Confidence, 32) ?? "medium",
                Severity = EvidenceRedaction.SafeIdentifier(finding.Severity, 32) ?? "info",
                Actionability = EvidenceRedaction.SafeIdentifier(finding.Actionability, 32) ?? "review",
                SuppressionKey = EvidenceRedaction.SafeIdentifier(finding.SuppressionKey, 160),
                Suppressed = finding.Suppressed,
                SuppressionReason = EvidenceRedaction.Scrub(
                    finding.SuppressionReason,
                    EvidenceFormat.MaxLayoutTextChars),
                Message = EvidenceRedaction.Scrub(finding.Message, EvidenceFormat.MaxLayoutTextChars) ?? "",
                ElementId = EvidenceRedaction.SafeIdentifier(finding.Element?.Id),
                ElementType = EvidenceRedaction.SafeIdentifier(finding.Element?.Type),
                AutomationId = EvidenceRedaction.SafeIdentifier(finding.Element?.AutomationId),
                SourceFile = EvidenceRedaction.NormalizeSourcePath(finding.Element?.SourceFile, projectRoot),
                SourceLine = finding.Element?.SourceLine,
                SourceColumn = finding.Element?.SourceColumn,
                Bounds = ToEvidenceBounds(finding.Evidence?.FullRegion?.Bounds),
                RelatedElementIds = [.. (finding.RelatedElements ?? [])
                    .Take(64)
                    .Select(related => EvidenceRedaction.SafeIdentifier(related.Element?.Id))
                    .Where(id => id is not null)
                    .Select(id => id!)],
                FixCategories = [.. (finding.FixCategories ?? [])
                    .Take(64)
                    .Select(category => EvidenceRedaction.SafeIdentifier(category, 64))
                    .Where(category => category is not null)
                    .Select(category => category!)],
                Limitations = [.. (finding.Evidence?.Limitations ?? [])
                    .Take(64)
                    .Select(limitation => EvidenceRedaction.Scrub(limitation, EvidenceFormat.MaxLayoutTextChars))
                    .Where(limitation => !string.IsNullOrEmpty(limitation))
                    .Select(limitation => limitation!)],
            });
        }

        document.FindingCount = document.Findings.Count;
        document.Limitations = [.. (report.Coverage?.Limitations ?? [])
            .Take(256)
            .Select(limitation => EvidenceRedaction.Scrub(limitation, EvidenceFormat.MaxLayoutTextChars))
            .Where(limitation => !string.IsNullOrEmpty(limitation))
            .Select(limitation => limitation!)];
        document.NeverCaptured = ["Element Text/Value content", "Native and framework property dictionaries"];
        return document;
    }

    private static EvidenceBounds? ToEvidenceBounds(LayoutRectInfo? rect)
        => rect is null ? null : new EvidenceBounds
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
        };

    /// <summary>Parses the agent's compact log array (<c>{t,l,c,m,e,s}</c>) into bounded, scrubbed entries.</summary>
    internal static EvidenceLogDocument ProjectLogs(string? rawJson, int limit)
    {
        var document = new EvidenceLogDocument { Limit = limit };
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidDataException("The agent did not return a log payload.");

        var parsed = JsonDocument.Parse(rawJson);
        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The agent did not return a log array.");

            foreach (var item in parsed.RootElement.EnumerateArray())
            {
                if (document.Entries.Count >= limit)
                {
                    document.Truncated = true;
                    break;
                }
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The agent returned an invalid log entry.");

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
