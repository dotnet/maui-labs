using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

public partial class BrokerServer
{
    /// <summary>Largest composite layout request body accepted.</summary>
    private const int MaxCompositeLayoutRequestBytes = 1024 * 1024;

    /// <summary>
    /// <c>POST /api/layout-diagnostics/composite</c> — an agent layout scan correlated with what
    /// the device around the app can independently see.
    /// <para>
    /// The broker is the only component holding both halves: the agent connection that produces
    /// the report and the device pairing that can capture the surfaces the app's own visual tree
    /// cannot contain. It is therefore the only place the correlation can be made honestly.
    /// </para>
    /// <para>
    /// The device half is strictly additive and strictly optional. Every failure — no device host,
    /// no exact pairing, an ambiguous pairing, no hierarchy — still returns the agent's report,
    /// with <c>systemEvidence.status</c> saying why the correlation did not happen. The findings,
    /// the summary, and the agent's diagnostics revision are never rewritten here: the agent owns
    /// the reviewed suppression policy for the scan, so a finding invented by the broker could not
    /// be suppressed by it.
    /// </para>
    /// </summary>
    private async Task HandleCompositeLayoutDiagnostics(HttpListenerContext context, string method)
    {
        if (method != "POST")
        {
            await WriteCompositeLayoutResponse(
                context,
                405,
                new JsonObject { ["ok"] = false, ["error"] = "Method not allowed." });
            return;
        }

        JsonElement body;
        try
        {
            using var document = await ReadBoundedCompositeJson(
                context.Request.InputStream,
                MaxCompositeLayoutRequestBytes);
            body = document.RootElement.Clone();
        }
        catch (InvalidDataException exception)
        {
            await WriteCompositeLayoutResponse(
                context,
                413,
                new JsonObject { ["ok"] = false, ["error"] = exception.Message });
            return;
        }
        catch (JsonException exception)
        {
            await WriteCompositeLayoutResponse(
                context,
                400,
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = $"The composite layout request was not readable JSON: {exception.Message}",
                });
            return;
        }

