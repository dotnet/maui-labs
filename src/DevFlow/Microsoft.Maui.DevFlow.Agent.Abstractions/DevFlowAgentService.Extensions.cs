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
    private readonly object _pluginStopGate = new();
    private Task? _pluginStopTask;
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
    /// Creates and configures every plugin added with <see cref="AgentOptions.AddExtension{T}"/> or
    /// registered in the app's services with <c>AddDevFlowExtension</c>, and maps its routes. Runs
    /// once, before the HTTP server starts listening.
    /// </summary>
    internal void ActivatePlugins()
    {
        if (Interlocked.Exchange(ref _pluginsActivated, 1) == 1)
            return;

        IServiceProvider? services;
        try { services = ExtensionServices; }
        catch { services = null; }
        _options.ProviderPolicy.Freeze();

        var registrations = new List<ExtensionRegistration>(_options.ExtensionFactories);
        registrations.AddRange(GetServiceRegistrations(services));
        var created = new HashSet<Type>();

        foreach (var registration in registrations)
        {
            // An app may call a library's registration and also add the same plugin type to the
            // options. Create it once.
            if (registration.ServiceType is { } type && created.Contains(type))
                continue;

            DevFlowExtension plugin;
            try
            {
                plugin = registration.Factory(services);
                if (plugin is DevFlowHostExtension)
                    throw new InvalidOperationException("Host plugins must be installed in the CLI/MCP/Inspector host, not in an app.");
            }
            catch (Exception ex)
            {
                ReportLoadError(registration.Name, ex);
                continue;
            }

            created.Add(plugin.GetType());

            AgentExtension extension;
            try
            {
                extension = new AgentExtension(plugin.Namespace, plugin.Description, plugin.Version, plugin.Features)
                {
                    Plugin = plugin,
                    Bindings = _options.ProviderPolicy.GetBindings(plugin.Namespace),
                    ToolPrefix = plugin.ToolPrefix,
                    State = "starting"
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

    // Plugins the app registered in its services. Resolving the registrations never runs plugin code;
    // each plugin is created separately so one failing constructor is reported on its own.
    private IEnumerable<ExtensionRegistration> GetServiceRegistrations(IServiceProvider? services)
    {
        if (services is null)
            return [];

        try
        {
            return services
                .GetServices<DevFlowExtensionRegistration>()
                .Select(r => new ExtensionRegistration(
                    r.ExtensionType.FullName ?? r.ExtensionType.Name,
                    sp => r.Factory(sp ?? services),
                    r.ExtensionType))
                .ToList();
        }
        catch (Exception ex)
        {
            ReportLoadError("service-registered extensions", ex);
            return [];
        }
    }

    /// <summary>Runs every plugin's <see cref="DevFlowExtension.StartAsync"/> in the background.</summary>
    internal void StartPlugins()
    {
        lock (_pluginStopGate)
            StartPluginsCore();
    }

    private void StartPluginsCore()
    {
        if (_disposed || _plugins.Count == 0 || _pluginCts is not null || _pluginStopTask is { IsCompleted: false })
            return;

        _pluginCts = new CancellationTokenSource();
        _pluginStopTask = null;
        var ct = _pluginCts.Token;

        IServiceProvider? services;
        try { services = ExtensionServices; }
        catch { services = null; }
        var loggerFactory = services?.GetService<ILoggerFactory>();

        foreach (var extension in _plugins.Where(e => e.Error is null && e.Plugin is not null))
        {
            extension.State = "starting";
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
                    if (!ct.IsCancellationRequested)
                        extension.State = "ready";
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
        Task stop;
        lock (_pluginStopGate)
            stop = _pluginStopTask ??= StopPluginsCoreAsync();
        await stop.ConfigureAwait(false);
    }

    private async Task StopPluginsCoreAsync()
    {
        var cts = Interlocked.Exchange(ref _pluginCts, null);
        if (cts is null)
            return;

        var starts = new Dictionary<AgentExtension, Task>(_pluginStartTasks);
        _pluginStartTasks.Clear();
        var deadline = DateTime.UtcNow + PluginStopTimeout;
        foreach (var extension in _plugins) extension.State = "stopping";
        var cancellation = Task.Run(async () =>
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[Microsoft.Maui.DevFlow] Plugin cancellation callback failed: {ex.Message}"); }
        });
        if (!await CompletesWithinAsync(cancellation, deadline).ConfigureAwait(false))
            Console.WriteLine("[Microsoft.Maui.DevFlow] Plugin cancellation callbacks did not finish within the shutdown deadline.");
        var stopToken = new CancellationTokenSource(PluginStopTimeout);
        var stops = new List<Task>();

        foreach (var extension in _plugins.Where(e => e.Plugin is not null))
        {
            extension.State = "stopping";
            if (starts.TryGetValue(extension, out var startTask)
                && !await CompletesWithinAsync(startTask, deadline).ConfigureAwait(false))
            {
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' did not finish starting; cleanup will run when startup finishes.");
            }

            try
            {
                var stopTask = Task.Run(async () =>
                {
                    try
                    {
                        if (startTask is not null) await startTask.ConfigureAwait(false);
                        await extension.Plugin!.StopAsync(stopToken.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Microsoft.Maui.DevFlow] Extension '{extension.Namespace}' failed to stop: {ex.Message}");
                    }
                }, CancellationToken.None);
                stops.Add(stopTask);
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
        // A late start or cancellation callback can still use this token after the deadline.
        _ = Task.WhenAll(starts.Values.Concat(stops).Append(cancellation)).ContinueWith(_ =>
            { stopToken.Dispose(); cts.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Runs <see cref="StopPluginsAsync"/> from <see cref="Dispose"/>, which native hosts call to stop
    /// the agent. Off the calling thread, so a plugin that needs the UI thread cannot deadlock it.
    /// </summary>
    private void StopPluginsForDispose()
    {
        lock (_pluginStopGate)
            if (_pluginCts is null && (_pluginStopTask is null || _pluginStopTask.IsCompleted))
                return;

        var stop = Task.Run(async () =>
        {
            try { await StopPluginsAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[Microsoft.Maui.DevFlow] Plugin cleanup failed: {ex.Message}"); }
        });
        if (_dispatcher is { IsDispatchRequired: false })
        {
            Console.WriteLine("[Microsoft.Maui.DevFlow] Plugin cleanup continues asynchronously; await StopAsync before Dispose to finish UI-thread cleanup.");
            return;
        }
        try { stop.Wait(PluginStopTimeout + TimeSpan.FromSeconds(1)); }
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
        => extension.State == "ready"
            ? null
            : extension.UnavailableResponse();

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
            await AgentHttpServer.WebSocketSendTextAsync(stream, DevFlowJson.Serialize(frame), cts.Token);
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

            await cts.CancelAsync();
            try { await readTask; } catch (OperationCanceledException) { }
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
