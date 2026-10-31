using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Agent.Core;

// Hosts DevFlowExtension plugins: creates them from the app's services, maps their routes and
// streams, and runs their start/stop hooks. A plugin failure is reported, never thrown at the app.
public partial class DevFlowAgentService
{
    private readonly HashSet<string> _extensionNamespaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _extensionRouteKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentExtension> _plugins = new();
    private readonly List<ExtensionLoadError> _extensionLoadErrors = new();
    private CancellationTokenSource? _pluginCts;
    private int _pluginsActivated;

    /// <summary>
    /// Service provider plugins are created from. MAUI backends return the app's provider;
    /// the default is <see cref="AgentOptions.Services"/>.
    /// </summary>
    protected virtual IServiceProvider? ExtensionServices => _options.Services;

    /// <summary>Plugins that could not be created, reported in capabilities as <c>extensionErrors</c>.</summary>
    internal IReadOnlyList<ExtensionLoadError> ExtensionLoadErrors => _extensionLoadErrors;

    /// <summary>
    /// Creates and configures every plugin added with <see cref="AgentOptions.AddExtension{T}"/> and
    /// maps its routes. Runs once, before the HTTP server starts listening.
    /// </summary>
    internal void ActivatePlugins()
    {
        if (Interlocked.Exchange(ref _pluginsActivated, 1) == 1 || _options.ExtensionFactories.Count == 0)
            return;

        IServiceProvider? services;
        try { services = ExtensionServices; }
        catch { services = null; }

        foreach (var registration in _options.ExtensionFactories)
        {
            DevFlowExtension plugin;
            try
            {
                plugin = registration.Factory(services);
            }
            catch (Exception ex)
            {
                ReportLoadError(registration.Name, ex);
                continue;
            }

            AgentExtension extension;
            try
            {
                extension = new AgentExtension(plugin.Namespace, plugin.Description, plugin.Version, plugin.Features)
                {
                    Plugin = plugin,
                    ToolPrefix = plugin.ToolPrefix
                };
            }
            catch (Exception ex)
            {
                ReportLoadError(plugin.GetType().FullName ?? plugin.Namespace, ex);
                continue;
            }

            if (_extensionNamespaces.Contains(extension.Namespace))
            {
                ReportLoadError(plugin.GetType().FullName ?? plugin.Namespace,
                    new InvalidOperationException($"Duplicate extension namespace registration: {extension.Namespace}"));
                continue;
            }

            try
            {
                plugin.Configure(extension);
            }
            catch (Exception ex)
            {
                extension.Error = ex.Message;
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' failed to configure: {ex.Message}");
            }

            try
            {
                RegisterExtensionRoutes(extension);
            }
            catch (Exception ex)
            {
                // A route collision with another extension: keep the descriptor so the failure is visible.
                extension.Error ??= ex.Message;
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' routes were not mapped: {ex.Message}");
            }

            _options.Extensions.Add(extension);
            _plugins.Add(extension);
        }
    }

    /// <summary>Runs every plugin's <see cref="DevFlowExtension.StartAsync"/> in the background.</summary>
    internal void StartPlugins()
    {
        if (_plugins.Count == 0 || _pluginCts is not null)
            return;

        _pluginCts = new CancellationTokenSource();
        var ct = _pluginCts.Token;

        IServiceProvider? services;
        try { services = ExtensionServices; }
        catch { services = null; }
        var loggerFactory = services?.GetService<ILoggerFactory>();

        foreach (var extension in _plugins.Where(e => e.Error is null && e.Plugin is not null))
        {
            var context = new ExtensionContext(
                services,
                (ILogger?)loggerFactory?.CreateLogger($"DevFlow.Extension.{extension.Namespace}")
                    ?? (ILogger?)_logProvider?.CreateLogger($"DevFlow.Extension.{extension.Namespace}")
                    ?? NullLogger.Instance,
                PlatformName,
                AgentVersion);

            _ = Task.Run(async () =>
            {
                try
                {
                    await extension.Plugin!.StartAsync(context, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    extension.Error = ex.Message;
                    Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' failed to start: {ex.Message}");
                }
            }, ct);
        }
    }

    /// <summary>Runs every plugin's <see cref="DevFlowExtension.StopAsync"/>.</summary>
    internal async Task StopPluginsAsync()
    {
        var cts = Interlocked.Exchange(ref _pluginCts, null);
        if (cts is null)
            return;

        cts.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var extension in _plugins.Where(e => e.Plugin is not null))
        {
            try
            {
                await extension.Plugin!.StopAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' failed to stop: {ex.Message}");
            }
        }
        cts.Dispose();
    }