        var agentId = body.TryGetProperty("agentId", out var agentIdValue) &&
            agentIdValue.ValueKind == JsonValueKind.String
            ? agentIdValue.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(agentId) || !_agents.TryGetValue(agentId, out var connection))
        {
            await WriteCompositeLayoutResponse(
                context,
                404,
                new JsonObject { ["ok"] = false, ["error"] = "The exact broker agent was not found." });
            return;
        }

        LayoutInspectionRequest layoutRequest;
        try
        {
            layoutRequest = body.TryGetProperty("request", out var requestValue) &&
                requestValue.ValueKind == JsonValueKind.Object
                ? CliJson.Deserialize<LayoutInspectionRequest>(requestValue.GetRawText()) ?? new()
                : new();
        }
        catch (JsonException exception)
        {
            await WriteCompositeLayoutResponse(
                context,
                400,
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = $"The layout request was not readable: {exception.Message}",
                });
            return;
        }

        using var agent = new AgentClient("localhost", connection.Registration.Port);
        LayoutDiagnosticsReport? report;
        try
        {
            report = await agent.AnalyzeLayoutAsync(layoutRequest, CancellationToken.None);
        }
        catch (LayoutDiagnosticsException exception)
        {
            await WriteCompositeLayoutResponse(
                context,
                exception.StatusCode is >= 400 and <= 599 ? exception.StatusCode : 502,
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = exception.Message,
                    ["type"] = exception.ErrorType,
                });
            return;
        }
        if (report is null)
        {
            await WriteCompositeLayoutResponse(
                context,
                501,
                new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "The selected agent does not support versioned layout diagnostics.",
                });
            return;
        }

        // The device half is additive. Whatever goes wrong inside it — a hostile host, a transport
        // fault, a malformed recheck request — the caller must still receive the scan the agent
        // already produced, with the reason the correlation did not happen.
        try
        {
            report.SystemEvidence = await ComposeSystemEvidenceAsync(
                report,
                layoutRequest,
                agent,
                agentId,
                connection,
                body.TryGetProperty("deviceId", out var deviceIdValue) &&
                    deviceIdValue.ValueKind == JsonValueKind.String
                    ? deviceIdValue.GetString()
                    : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.SystemEvidence = LayoutSystemEvidenceComposer.Unavailable(
                $"The device layer could not be consulted: {exception.Message}");
        }

        await WriteCompositeLayoutReportAsync(context, report);
    }

    /// <summary>
    /// Resolves the exact device this agent is running inside, captures it, and composes the
    /// evidence. A device is only used when exactly one pairing matches this agent at exact
    /// confidence: a heuristic pairing would attribute one device's dialogs to another device's
    /// app, which is worse than reporting nothing.
    /// </summary>
    private async Task<LayoutSystemEvidence> ComposeSystemEvidenceAsync(
        LayoutDiagnosticsReport report,
        LayoutInspectionRequest layoutRequest,
        AgentClient agent,
        string agentId,
        AgentConnection connection,
        string? requestedDeviceId)
    {
        var health = await _devices.GetHealthAsync();
        if (!health.Available)
            return LayoutSystemEvidenceComposer.Unavailable(health.Reason ?? "No device host is available.");

        var agents = _agents.Values.Select(item => item.Registration).ToArray();
        var matches = (await _devices.ListPairedAsync(agents))
            .Where(item =>
                string.Equals(item.AgentId, agentId, StringComparison.Ordinal) &&
                item.MatchConfidence == DeviceMatchConfidence.Exact &&
                (string.IsNullOrWhiteSpace(requestedDeviceId) ||
                 string.Equals(item.Device.Id, requestedDeviceId, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length != 1)
        {
            return LayoutSystemEvidenceComposer.Unavailable(matches.Length == 0
                ? "No device was paired with this agent at exact confidence."
                : "More than one device matched this agent at exact confidence.");
        }

        var pair = matches[0];
        var uiTask = _devices.CaptureUiAsync(pair.Device.Id, connection.Registration.PackageId);
        var screenshotTask = _devices.ScreenshotAsync(pair.Device.Id);
        // Awaited individually so a fault in one is never left unobserved behind a fault in the
        // other, and so a broken screenshot cannot discard a good hierarchy.
        DeviceUiSnapshot? snapshot = null;
        byte[]? screenshot = null;
        try
        {
            try { snapshot = await uiTask; }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
            try { screenshot = await screenshotTask; }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
        catch (OperationCanceledException)
        {
            // Cancellation propagates, but not before both tasks are observed. The screenshot was
            // started in parallel and nothing else is holding it: leaving it unobserved would
            // resurface its fault on a finalizer thread, long after the request it belonged to.
            ObserveQuietly(uiTask);
            ObserveQuietly(screenshotTask);
            throw;
        }

        // A second immediate scan is the only proof the tree held still while the device was
        // captured. Its own failure is not fatal — it simply leaves the evidence "incomplete".
        LayoutDiagnosticsReport? recheck = null;
        try
        {
            recheck = await agent.AnalyzeLayoutAsync(
                CloneForImmediateRecheck(layoutRequest),
                CancellationToken.None);
        }
        catch (LayoutDiagnosticsException)
        {
        }

        var identityStable = _agents.TryGetValue(agentId, out var current) &&
            string.Equals(
                connection.Registration.InstanceId,
                current.Registration.InstanceId,
                StringComparison.Ordinal);

        return LayoutSystemEvidenceComposer.Compose(
            report,
            recheck,
            pair.Device.Id,
            pair.Device.Display,
            snapshot,
            screenshot,
            connection.Registration.PackageId,
            identityStable);
    }

    /// <summary>
    /// Attaches a no-op observer to a task that is being abandoned, so its fault cannot resurface
    /// as an unobserved <see cref="TaskScheduler.UnobservedTaskException"/> after the request that
    /// started it is gone.
    /// </summary>
    private static void ObserveQuietly(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// The recheck asks the same question with stability waiting turned off, so it measures the
    /// tree as it is right now rather than waiting for it to settle again.
    /// </summary>
    private static LayoutInspectionRequest CloneForImmediateRecheck(LayoutInspectionRequest request)
        => new()
        {
            SchemaVersion = request.SchemaVersion,
            Scope = request.Scope,
            Profile = request.Profile,
            Rules = request.Rules is null ? null : [.. request.Rules],
            MinimumSeverity = request.MinimumSeverity,
            IncludeEvidence = request.IncludeEvidence,
            IncludePasses = request.IncludePasses,
            Stability = new LayoutStabilityOptions { Mode = "immediate" },
            Occlusion = request.Occlusion,
            Privacy = request.Privacy,
            // A body of {"request":{"suppressions":null}} deserializes to null despite the property
            // initializer, so this is coalesced rather than spread blindly.
            Suppressions = request.Suppressions is null ? [] : [.. request.Suppressions],
            SuppressionMode = request.SuppressionMode,
            ElementId = request.ElementId,
            Window = request.Window,
            MaxElements = request.MaxElements,
        };

    private static async Task WriteCompositeLayoutReportAsync(
        HttpListenerContext context,
        LayoutDiagnosticsReport report)
    {
        var reportJson = JsonSerializer.Serialize(
            report,
            DevFlowCliJsonContext.Default.LayoutDiagnosticsReport);
        var payload = $"{{\"ok\":true,\"report\":{reportJson}}}";
        await WriteCompositeLayoutBytesAsync(context, 200, Encoding.UTF8.GetBytes(payload));
    }

    private static Task WriteCompositeLayoutResponse(
        HttpListenerContext context,
        int statusCode,
        JsonObject body)
        => WriteCompositeLayoutBytesAsync(
            context,
            statusCode,
            Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(body, indented: false)));

    private static async Task WriteCompositeLayoutBytesAsync(
        HttpListenerContext context,
        int statusCode,
        byte[] bytes)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <summary>
    /// Reads the request body with a hard ceiling. An unbounded read on a local HTTP listener is
    /// a trivial way to exhaust the broker's memory.
    /// </summary>
    private static async Task<JsonDocument> ReadBoundedCompositeJson(Stream input, int maximumBytes)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(block);
            if (read == 0)
                break;
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"A composite layout request cannot exceed {maximumBytes} bytes.");
            }
            await buffer.WriteAsync(block.AsMemory(0, read));
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer);
    }
}
