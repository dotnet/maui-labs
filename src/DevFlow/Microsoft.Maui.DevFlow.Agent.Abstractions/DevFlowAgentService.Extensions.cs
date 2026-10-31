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
    private readonly Dictionary<AgentExtension, Task> _pluginStartTasks = new();
    private CancellationTokenSource? _pluginCts;
    private int _pluginsActivated;

    /// <summary>How long stopping all plugins may take before the agent stops without them.</summary>
    internal TimeSpan PluginStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

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

            // Tracked so stopping waits for a start in progress: an async start must not install
            // subscriptions after StopAsync has already removed them.
            _pluginStartTasks[extension] = Task.Run(async () =>
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
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs every plugin's <see cref="DevFlowExtension.StopAsync"/> once. Each plugin's start is
    /// cancelled and awaited first, and the whole shutdown is bounded by <see cref="PluginStopTimeout"/>
    /// so an unresponsive plugin cannot keep the agent from stopping.
    /// </summary>
    internal async Task StopPluginsAsync()
    {
        var cts = Interlocked.Exchange(ref _pluginCts, null);
        if (cts is null)
            return;

        cts.Cancel();
        var deadline = DateTime.UtcNow + PluginStopTimeout;
        using var stopToken = new CancellationTokenSource(PluginStopTimeout);

        foreach (var extension in _plugins.Where(e => e.Plugin is not null))
        {
            if (_pluginStartTasks.Remove(extension, out var startTask)
                && !await CompletesWithinAsync(startTask, deadline).ConfigureAwait(false))
            {
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' did not finish starting; stopping it anyway.");
            }

            try
            {
                var stopTask = extension.Plugin!.StopAsync(stopToken.Token);
                if (!await CompletesWithinAsync(stopTask, deadline).ConfigureAwait(false))
                    Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' did not stop within {PluginStopTimeout.TotalSeconds:0}s.");
                else
                    await stopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' failed to stop: {ex.Message}");
            }
        }
        cts.Dispose();
    }

    /// <summary>
    /// Runs <see cref="StopPluginsAsync"/> from <see cref="Dispose"/>, which native hosts call to stop
    /// the agent. Off the calling thread, so a plugin that needs the UI thread cannot deadlock it.
    /// </summary>
    private void StopPluginsForDispose()
    {
        if (_pluginCts is null)
            return;

        try { Task.Run(StopPluginsAsync).Wait(PluginStopTimeout + TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (task.IsCompleted)
            return true;
        if (remaining <= TimeSpan.Zero)
            return false;
        return await Task.WhenAny(task, Task.Delay(remaining)).ConfigureAwait(false) == task;
    }

    private void ReportLoadError(string extension, Exception ex)
    {
        _extensionLoadErrors.Add(new ExtensionLoadError(extension, ex.Message));
        Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension}' could not be loaded: {ex.Message}");
    }

    /// <summary>Refuses a stream subscription before the upgrade when its plugin is faulted.</summary>
    private static HttpResponse? AdmitExtensionStream(AgentExtension extension)
        => extension.Error is null
            ? null
            : HttpResponse.Error($"Extension '{extension.Namespace}' is faulted: {extension.Error}", 503, "extension-faulted");

    private async Task HandleExtensionStreamWebSocket(
        AgentExtension extension,
        ExtensionStreamDescriptor descriptor,
        System.Net.Sockets.TcpClient client,
        System.Net.Sockets.NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        // Without ?since the client only wants what happens from now on.
        var lastSent = request.QueryParams.TryGetValue("since", out var sinceText) && long.TryParse(sinceText, out var parsed)
            ? parsed
            : descriptor.Stream.LastSequence;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Publishes only wake the sender, which then reads the buffer after lastSent. Reading from the
        // buffer keeps delivery in sequence order even when publishers race, and reports real gaps
        // through the page's dropped count.
        var wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
        using var subscription = descriptor.Stream.Subscribe(_ => wake.Writer.TryWrite(true));

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

        async Task DrainAsync()
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
                    return;
            }
        }

        try
        {
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
                if (extension.Error is not null)
                {
                    // The plugin faulted after this subscription started: end it rather than keep
                    // serving a stream its descriptor now reports as faulted.
                    await AgentHttpServer.WebSocketSendCloseAsync(stream, 1011, "extension faulted", cts.Token);
                    break;
                }

                await DrainAsync();

                try
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    wait.CancelAfter(TimeSpan.FromSeconds(1));
                    await wake.Reader.WaitToReadAsync(wait.Token);
                    wake.Reader.TryRead(out _);
                }
                catch (OperationCanceledException) when (!cts.Token.IsCancellationRequested)
                {
                    // Woken by the timer: check the fault state and keep-alive.
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