    private void ReportLoadError(string extension, Exception ex)
    {
        _extensionLoadErrors.Add(new ExtensionLoadError(extension, ex.Message));
        Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension}' could not be loaded: {ex.Message}");
    }

    private async Task HandleExtensionStreamWebSocket(
        ExtensionStreamDescriptor descriptor,
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        // Without ?since the client only wants what happens from now on.
        var since = request.QueryParams.TryGetValue("since", out var sinceText) && long.TryParse(sinceText, out var parsed)
            ? parsed
            : descriptor.Stream.LastSequence;

        long dropped = 0;
        var queue = Channel.CreateBounded<DevFlowEvent>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref dropped));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Subscribe before replaying so nothing published in between is lost; the sequence check drops repeats.
        using var subscription = descriptor.Stream.Subscribe(evt => queue.Writer.TryWrite(evt));

        var lastSent = since;
        async Task SendAsync(DevFlowEvent? evt, long droppedCount)
        {
            var frame = new Dictionary<string, object?>();
            if (evt is not null)
            {
                frame["seq"] = evt.Sequence;
                frame["ts"] = evt.Timestamp.ToString("O");
                frame["data"] = evt.Data;
            }
            if (droppedCount > 0)
                frame["dropped"] = droppedCount;
            await AgentHttpServer.WebSocketSendTextAsync(stream, JsonSerializer.Serialize(frame, DevFlowJson.Options), cts.Token);
        }

        try
        {
            while (true)
            {
                var page = descriptor.Stream.Read(lastSent, 500);
                if (page.Dropped > 0 && page.Events.Count == 0)
                    await SendAsync(null, page.Dropped);
                for (var i = 0; i < page.Events.Count; i++)
                    await SendAsync(page.Events[i], i == 0 ? page.Dropped : 0);
                lastSent = page.NextSequence;
                if (page.Events.Count < 500)
                    break;
            }

            var readTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var msg = await AgentHttpServer.WebSocketReadTextAsync(stream, cts.Token);
                    if (msg == null) { await cts.CancelAsync(); break; }
                }
            }, cts.Token);

            var lastPing = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                bool available;
                try
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    wait.CancelAfter(TimeSpan.FromSeconds(15));
                    available = await queue.Reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!cts.Token.IsCancellationRequested)
                {
                    available = true;
                }

                if (!available)
                    break;

                while (queue.Reader.TryRead(out var evt))
                {
                    if (evt.Sequence <= lastSent)
                        continue;
                    await SendAsync(evt, Interlocked.Exchange(ref dropped, 0));
                    lastSent = evt.Sequence;
                }

                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 15)
                {
                    await AgentHttpServer.WebSocketSendPingAsync(stream, cts.Token);
                    lastPing = DateTime.UtcNow;
                }
            }

            try { await readTask; } catch { }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            await cts.CancelAsync();
        }
    }

    internal sealed record ExtensionLoadError(string Extension, string Error);

    private sealed class ExtensionContext(IServiceProvider? services, ILogger logger, string platform, string agentVersion)
        : IDevFlowExtensionContext
    {
        public IServiceProvider? Services { get; } = services;
        public ILogger Logger { get; } = logger;
        public string Platform { get; } = platform;
        public string AgentVersion { get; } = agentVersion;
    }
}
