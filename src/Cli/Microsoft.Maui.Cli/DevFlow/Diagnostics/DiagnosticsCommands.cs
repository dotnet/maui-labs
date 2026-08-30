using System.CommandLine;
using System.Globalization;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow.Diagnostics;

/// <summary>
/// <c>maui devflow diagnostics</c>: on-demand, read-only diagnostics against the running app.
///
/// <list type="bullet">
///   <item><c>performance</c> — a bounded performance triage window over the existing profiler.</item>
/// </list>
///
/// It follows the DevFlow CLI conventions: shared <c>--json</c>/<c>--no-json</c>,
/// <see cref="IDevFlowOutputWriter"/> for output, structured errors on stderr.
/// </summary>
internal static class DiagnosticsCommands
{
    private const int MinDurationSeconds = 1;
    private const int MaxDurationSeconds = 300;
    private const int MinSampleIntervalMs = 50;
    private const int MaxSampleIntervalMs = 60_000;

    public static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var command = new Command("diagnostics",
            "Run on-demand, read-only layout and performance diagnostics against the connected app");

        command.Add(CreateLayoutCommand(jsonOption, noJsonOption, agentHostOption, agentPortOption, output, clientFactory, onError));
        command.Add(CreatePerformanceCommand(jsonOption, noJsonOption, agentHostOption, agentPortOption, output, clientFactory, onError));
        return command;
    }

    // ── layout ───────────────────────────────────────────────────────────────────────────────

    private static Command CreateLayoutCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var elementOption = new Option<string?>("--element")
        {
            Description = "Restrict the scan to this element id and its descendants"
        };
        var windowOption = new Option<int?>("--window")
        {
            Description = "0-based window index (default: every window)"
        };
        var maxElementsOption = new Option<int?>("--max-elements")
        {
            Description = "Element budget for the scan (default: 2000, maximum: 5000)"
        };
        var includeObservationsOption = new Option<bool>("--include-observations")
        {
            Description = "Include observations and incomplete results in human output (JSON always includes everything)",
            DefaultValueFactory = _ => true,
        };

        var command = new Command("layout",
            "Scan managed MAUI layout state once and report violations, observations, and coverage gaps")
        {
            elementOption,
            windowOption,
            maxElementsOption,
            includeObservationsOption,
        };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var window = ctx.GetValue(windowOption);
            if (window is < 0)
            {
                output.WriteError(
                    "--window must be zero or greater.",
                    isJson,
                    "InvalidArgument");
                onError();
                return;
            }
            var maxElements = ctx.GetValue(maxElementsOption);
            if (maxElements is < 1 or > 5000)
            {
                output.WriteError(
                    "--max-elements must be between 1 and 5000.",
                    isJson,
                    "InvalidArgument");
                onError();
                return;
            }

            try
            {
                using var client = await clientFactory(
                    ctx.GetValue(agentHostOption)!,
                    ctx.GetValue(agentPortOption),
                    !isJson);
                if (await client.GetStatusAsync() is null)
                {
                    output.WriteError(
                        $"The DevFlow agent at {client.BaseUrl} is not reachable.",
                        isJson,
                        "ConnectionError",
                        retryable: true,
                        suggestions: ["Start the app or select a reachable agent with --agent-port"]);
                    onError();
                    return;
                }
                var report = await LayoutDiagnosticsCoordinator.ScanAsync(
                    client,
                    elementId: ctx.GetValue(elementOption),
                    window: window,
                    maxElements: maxElements,
                    // The CLI is the user's own shell, so its working directory is the explicit,
                    // disclosed project root the suppression policy is read from.
                    policyStartPath: Environment.CurrentDirectory,
                    cancellationToken: ct);

                if (report is null)
                {
                    output.WriteError(
                        "Layout diagnostics are unavailable. The agent may be older than this CLI, or the requested element no longer exists.",
                        isJson,
                        "InvocationError",
                        suggestions: ["Run 'maui devflow agent status' to check the connected agent"]);
                    onError();
                    return;
                }

                if (isJson) output.WriteRawJson(CliJson.SerializeUntyped(report, indented: true));
                else Console.WriteLine(FormatLayout(report, ctx.GetValue(includeObservationsOption)));
            }
            catch (Exception ex)
            {
                output.WriteError(ex.Message, isJson, suggestions: ["Run 'maui devflow agent status' to confirm the selected app is connected"]);
                onError();
            }
        });

        return command;
    }

    internal static string FormatLayout(LayoutDiagnosticsReport report, bool includeObservations)
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"Layout diagnostics for {report.Platform} · schema v{report.SchemaVersion} · rules v{report.RuleSetVersion}");
        text.AppendLine(
            $"  Scope: {report.Scope.ElementsExamined} element(s)" +
            (report.Scope.RootElementId is { } root ? $" under '{root}'" : "") +
            (report.Scope.Truncated ? $" (truncated at {report.Scope.MaxElements})" : ""));
        text.AppendLine(
            $"  Summary: {report.Summary.Violations} violation(s) · {report.Summary.Observations} observation(s) · " +
            $"{report.Summary.Incomplete} incomplete");
        text.AppendLine($"  Coverage: {report.Coverage.Overall}");

        foreach (var rule in report.Coverage.Rules)
            text.AppendLine($"    - {rule.RuleId}: {rule.Support} ({rule.Evaluated} evaluated, {rule.Skipped} skipped)");

        var findings = includeObservations
            ? report.Findings
            : report.Findings.Where(finding => finding.Outcome == "violation").ToList();

        if (findings.Count == 0)
        {
            text.AppendLine("  No findings for the selected outcomes.");
        }
        else
        {
            text.AppendLine("  Findings:");
            foreach (var finding in findings)
            {
                var element = finding.Element is null
                    ? ""
                    : $" [{finding.Element.Type}#{finding.Element.Id}" +
                      (finding.Element.SourceFile is { } file
                          ? $" · {Path.GetFileName(file)}{(finding.Element.SourceLine is { } line ? $":{line}" : "")}"
                          : "") + "]";
                text.AppendLine($"    {finding.Outcome.ToUpperInvariant()} ({finding.Confidence}) {finding.RuleId}{element}");
                text.AppendLine($"      {finding.Message}");
                text.AppendLine($"      Why: {finding.Explanation}");
                foreach (var limitation in finding.Limitations)
                    text.AppendLine($"      ! {limitation}");
            }
        }

        if (report.SystemEvidence is { } system)
        {
            text.AppendLine(
                $"  System evidence: {system.Status} · device {system.DeviceId ?? "unknown"} · " +
                $"geometry stable: {system.GeometryStable}" +
                (system.CaptureSkewMs is { } skew ? $" · skew {Math.Round(skew)} ms" : ""));
            if (system.KeyboardVisible == true)
                text.AppendLine("    - Software keyboard visible");
            if (!string.IsNullOrWhiteSpace(system.ForegroundOwner))
                text.AppendLine($"    - Foreground owner: {system.ForegroundOwner}");
            text.AppendLine($"    - External system elements: {system.Elements.Count}");
            foreach (var limitation in system.Limitations)
                text.AppendLine($"    ! {limitation}");
        }

        text.AppendLine("  Limitations:");
        foreach (var limitation in report.Coverage.Limitations)
            text.AppendLine($"    ! {limitation}");
        text.Append("  Never captured: " + string.Join(", ", report.Coverage.NeverCaptured));
        return text.ToString();
    }

    // ── performance ──────────────────────────────────────────────────────────────────────────

    private static Command CreatePerformanceCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var durationOption = new Option<int>("--duration")
        {
            Description = $"Recording window in seconds ({MinDurationSeconds}-{MaxDurationSeconds})",
            DefaultValueFactory = _ => 5,
        };
        var sampleIntervalOption = new Option<int?>("--sample-interval")
        {
            Description = $"Profiler sampling interval in milliseconds ({MinSampleIntervalMs}-{MaxSampleIntervalMs}); defaults to the agent's configured interval"
        };
        var hotspotLimitOption = new Option<int>("--hotspots")
        {
            Description = "Number of top hotspots to include (1-50)",
            DefaultValueFactory = _ => 10,
        };
        var attachOption = new Option<bool>("--attach")
        {
            Description = "Summarize the profiler session that is already running instead of starting and stopping one",
            DefaultValueFactory = _ => false,
        };

        var command = new Command("performance",
            "Record a bounded performance triage window and summarize memory, GC, CPU, and hotspots")
        {
            durationOption,
            sampleIntervalOption,
            hotspotLimitOption,
            attachOption,
        };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var duration = ctx.GetValue(durationOption);
            if (duration < MinDurationSeconds || duration > MaxDurationSeconds)
            {
                output.WriteError(
                    $"--duration must be between {MinDurationSeconds} and {MaxDurationSeconds} seconds.",
                    isJson,
                    "InvalidArgument");
                onError();
                return;
            }
            var hotspots = ctx.GetValue(hotspotLimitOption);
            if (hotspots < 1 || hotspots > 50)
            {
                output.WriteError(
                    "--hotspots must be between 1 and 50.",
                    isJson,
                    "InvalidArgument");
                onError();
                return;
            }
            var sampleInterval = ctx.GetValue(sampleIntervalOption);
            if (sampleInterval is { } interval && (interval < MinSampleIntervalMs || interval > MaxSampleIntervalMs))
            {
                output.WriteError(
                    $"--sample-interval must be between {MinSampleIntervalMs} and {MaxSampleIntervalMs} milliseconds.",
                    isJson,
                    "InvalidArgument");
                onError();
                return;
            }

            try
            {
                using var client = await clientFactory(
                    ctx.GetValue(agentHostOption)!,
                    ctx.GetValue(agentPortOption),
                    !isJson);
                string? ownedSessionId = null;
                string? ownedStopToken = null;
                try
                {
                    PerformanceSummary summary;
                    if (ctx.GetValue(attachOption))
                    {
                        summary = await client.GetPerformanceSummaryAsync(hotspotLimit: hotspots);
                    }
                    else
                    {
                        var started = await client.StartPerformanceSessionAsync(sampleInterval);
                        if (!started.Capability.Available)
                        {
                            WriteSummary(output, started, isJson);
                            return;
                        }
                        if (!started.Session.Active || string.IsNullOrWhiteSpace(started.Session.SessionId))
                            throw new InvalidOperationException("The agent did not return an active profiler session.");
                        if (string.IsNullOrWhiteSpace(started.Session.StopToken))
                            throw new InvalidOperationException("The agent did not return a profiler creator stop token.");

                        ownedSessionId = started.Session.SessionId;
                        ownedStopToken = started.Session.StopToken;
                        await Task.Delay(TimeSpan.FromSeconds(duration), ct);
                        summary = await client.StopPerformanceSessionAsync(
                            ownedSessionId,
                            ownedStopToken,
                            hotspotLimit: hotspots);
                        if (summary.Session.Active)
                            throw new InvalidOperationException("The profiler session did not stop.");
                        ownedSessionId = null;
                        ownedStopToken = null;
                    }

                    WriteSummary(output, summary, isJson);
                }
                catch
                {
                    if (ownedSessionId is not null && ownedStopToken is not null)
                    {
                        var stopped = await client.StopProfilerAsync(ownedSessionId, ownedStopToken);
                        if (stopped is null || stopped.IsActive)
                        {
                            throw new InvalidOperationException(
                                $"The performance command failed and profiler session '{ownedSessionId}' could not be stopped.");
                        }
                    }
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                output.WriteError("The recording was cancelled.", isJson, "Cancelled");
                onError();
            }
            catch (Exception ex)
            {
                output.WriteError(ex.Message, isJson, suggestions: ["Run 'maui devflow agent status' to confirm the selected app is connected"]);
                onError();
            }
        });

        return command;
    }

    private static void WriteSummary(IDevFlowOutputWriter output, PerformanceSummary summary, bool isJson)
    {
        if (isJson) output.WriteRawJson(CliJson.SerializeUntyped(summary, indented: true));
        else Console.WriteLine(FormatPerformance(summary));
    }

    internal static string FormatPerformance(PerformanceSummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"Performance triage · {summary.Capability.Platform} · mode {summary.Capability.Mode}" +
            (summary.Capability.LowPerturbation ? " (read-only profile build)" : ""));
        text.AppendLine(
            $"  Session: {summary.Session.SampleCount} sample(s) over {FormatMs(summary.Session.SampledDurationMs)} " +
            $"at {summary.Session.SampleIntervalMs} ms" + (summary.Session.Active ? " · active" : ""));

        text.AppendLine(
            $"  Managed memory: {FormatBytes(summary.Memory.ManagedStartBytes)} → {FormatBytes(summary.Memory.ManagedEndBytes)} " +
            $"(peak {FormatBytes(summary.Memory.ManagedPeakBytes)}, delta {FormatDelta(summary.Memory.ManagedDeltaBytes)})");
        text.AppendLine(summary.Memory.ProcessSupported && summary.Memory.ProcessEndBytes.HasValue
            ? $"  Process memory ({summary.Memory.ProcessKind ?? "unknown"}): {FormatBytes(summary.Memory.ProcessStartBytes)} → " +
              $"{FormatBytes(summary.Memory.ProcessEndBytes)} (peak {FormatBytes(summary.Memory.ProcessPeakBytes)}, " +
              $"delta {FormatDelta(summary.Memory.ProcessDeltaBytes)})"
            : $"  Process memory: {summary.Memory.ProcessUnsupportedReason ?? "not observable on this platform"}");
        text.AppendLine(summary.Memory.NativeSupported && summary.Memory.NativeEndBytes.HasValue
            ? $"  Native heap ({summary.Memory.NativeKind ?? "unknown"}): {FormatBytes(summary.Memory.NativeStartBytes)} → " +
              $"{FormatBytes(summary.Memory.NativeEndBytes)} (peak {FormatBytes(summary.Memory.NativePeakBytes)}, " +
              $"delta {FormatDelta(summary.Memory.NativeDeltaBytes)})"
            : $"  Native heap: {summary.Memory.NativeUnsupportedReason ?? "not observable on this platform"}");

        text.AppendLine(summary.Gc.Supported
            ? $"  GC: gen0 +{summary.Gc.Gen0Delta ?? 0} · gen1 +{summary.Gc.Gen1Delta ?? 0} · gen2 +{summary.Gc.Gen2Delta ?? 0}"
            : "  GC: not observable");
        text.AppendLine(summary.Cpu.Supported && summary.Cpu.AveragePercent.HasValue
            ? $"  CPU: avg {summary.Cpu.AveragePercent:0.##}% · peak {summary.Cpu.PeakPercent:0.##}%"
            : "  CPU: not observable");
        text.AppendLine(summary.Threads.Supported && summary.Threads.PeakCount.HasValue
            ? $"  Threads: peak {summary.Threads.PeakCount}"
            : "  Threads: not observable");

        text.AppendLine(summary.Frames.Supported
            ? $"  Frames ({summary.Frames.Source}/{summary.Frames.Quality}): avg {summary.Frames.AverageFps:0.##} fps · " +
              $"min {summary.Frames.MinimumFps:0.##} fps · p95 {summary.Frames.FrameTimeMsP95:0.##} ms · " +
              $"worst {summary.Frames.WorstFrameTimeMs:0.##} ms · jank {summary.Frames.JankFrameCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} · " +
              $"stalls {summary.Frames.UiThreadStallCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}"
            : $"  Frames: unavailable — {summary.Frames.UnsupportedReason}");

        text.AppendLine(
            $"  Markers: {summary.Markers.Total} total (ui {summary.Markers.Ui} · network {summary.Markers.Network} · " +
            $"navigation {summary.Markers.Navigation}) · spans {summary.Markers.SpanCount}");

        if (summary.Hotspots.Count > 0)
        {
            text.AppendLine("  Top hotspots (p95 duration):");
            foreach (var hotspot in summary.Hotspots)
            {
                text.AppendLine(
                    $"    {hotspot.P95DurationMs,8:0.##} ms  {hotspot.Kind}/{hotspot.Name}" +
                    (hotspot.Screen is { } screen ? $" @ {screen}" : "") +
                    $"  (n={hotspot.Count}, errors={hotspot.ErrorCount}, max={hotspot.MaxDurationMs:0.##} ms)");
            }
        }

        foreach (var warning in summary.Warnings)
            text.AppendLine($"  ! {warning}");

        text.AppendLine("  Limitations:");
        foreach (var limitation in summary.Capability.Limitations)
            text.AppendLine($"    - {limitation}");
        text.Append("  Hand off to a native profiler (dotnet-trace, Instruments, Android Studio Profiler) for call-stack attribution.");
        return text.ToString();
    }

    private static string FormatMs(double value)
        => value <= 0 ? "0 ms" : $"{value / 1000d:0.##} s";

    private static string FormatBytes(long? bytes)
    {
        if (bytes is not { } value)
            return "n/a";
        var units = new[] { "B", "KB", "MB", "GB" };
        double size = Math.Abs(value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        var sign = value < 0 ? "-" : "";
        return $"{sign}{size.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static string FormatDelta(long? bytes)
        => bytes is not { } value ? "n/a" : (value >= 0 ? "+" : "") + FormatBytes(value);
}
