// This is the single internal boundary that talks to the GitHub Copilot SDK's low-level RPC
// surface. Permission decision factories are currently marked [Experimental("GHCP001")] in
// 1.0.9. We intentionally opt in here and never surface those experimental types publicly.
#pragma warning disable GHCP001

using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// The production <see cref="ICopilotBackend"/> implementation backed by a shared, lazily started
/// <see cref="CopilotClient"/>.
/// </summary>
internal sealed class CopilotClientBackend : ICopilotBackend
{
    private readonly CopilotClientOptions _options;
    private CopilotClient _client;
    private Task? _startTask;
    private bool _disposed;

    public CopilotClientBackend(CopilotSdkConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _options = new CopilotClientOptions();

        if (!string.IsNullOrEmpty(configuration.CliPath))
        {
            _options.Connection = RuntimeConnection.ForStdio(
                configuration.CliPath,
                configuration.CliArguments is { Count: > 0 } args ? [.. args] : null);
        }

        if (!string.IsNullOrEmpty(configuration.GitHubToken))
        {
            _options.GitHubToken = configuration.GitHubToken;
            _options.UseLoggedInUser = false;
        }
        else
        {
            _options.UseLoggedInUser = configuration.UseLoggedInUser;
        }

        if (!string.IsNullOrEmpty(configuration.WorkingDirectory))
        {
            _options.WorkingDirectory = configuration.WorkingDirectory;
        }

        if (!string.IsNullOrEmpty(configuration.BaseDirectory))
        {
            _options.BaseDirectory = configuration.BaseDirectory;
        }

        _client = new CopilotClient(_options);
    }

    public object? GetUnderlyingClient() => _disposed ? null : _client;

    public async Task<ICopilotSession> CreateSessionAsync(
        CopilotSessionParameters parameters,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        var config = new SessionConfig
        {
            Streaming = true,
            OnEvent = onEvent,
            OnPermissionRequest = Adapt(parameters.PermissionHandler),
        };
        Populate(config, parameters);

        var session = await _client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);
        return new SessionAdapter(session);
    }

    public async Task<ICopilotSession> ResumeSessionAsync(
        string sessionId,
        CopilotSessionParameters parameters,
        bool continuePendingWork,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        var config = new ResumeSessionConfig
        {
            Streaming = true,
            OnEvent = onEvent,
            OnPermissionRequest = Adapt(parameters.PermissionHandler),
            ContinuePendingWork = continuePendingWork,
        };
        Populate(config, parameters);

        var session = await _client.ResumeSessionAsync(sessionId, config, cancellationToken).ConfigureAwait(false);
        return new SessionAdapter(session);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var models = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        return [.. models];
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await _client.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    // Lazily start the shared client once. The cached task is reset on failure so a later call can
    // retry. The client is intended to be started by one logical caller sequence; see the concurrency
    // note on CopilotSdkChatClient.
    private Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var task = _startTask ??= StartCoreAsync();
        return task.IsCompleted ? task : task.WaitAsync(cancellationToken);
    }

    private async Task StartCoreAsync()
    {
        try
        {
            await _client.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _startTask = null;
            throw;
        }
    }

    private static void Populate(SessionConfigBase config, CopilotSessionParameters parameters)
    {
        if (!string.IsNullOrEmpty(parameters.Model))
        {
            config.Model = parameters.Model;
        }

        if (!string.IsNullOrEmpty(parameters.ReasoningEffort))
        {
            config.ReasoningEffort = parameters.ReasoningEffort;
        }

        if (!string.IsNullOrEmpty(parameters.WorkingDirectory))
        {
            config.WorkingDirectory = parameters.WorkingDirectory;
        }

        if (!string.IsNullOrEmpty(parameters.SystemInstructions))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = parameters.SystemInstructions,
            };
        }

        if (parameters.ToolDeclarations.Count > 0)
        {
            config.Tools = [.. parameters.ToolDeclarations];
        }

        if (parameters.AvailableTools.Count > 0)
        {
            config.AvailableTools = [.. parameters.AvailableTools];
        }

        if (parameters.ExcludedTools.Count > 0)
        {
            config.ExcludedTools = [.. parameters.ExcludedTools];
        }
    }

    private static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>> Adapt(
        CopilotSdkPermissionHandler handler)
    {
        return async (request, invocation) =>
        {
            var decision = await handler(request, invocation).ConfigureAwait(false);
            return decision == CopilotSdkPermissionDecision.Approve
                ? PermissionDecision.ApproveOnce()
                : PermissionDecision.Reject("Denied by the CopilotSdkChatClient permission policy.");
        };
    }

    private sealed class SessionAdapter(CopilotSession session) : ICopilotSession
    {
        public string SessionId => session.SessionId;

        public Task SendAsync(MessageOptions options, CancellationToken cancellationToken)
            => session.SendAsync(options, cancellationToken);

        public async Task AbortAsync(CancellationToken cancellationToken)
        {
            try
            {
                await session.AbortAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception.GetType().FullName == "GitHub.Copilot.RemoteRpcException")
            {
                // The request may already have completed or the connection may be closing.
            }
            catch (IOException)
            {
                // A broken connection has already stopped the in-flight request.
            }
            catch (ObjectDisposedException)
            {
                // Session/client cleanup raced the best-effort abort.
            }
        }

        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}
