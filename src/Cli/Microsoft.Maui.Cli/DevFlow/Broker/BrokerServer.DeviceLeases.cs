using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

public partial class BrokerServer
{
    private const int MaxCompanionLeaseBindings = 1024;
    private static readonly TimeSpan CompanionLeaseBindingLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CompanionLeaseBinding> _companionLeaseBindings =
        new(StringComparer.Ordinal);

    private async Task HandleDeviceMutationLeaseRoute(HttpListenerContext context, string method)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }
        if (context.Request.ContentLength64 > 64 * 1024)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }

        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                64 * 1024);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }
        catch
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
            return;
        }

        // Every read goes through the exception-safe helpers: a wrong-typed value must be a stated
        // 400, not an escaped InvalidOperationException that the outer handler turns into an empty
        // 200 the caller cannot diagnose.
        var action = ReadBoundedString(body, "action", 64)?.ToLowerInvariant() ?? "status";
        var leaseId = ReadBoundedString(body, "leaseId", 256);
        var transactionId = ReadBoundedString(body, "transactionId", 256);
        var deviceId = ReadBoundedString(body, "deviceId", 512);
        if (!TryReadBoolean(body, "catalog", out var catalog) ||
            !TryReadBoolean(body, "force", out var force))
        {
            await WriteJsonResponseAsync(
                context,
                400,
                new JsonObject { ["error"] = "Device lease flags must be booleans." });
            return;
        }
        if (force)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Companion leases never force takeover." });
            return;
        }
        if (string.IsNullOrWhiteSpace(leaseId) || leaseId.Length > 256)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "A bounded leaseId is required." });
            return;
        }
        if (deviceId?.Length > 512 || transactionId?.Length > 256)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Device lease identifiers exceed their bounds." });
            return;
        }
        if (catalog == !string.IsNullOrWhiteSpace(deviceId))
        {
            await WriteJsonResponseAsync(
                context,
                400,
                new JsonObject { ["error"] = "Specify exactly one deviceId or catalog=true." });
            return;
        }
        if (action is not ("claim" or "heartbeat" or "begin" or "end" or "release" or "status"))
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Unknown device lease action." });
            return;
        }
        if (action is "begin" or "end" && string.IsNullOrWhiteSpace(transactionId))
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "transactionId is required." });
            return;
        }

        PurgeCompanionLeaseBindings();
        var resource = catalog ? "catalog" : deviceId!;
        var bindingId = CompanionBindingId(leaseId, resource);
        string? leaseKey;
        if (action == "claim")
        {
            leaseKey = catalog
                ? "device-catalog"
                : await ResolveStableDeviceLeaseKeyAsync(
                    deviceId!,
                    _cts?.Token ?? CancellationToken.None);
            if (leaseKey is null)
            {
                await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = $"Device '{deviceId}' not found." });
                return;
            }
            if (_companionLeaseBindings.Count >= MaxCompanionLeaseBindings &&
                !_companionLeaseBindings.ContainsKey(bindingId))
            {
                await WriteJsonResponseAsync(context, 429, new JsonObject { ["error"] = "Too many companion lease bindings." });
                return;
            }
            _companionLeaseBindings[bindingId] =
                new CompanionLeaseBinding(leaseKey, DateTimeOffset.UtcNow);
        }
        else if (_companionLeaseBindings.TryGetValue(bindingId, out var existing))
        {
            leaseKey = existing.LeaseKey;
            _companionLeaseBindings[bindingId] = existing with { LastSeenAt = DateTimeOffset.UtcNow };
        }
        else
        {
            await WriteJsonResponseAsync(context, 409, new JsonObject { ["error"] = "Claim the device lease before using it." });
            return;
        }

        MutationLeaseSnapshot status;
        try
        {
            status = action == "begin"
                ? _mutationLeases.BeginExclusive(
                    leaseKey,
                    leaseId,
                    transactionId!,
                    "mobile-canvas-mcp",
                    "maui devflow devices host mcp")
                : _mutationLeases.Control(
                    leaseKey,
                    action,
                    leaseId,
                    "mobile-canvas-mcp",
                    "maui devflow devices host mcp",
                    force: false,
                    transactionId);
        }
        catch (ArgumentException exception)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = exception.Message });
            return;
        }

        if (action == "release" && !status.YouHold && !status.HeldByOther)
            _companionLeaseBindings.TryRemove(bindingId, out _);

        await WriteJsonResponseAsync(context, 200, new JsonObject
        {
            ["ok"] = true,
            ["allowed"] = status.Allowed,
            ["youHold"] = status.YouHold,
            ["heldByOther"] = status.HeldByOther,
            ["transactionId"] = status.TransactionId,
            ["holderKind"] = status.HolderKind,
            ["label"] = status.Label,
            ["expiresInMs"] = status.ExpiresInMs,
            ["authorityEpoch"] = status.AuthorityEpoch,
        });
    }

    private async Task<string?> ResolveStableDeviceLeaseKeyAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var devices = await _devices.ListAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        var device = devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        return device is null
            ? null
            : DeviceLeaseKeys.FromTarget(device) ?? DeviceRegistry.DeviceLeaseKey(device.Id);
    }

    private void PurgeCompanionLeaseBindings()
    {
        var cutoff = DateTimeOffset.UtcNow - CompanionLeaseBindingLifetime;
        foreach (var pair in _companionLeaseBindings.Where(pair => pair.Value.LastSeenAt < cutoff).ToArray())
            _companionLeaseBindings.TryRemove(pair.Key, out _);
    }

    private static string CompanionBindingId(string leaseId, string resource) =>
        leaseId + "\u001f" + resource;

    private sealed record CompanionLeaseBinding(string LeaseKey, DateTimeOffset LastSeenAt);
}
