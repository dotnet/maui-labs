using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>Coordinates explicit route saves and restores with a connected agent.</summary>
public sealed class RouteCheckpointCoordinator
{
    private readonly RouteCheckpointStore _store;
    private readonly Func<AgentRegistration, AgentClient> _createClient;

    public RouteCheckpointCoordinator(
        RouteCheckpointStore? store = null,
        Func<AgentRegistration, AgentClient>? createClient = null)
    {
        _store = store ?? new RouteCheckpointStore();
        _createClient = createClient ?? (registration => new AgentClient("localhost", registration.Port));
    }

    public RouteCheckpointStore Store => _store;

    public RouteCheckpointStatus Status(AgentRegistration registration, bool connected = true)
    {
        var status = _store.Get(StableAgentId(registration), registration.SessionId, connected);
        status.Checkpoint ??= _store.Get(StableAgentId(registration), null, connected).Checkpoint;
        status.HasCheckpoint = status.Checkpoint is not null;
        return status;
    }

    public Task<RouteCheckpointStatus> SaveAsync(AgentRegistration registration, CancellationToken cancellationToken = default)
        => SaveAsync(registration, client: null, cancellationToken);

    /// <summary>Uses a caller-owned client when a host already holds the app mutation lease.</summary>
    public async Task<RouteCheckpointStatus> SaveAsync(
        AgentRegistration registration,
        AgentClient? client,
        CancellationToken cancellationToken = default)
    {
        var ownsClient = client is null;
        client ??= _createClient(registration);
        try
        {
            var status = await client.GetStatusAsync().WaitAsync(cancellationToken);
            if (!RouteCheckpointStore.TryNormalizeRoute(status?.Route, out var route, out var error))
            {
                var existing = Status(registration);
                return new RouteCheckpointStatus
                {
                    Ok = false,
                    Connected = true,
                    Warning = error,
                    Checkpoint = existing.Checkpoint,
                    HasCheckpoint = existing.HasCheckpoint
                };
            }
            var checkpoint = _store.Save(
                StableAgentId(registration),
                registration.SessionId,
                route!,
                registration.AppName,
                registration.Platform,
                registration.Project);
            return new RouteCheckpointStatus
            {
                Connected = true,
                HasCheckpoint = true,
                Checkpoint = checkpoint
            };
        }
        finally
        {
            if (ownsClient)
                client.Dispose();
        }
    }

    public Task<RouteCheckpointStatus> RestoreAsync(AgentRegistration registration, CancellationToken cancellationToken = default)
        => RestoreAsync(registration, client: null, cancellationToken);

    /// <summary>Uses a caller-owned client when a host already holds the app mutation lease.</summary>
    public async Task<RouteCheckpointStatus> RestoreAsync(
        AgentRegistration registration,
        AgentClient? client,
        CancellationToken cancellationToken = default)
    {
        var current = Status(registration);
        if (current.Checkpoint is null)
        {
            current.Ok = false;
            current.Warning = "No saved route checkpoint is available.";
            return current;
        }

        var ownsClient = client is null;
        client ??= _createClient(registration);
        RouteRestoreResult result;
        try
        {
            var navigated = await client.NavigateAsync(current.Checkpoint.Route).WaitAsync(cancellationToken);
            var observed = (await client.GetStatusAsync().WaitAsync(cancellationToken))?.Route;
            result = new RouteRestoreResult
            {
                AttemptedUtc = DateTimeOffset.UtcNow,
                Success = navigated && string.Equals(observed, current.Checkpoint.Route, StringComparison.Ordinal),
                Kind = !navigated ? "navigation-failed" :
                    string.Equals(observed, current.Checkpoint.Route, StringComparison.Ordinal) ? "restored" : "diverged",
                Message = navigated
                    ? string.Equals(observed, current.Checkpoint.Route, StringComparison.Ordinal)
                        ? "Route restored."
                        : "Navigation completed but the agent reported a different route."
                    : "The agent rejected route navigation.",
                ObservedRoute = observed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            result = new RouteRestoreResult
            {
                AttemptedUtc = DateTimeOffset.UtcNow,
                Success = false,
                Kind = "disconnected",
                Message = "The DevFlow agent is unavailable."
            };
        }
        finally
        {
            if (ownsClient)
                client.Dispose();
        }

        _store.RecordRestoreResult(StableAgentId(registration), current.Checkpoint.SessionId, result);
        var outcome = Status(registration);
        outcome.Ok = result.Success;
        outcome.Connected = result.Kind != "disconnected";
        outcome.Warning = result.Success ? null : result.Message;
        return outcome;
    }

    public bool Clear(AgentRegistration registration)
    {
        var stableAgentId = StableAgentId(registration);
        var sessionCleared = _store.Clear(stableAgentId, registration.SessionId);
        var fallbackCleared = _store.Clear(stableAgentId);
        return sessionCleared || fallbackCleared;
    }

    public static string StableAgentId(AgentRegistration registration)
    {
        var identity = !string.IsNullOrWhiteSpace(registration.PackageId)
            ? registration.PackageId!
            : !string.IsNullOrWhiteSpace(registration.SessionId)
                ? registration.SessionId!
                : registration.Project;
        return AgentRegistration.ComputeId(identity, registration.Tfm);
    }
}
