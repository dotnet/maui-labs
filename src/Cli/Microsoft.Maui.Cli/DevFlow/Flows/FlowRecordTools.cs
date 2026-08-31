using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// MCP controls for the broker-owned workflow recorder. Successful mutations from every client using
/// the active lease are observed by the agent and appended automatically. Distinct from
/// <c>maui_recording_*</c> (screen video).
/// </summary>
[McpServerToolType]
public sealed class FlowRecordTools
{
    private const int MaxFieldLen = 8 * 1024;
    private const int MaxAssertsJson = 64 * 1024;

    [McpServerTool(Name = "maui_flow_record_start"),
     Description("Begin or resume the shared workflow recording for the selected app. A valid mutation lease is required. " +
                "Successful mutations from the browser inspector, VS Code, Canvas, MCP, and CLI are appended automatically. " +
                "Returns a recordingId used by the status/stop/cancel compatibility parameters.")]
    public static async Task<string> RecordStart(
        McpAgentSession session,
        [Description("Short scenario name (also the default .md filename)")] string name,
        [Description("App name (optional; auto-detected from the agent when omitted)")] string? app = null,
        [Description("Platform (optional; auto-detected from the agent when omitted)")] string? platform = null,
        [Description("Human note describing the starting state (optional)")] string? preconditions = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (TooLong(name, out var e) || TooLong(app, out e) || TooLong(platform, out e) || TooLong(preconditions, out e))
            return Error(e!);
        if (session is null)
            return StartLocal(name, app, platform, preconditions);

        try
        {
            using var agent = await session.GetAgentClientAsync(agentPort);
            if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(platform))
            {
                var status = await agent.GetStatusAsync();
                app ??= status?.App?.Name;
                platform ??= status?.Device?.Platform;
            }

            var result = await agent.ControlMutationRecordingAsync("start", name, app, platform, preconditions);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Error($"Could not start shared recording: {ex.Message}");
        }
    }

    [McpServerTool(Name = "maui_flow_record_step"),
     Description("Compatibility tool for older callers. Steps are now captured automatically after successful mutations; " +
                "this reports the current shared recording count and does not append a duplicate step.")]
    public static async Task<string> RecordStep(
        McpAgentSession session,
        [Description("The recordingId from maui_flow_record_start")] string recordingId,
        [Description("Action: tap | fill | scroll | navigate | back | setTheme | setProperty")] string action,
        [Description("Target AutomationId (preferred, durable)")] string? automationId = null,
        [Description("Target exact visible Text (fallback selector)")] string? text = null,
        [Description("Target element Type for a Type+Index selector (fragile)")] string? type = null,
        [Description("Zero-based index for the Type+Index selector")] int? index = null,
        [Description("Target raw element Id (most fragile selector)")] string? id = null,
        [Description("Scalar value for the action (fill text / navigate route / setTheme theme / setProperty value)")] string? value = null,
        [Description("Property name (required for setProperty)")] string? name = null,
        [Description("Scroll horizontal delta")] double? dx = null,
        [Description("Scroll vertical delta")] double? dy = null,
        [Description("Scroll target item index")] int? itemIndex = null,
        [Description("Scroll position: start | center | end")] string? position = null,
        [Description("Current page/route label for the human layer (optional)")] string? page = null,
        [Description("True if this action changed the screen/route")] bool navigated = false,
        [Description("Optional JSON array of assertions: [{kind,selector,name,expected,verify}] (retained for compatibility)")] string? assertsJson = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (session is null)
            return StepLocal(recordingId, action, automationId, text, type, index, id, value, name, dx, dy, itemIndex, position, page, navigated, assertsJson);

        try
        {
            using var agent = await session.GetAgentClientAsync(agentPort);
            var status = await agent.ControlMutationRecordingAsync(
                "status", null, null, null, null, recordingId);
            if (!status.Ok)
                return Error(status.Error ?? "No shared recording is active.");
            if (!string.Equals(recordingId, status.RecordingId, StringComparison.Ordinal))
                return Error($"Unknown recordingId '{recordingId}'.");
            return Json(new
            {
                ok = true,
                recordingId = status.RecordingId,
                stepCount = status.Steps,
                automaticallyRecorded = true,
                message = "The mutation was observed automatically; no duplicate step was appended."
            });
        }
        catch (Exception ex)
        {
            return Error($"Could not read shared recording: {ex.Message}");
        }
    }

    /// <summary>
    /// Shared step-intake core used by BOTH the MCP <see cref="RecordStep"/> tool and the broker's
    /// HTTP <c>/api/flows/record/step</c> endpoint, so an inspector-recorded step is validated and
    /// normalized by the exact same rules (action allow-list, field caps, canonical selector,
    /// replay-validation probe) as an agent-recorded one.
    /// </summary>
    internal static (bool ok, int seq, int stepCount, bool fragile, string? error) AddStepCore(
        Testing.FlowRecorder recorder, string action,
        string? automationId, string? text, string? type, int? index, string? id,
        string? value, string? name, double? dx, double? dy, int? itemIndex, string? position,
        string? page, bool navigated, string? assertsJson,
        int? matchCount = null,
        string? quality = null,
        IReadOnlyList<string>? fragilityReasons = null,
        string? valueSource = null,
        bool sensitive = false,
        MauiSelectorObservation? selectorObservation = null)
    {
        if (string.IsNullOrWhiteSpace(action) || !Testing.FlowActions.All.Contains(action))
            return (false, -1, recorder.StepCount, false, $"Unknown action '{action}'. Use one of: {string.Join(", ", Testing.FlowActions.All)}.");

        var targetValueMayBeSecret = action == Testing.FlowActions.Fill ||
            (action == Testing.FlowActions.SetProperty &&
             (string.Equals(name, "Text", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase)));
        sensitive |= Testing.FlowSecretReference.LooksSensitive(name) ||
            (targetValueMayBeSecret &&
             Testing.FlowSecretReference.LooksSensitive(automationId, text, type, id));

        foreach (var s in new[] { automationId, text, type, id, value, name, position, page })
            if (TooLong(s, out var e)) return (false, -1, recorder.StepCount, false, e);

        // Normalize: whitespace-only identifiers/routes are treated as missing — they would pass
        // validation but cannot be resolved or driven on replay.
        automationId = Clean(automationId);
        type = Clean(type);
        id = Clean(id);
        name = Clean(name);
        position = Clean(position);
        text = string.IsNullOrWhiteSpace(text) ? null : text;

        var scopedItem = GetScopedStableItem(selectorObservation, automationId);
        var target = BuildSelector(
            automationId,
            text,
            type,
            index,
            id,
            matchCount,
            quality,
            fragilityReasons,
            scopedItem.stableItemKey,
            scopedItem.collectionScope);

        var args = new Testing.FlowStepArgs();
        string? stepValue = null;
        var secretEnvironmentVariable = sensitive && action is Testing.FlowActions.Fill or Testing.FlowActions.SetProperty
            ? Testing.FlowSecretReference.BuildEnvironmentVariable(automationId, name, type, recorder.StepCount + 1)
            : null;
        switch (action)
        {
            case Testing.FlowActions.Fill:
                if (secretEnvironmentVariable is not null)
                    args.SecretEnvironmentVariable = secretEnvironmentVariable;
                else
                    stepValue = args.Text = value;
                break;
            case Testing.FlowActions.SetProperty:
                args.Name = name;
                args.ValueSource = valueSource;
                if (secretEnvironmentVariable is not null)
                    args.SecretEnvironmentVariable = secretEnvironmentVariable;
                else
                    stepValue = args.Value = value;
                break;
            case Testing.FlowActions.Navigate: stepValue = Clean(value); args.Route = stepValue; break;
            case Testing.FlowActions.SetTheme: stepValue = value; args.Theme = value; break;
            case Testing.FlowActions.Scroll:
                args.Dx = dx; args.Dy = dy; args.ItemIndex = itemIndex;
                args.Position = position;
                break;
        }
        if (IsEmptyArgs(args)) args = null;

        List<Testing.FlowAssert>? asserts;
        try
        {
            asserts = ParseAsserts(assertsJson);
        }
        catch (Exception ex)
        {
            return (false, -1, recorder.StepCount, false, $"Invalid assertsJson: {ex.Message}");
        }
        if (asserts is not null)
        {
            foreach (var assertion in asserts)
            {
                var assertionTargetMayBeSecret =
                    string.Equals(assertion.Name, "Text", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assertion.Name, "Value", StringComparison.OrdinalIgnoreCase);
                if (assertion.Kind == "propEquals" &&
                    (Testing.FlowSecretReference.LooksSensitive(assertion.Name) ||
                     (assertionTargetMayBeSecret && Testing.FlowSecretReference.LooksSensitive(
                         assertion.Selector?.AutomationId,
                         assertion.Selector?.Text,
                         assertion.Selector?.Type,
                         assertion.Selector?.Id))))
                {
                    assertion.Expected = "<redacted>";
                    assertion.Verify = false;
                    assertion.Note = "Sensitive values are not persisted or asserted.";
                }
            }
        }
        if (secretEnvironmentVariable is not null && asserts is not null)
        {
            foreach (var assertion in asserts.Where(static assertion => assertion.Kind == "propEquals"))
            {
                assertion.Expected = "<redacted>";
                assertion.Verify = false;
                assertion.Note = "Sensitive values are supplied at replay time and are not persisted or asserted.";
            }
        }
        if (asserts is null && target is not null && !target.IsEmpty)
        {
            if (action == Testing.FlowActions.Fill && secretEnvironmentVariable is null)
            {
                asserts =
                [
                    new Testing.FlowAssert
                    {
                        Kind = "propEquals",
                        Selector = target,
                        Name = "Text",
                        Expected = stepValue ?? string.Empty,
                        Verify = true,
                    },
                ];
            }
            else if (action == Testing.FlowActions.SetProperty &&
                     secretEnvironmentVariable is null &&
                     !string.IsNullOrEmpty(name))
            {
                asserts =
                [
                    new Testing.FlowAssert
                    {
                        Kind = "propEquals",
                        Selector = target,
                        Name = name,
                        Expected = stepValue ?? string.Empty,
                        Verify = true,
                    },
                ];
            }
        }

        // Fail fast: reject a step that would not replay, using the exact replay validation rules.
        var candidate = new Testing.FlowStep
        {
            Seq = 1,
            Action = action,
            Target = target,
            Value = stepValue,
            Args = args,
            Page = page,
            Navigated = navigated,
            Asserts = asserts,
        };
        var probe = new Testing.MauiFlow();
        probe.Steps.Add(candidate);
        var validation = Testing.FlowValidator.Validate(probe);
        if (!validation.Ok)
            return (false, -1, recorder.StepCount, false, "Step rejected: " + string.Join("; ", validation.Errors));

        MauiSelectorEvidence? selectorEvidence = null;
        if (selectorObservation?.Target is not null)
        {
            if (selectorObservation.Truncated == true)
            {
                selectorEvidence = new MauiSelectorEvidence
                {
                    Fingerprint = MauiElementFingerprintBuilder.Build(selectorObservation),
                    Omissions =
                    [
                        new MauiSelectorEvidenceOmission
                        {
                            Kind = "live-tree",
                            Reason = "The recording observation tree was capped, so uniqueness-dependent selector candidates were not generated.",
                        },
                    ],
                };
            }
            else
            {
                var generated = MauiSelectorCandidateGenerator.Generate(selectorObservation);
                selectorEvidence = new MauiSelectorEvidence
                {
                    Fingerprint = generated.Fingerprint,
                    Candidates = generated.Candidates,
                    Omissions = generated.Omissions,
                };
            }
        }

        var seq = recorder.AppendStep(
            action,
            target,
            stepValue,
            args,
            page,
            navigated,
            asserts,
            selectorEvidence);
        if (seq < 0)
            return (false, -1, recorder.StepCount, false, $"Recording is full (max {Testing.FlowRecorder.MaxSteps} steps).");

        var fragile = IsFragileSelector(target);
        return (true, seq, recorder.StepCount, fragile, null);
    }

    [McpServerTool(Name = "maui_flow_record_stop"),
     Description("Finish the shared recording and write the workflow test as a .md file (with the authoritative ```json maui-test``` block). " +
                "The recording is validated first; if it has errors the file is not written and remains active. " +
                "Writes under ./maui-tests/<name>.md by default; will not overwrite an existing file unless overwrite=true.")]
    public static async Task<string> RecordStop(
        McpAgentSession session,
        [Description("The recordingId from maui_flow_record_start")] string recordingId,
        [Description("Explicit output .md path (optional; must be inside the current workspace)")] string? file = null,
        [Description("Output directory (optional; default ./maui-tests)")] string? directory = null,
        [Description("Overwrite the output file if it already exists")] bool overwrite = false,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (session is null)
            return StopLocal(recordingId, file, directory, overwrite);

        string? resolved = null;
        try
        {
            using var agent = await session.GetAgentClientAsync(agentPort);
            var status = await agent.ControlMutationRecordingAsync(
                "status", null, null, null, null, recordingId);
            if (!status.Ok || !string.Equals(recordingId, status.RecordingId, StringComparison.Ordinal))
                return Error(status.Error ?? $"Unknown recordingId '{recordingId}'.");

            resolved = ResolveOutputPath(status.Name ?? "scenario", file, directory, out var pathError);
            if (pathError is not null)
                return Error(pathError);
            if (!overwrite && File.Exists(resolved))
                return Error($"File already exists: {resolved}. Pass overwrite=true to replace it.");

            var result = await agent.ControlMutationRecordingAsync("stop", null, null, null, null, recordingId);
            if (!result.Ok || string.IsNullOrEmpty(result.Markdown))
                return Error(result.Error ?? "The shared recording could not be finished.");

            Directory.CreateDirectory(Path.GetDirectoryName(resolved!)!);
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            using var fs = new FileStream(resolved!, mode, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs);
            await writer.WriteAsync(result.Markdown);
            return Json(new { ok = true, file = resolved, steps = result.Steps, warnings = result.Warnings });
        }
        catch (IOException) when (!overwrite && File.Exists(resolved))
        {
            return Error($"File already exists: {resolved}. Pass overwrite=true to replace it.");
        }
        catch (Exception ex)
        {
            return Error($"Could not finish shared recording: {ex.Message}");
        }
    }

    /// <summary>Non-mutating validation of the current recording (shared by MCP stop and HTTP stop).</summary>
    internal static (bool valid, bool empty, string[] errors, string[] warnings) ValidatePeekCore(Testing.FlowRecorder recorder)
    {
        var peek = recorder.Snapshot();
        if (peek.Steps.Count == 0)
            return (false, true, Array.Empty<string>(), Array.Empty<string>());
        var v = Testing.FlowValidator.Validate(peek);
        return (v.Ok, false, v.Errors.ToArray(), v.Warnings.ToArray());
    }

    /// <summary>Closes the recording and serializes it to Markdown, with a parse round-trip safety
    /// net (shared by MCP stop and HTTP stop). Caller decides whether to write a file or return it.</summary>
    internal static (bool ok, string? markdown, Testing.MauiFlow? flow, string? error) FinishToMarkdownCore(Testing.FlowRecorder recorder)
    {
        var flow = recorder.Finish();
        var markdown = Testing.FlowMarkdown.Serialize(flow);
        var roundTrip = Testing.FlowMarkdown.Parse(markdown);
        if (!roundTrip.Ok)
            return (false, null, null, $"Internal: recorded flow did not round-trip ({roundTrip.Error}).");
        return (true, markdown, flow, null);
    }

    [McpServerTool(Name = "maui_flow_record_status"),
     Description("Report the shared in-progress recording and current automatically observed step count.")]
    public static async Task<string> RecordStatus(
        McpAgentSession session,
        [Description("A recordingId to inspect (optional compatibility check)")] string? recordingId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (session is null)
            return StatusLocal(recordingId);

        try
        {
            using var agent = await session.GetAgentClientAsync(agentPort);
            var result = await agent.ControlMutationRecordingAsync(
                "status", null, null, null, null, recordingId);
            if (!string.IsNullOrWhiteSpace(recordingId) &&
                !string.Equals(recordingId, result.RecordingId, StringComparison.Ordinal))
                return Error($"Unknown recordingId '{recordingId}'.");
            return Json(result);
        }
        catch (Exception ex)
        {
            return Error($"Could not read shared recording: {ex.Message}");
        }
    }

    [McpServerTool(Name = "maui_flow_record_cancel"),
     Description("Discard an in-progress recording without writing a file.")]
    public static async Task<string> RecordCancel(
        McpAgentSession session,
        [Description("The recordingId to discard")] string recordingId,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (session is null)
            return CancelLocal(recordingId);

        try
        {
            using var agent = await session.GetAgentClientAsync(agentPort);
            var status = await agent.ControlMutationRecordingAsync(
                "status", null, null, null, null, recordingId);
            if (!string.Equals(recordingId, status.RecordingId, StringComparison.Ordinal))
                return Error($"Unknown recordingId '{recordingId}'.");
            return Json(await agent.ControlMutationRecordingAsync("cancel", null, null, null, null, recordingId));
        }
        catch (Exception ex)
        {
            return Error($"Could not cancel shared recording: {ex.Message}");
        }
    }

    // ── helpers ──

    private static string StartLocal(string name, string? app, string? platform, string? preconditions)
    {
        var id = Testing.FlowRecordingStore.Instance.Start(name, app, platform, preconditions);
        return id is null
            ? Error($"Too many active recordings (max {Testing.FlowRecordingStore.MaxActive}). Stop or cancel one first.")
            : Json(new { ok = true, recordingId = id, name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim(), app, platform });
    }

    private static string StepLocal(
        string recordingId,
        string action,
        string? automationId,
        string? text,
        string? type,
        int? index,
        string? id,
        string? value,
        string? name,
        double? dx,
        double? dy,
        int? itemIndex,
        string? position,
        string? page,
        bool navigated,
        string? assertsJson)
    {
        if (!Testing.FlowRecordingStore.Instance.TryGet(recordingId, out var recorder))
            return Error($"Unknown recordingId '{recordingId}'.");

        var added = AddStepCore(
            recorder, action, automationId, text, type, index, id, value, name,
            dx, dy, itemIndex, position, page, navigated, assertsJson);
        return added.ok
            ? Json(new { ok = true, seq = added.seq, stepCount = added.stepCount, fragile = added.fragile })
            : Error(added.error!);
    }

    private static string StopLocal(string recordingId, string? file, string? directory, bool overwrite)
    {
        if (!Testing.FlowRecordingStore.Instance.TryGet(recordingId, out var recorder))
            return Error($"Unknown recordingId '{recordingId}'.");

        var validation = ValidatePeekCore(recorder);
        if (validation.empty)
            return Error("Recording has no steps. Add steps or cancel it.");
        if (!validation.valid)
        {
            return Json(new
            {
                ok = false,
                error = "Recording has validation errors; fix the offending steps or cancel. Not written.",
                errors = validation.errors,
                warnings = validation.warnings,
            });
        }

        var resolved = ResolveOutputPath(recorder.Name, file, directory, out var pathError);
        if (pathError is not null)
            return Error(pathError);

        var finished = FinishToMarkdownCore(recorder);
        if (!finished.ok)
            return Error($"{finished.error} Not written.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolved!)!);
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            using var fs = new FileStream(resolved!, mode, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs);
            writer.Write(finished.markdown);
        }
        catch (IOException) when (!overwrite && File.Exists(resolved))
        {
            return Error($"File already exists: {resolved}. Pass overwrite=true to replace it.");
        }
        catch (Exception ex)
        {
            return Error($"Could not write flow test: {ex.Message}");
        }

        Testing.FlowRecordingStore.Instance.Remove(recordingId);
        return Json(new
        {
            ok = true,
            file = resolved,
            steps = finished.flow!.Steps.Count,
            warnings = validation.warnings
        });
    }

    private static string StatusLocal(string? recordingId)
    {
        if (!string.IsNullOrWhiteSpace(recordingId))
        {
            if (!Testing.FlowRecordingStore.Instance.TryGet(recordingId!, out var recorder))
                return Error($"Unknown recordingId '{recordingId}'.");
            return Json(new { ok = true, recordingId, name = recorder.Name, steps = recorder.StepCount });
        }

        var active = Testing.FlowRecordingStore.Instance.List()
            .Select(r => new { name = r.Name, steps = r.Steps })
            .ToList();
        return Json(new { ok = true, count = active.Count, active });
    }

    private static string CancelLocal(string recordingId)
    {
        var removed = Testing.FlowRecordingStore.Instance.Remove(recordingId);
        return removed is null
            ? Error($"Unknown recordingId '{recordingId}'.")
            : Json(new { ok = true, cancelled = recordingId });
    }

    /// <summary>Builds a canonical selector keeping ONLY the highest-precedence form provided
    /// (AutomationId &gt; Text &gt; Type+Index &gt; Id), so the JSON is never ambiguous.</summary>
    internal static Testing.FlowSelector? BuildSelector(
        string? automationId,
        string? text,
        string? type,
        int? index,
        string? id,
        int? matchCount = null,
        string? quality = null,
        IReadOnlyList<string>? fragilityReasons = null,
        string? stableItemKey = null,
        string? collectionScope = null)
    {
        if (!string.IsNullOrEmpty(automationId))
        {
            var scoped = Testing.FlowSelector.IsOpaqueStableItemKey(stableItemKey) &&
                !string.IsNullOrWhiteSpace(collectionScope);
            return new Testing.FlowSelector
            {
                AutomationId = automationId,
                StableItemKey = scoped ? stableItemKey : null,
                CollectionScope = scoped ? collectionScope : null,
                MatchCount = scoped ? 1 : matchCount,
                Quality = scoped ? "stable-item-key" : quality ?? (matchCount == 1 ? "durable" : null),
                FragilityReasons = scoped ? null : fragilityReasons?.ToList()
            };
        }
        if (!string.IsNullOrEmpty(text))
            return new Testing.FlowSelector
            {
                Text = text,
                MatchCount = matchCount,
                Quality = quality ?? (matchCount == 1 ? "text" : null),
                FragilityReasons = fragilityReasons?.ToList()
            };
        if (!string.IsNullOrEmpty(type))
            return new Testing.FlowSelector
            {
                TypeIndex = new Testing.FlowTypeIndex { Type = type, Index = index ?? 0 },
                MatchCount = matchCount,
                Quality = quality ?? "fragile",
                FragilityReasons = fragilityReasons?.ToList()
                    ?? ["type-index selector can change when the visual tree changes"]
            };
        if (!string.IsNullOrEmpty(id))
            return new Testing.FlowSelector
            {
                Id = id,
                Quality = "fragile",
                FragilityReasons = ["runtime element id is not stable across rebuilds"]
            };
        return null;
    }

    private static (string? stableItemKey, string? collectionScope) GetScopedStableItem(
        MauiSelectorObservation? observation,
        string? automationId)
    {
        var target = observation?.Target;
        var elements = observation?.Elements;
        if (target is null || elements is null || observation?.Truncated == true ||
            string.IsNullOrWhiteSpace(automationId) ||
            !IsOpaqueStableItemKey(target.StableItemKey) ||
            string.IsNullOrWhiteSpace(target.CollectionScope))
        {
            return (null, null);
        }

        var matches = elements.Count(element =>
            string.Equals(element.AutomationId, automationId, StringComparison.Ordinal) &&
            string.Equals(element.StableItemKey, target.StableItemKey, StringComparison.Ordinal) &&
            string.Equals(element.CollectionScope, target.CollectionScope, StringComparison.Ordinal));
        return matches == 1
            ? (target.StableItemKey, target.CollectionScope)
            : (null, null);
    }

    private static bool IsOpaqueStableItemKey(string? value)
        => Testing.FlowSelector.IsOpaqueStableItemKey(value);

    internal static bool IsFragileSelector(Testing.FlowSelector? selector)
        => Testing.FlowSelector.IsFragile(selector);

    private static List<Testing.FlowAssert>? ParseAsserts(string? assertsJson)
    {
        if (string.IsNullOrWhiteSpace(assertsJson))
            return null;
        if (assertsJson.Length > MaxAssertsJson)
            throw new InvalidOperationException("assertsJson is too large.");

        var parsed = JsonSerializer.Deserialize<List<Testing.FlowAssert>>(assertsJson, ReadOptions);
        if (parsed is not { Count: > 0 })
            return null;
        if (parsed.Any(a => a is null))
            throw new InvalidOperationException("assertion entries must not be null.");

        // Normalize assertion selectors/names the same way as step fields, so a whitespace-only
        // hard-assert selector/name is rejected by validation instead of being written and failing
        // replay.
        foreach (var a in parsed)
        {
            a.Selector = CanonicalizeSelector(a.Selector);
            a.Name = Clean(a.Name);
        }
        return parsed;
    }

    /// <summary>Collapses an arbitrary selector to a canonical single form, treating whitespace-only
    /// fields as missing (so it lands null when nothing usable remains).</summary>
    private static Testing.FlowSelector? CanonicalizeSelector(Testing.FlowSelector? s)
    {
        if (s is null) return null;
        var type = Clean(s.TypeIndex?.Type ?? s.Type);
        var idx = s.TypeIndex?.Index ?? s.Index;
        var text = string.IsNullOrWhiteSpace(s.Text) ? null : s.Text;
        return BuildSelector(
            Clean(s.AutomationId),
            text,
            type,
            idx,
            Clean(s.Id),
            s.MatchCount,
            s.Quality,
            s.FragilityReasons,
            Clean(s.StableItemKey),
            Clean(s.CollectionScope));
    }

    private static bool IsEmptyArgs(Testing.FlowStepArgs a) =>
        a.Selector is null && a.Text is null && a.Name is null && a.Value is null && a.Route is null &&
        a.Theme is null && a.ValueSource is null && a.SecretEnvironmentVariable is null &&
        a.Element is null && a.Dx is null && a.Dy is null && a.ItemIndex is null &&
        a.Position is null && a.Animated is null;

    /// <summary>
    /// Resolves the output path, enforcing: .md extension, containment within the current workspace
    /// (blocks <c>..</c> traversal and absolute paths elsewhere), and that the target is not a directory.
    /// </summary>
    internal static string? ResolveOutputPath(string name, string? file, string? directory, out string? error)
    {
        error = null;
        string root;
        try { root = Path.GetFullPath(Directory.GetCurrentDirectory()); }
        catch { error = "Could not resolve the working directory."; return null; }

        string candidate;
        try
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                candidate = Path.GetFullPath(file);
                if (!candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Flow tests must be written to a .md file.";
                    return null;
                }
            }
            else
            {
                var dir = string.IsNullOrWhiteSpace(directory)
                    ? Path.Combine(root, "maui-tests")
                    : Path.GetFullPath(directory);
                candidate = Path.GetFullPath(Path.Combine(dir, SanitizeFileName(name) + ".md"));
            }
        }
        catch
        {
            error = "Invalid output path.";
            return null;
        }

        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            error = "Output path must be inside the current workspace.";
            return null;
        }
        if (Directory.Exists(candidate))
        {
            error = "Output path is a directory.";
            return null;
        }
        return candidate;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(clean) ? "scenario" : clean;
    }

    private static bool TooLong(string? value, out string? error)
    {
        if (value is not null && value.Length > MaxFieldLen)
        {
            error = $"A field exceeds the {MaxFieldLen}-character limit.";
            return true;
        }
        error = null;
        return false;
    }

    /// <summary>Trims an identifier-like field and treats whitespace-only as missing (null).</summary>
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOpts);
    private static string Error(string error) => JsonSerializer.Serialize(new { ok = false, error }, JsonOpts);
}
