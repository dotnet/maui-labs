using System.Reflection;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>Locally installed host code. Agent descriptors can never install or activate it.</summary>
internal sealed class DevFlowHostPlugins(IServiceProvider services) : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<IServiceProvider, DevFlowHostPlugins> Hosts = new();
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assemblies = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<PluginLoadContext> _contexts = [];
    private readonly object _lifecycleGate = new();
    private Task? _stopTask;
    private bool _initialized;
    private volatile bool _disposed;

    internal static DevFlowHostPlugins Current => ForServices(Program.Services);
    internal static DevFlowHostPlugins ForServices(IServiceProvider services) => Hosts.GetValue(services, sp => new(sp));
    internal static DevFlowHostPlugins? TryGet(IServiceProvider services) => Hosts.TryGetValue(services, out var host) ? host : null;
    internal bool HasPlugins => _entries.Count > 0;
    internal string? DeviceId { get; set; }
    internal DevFlowProviderPolicy ProviderPolicy { get; } = services.GetService<DevFlowProviderPolicy>() ?? new();

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Explicit plugin assemblies are external, untrimmed code loaded only when dynamic code is supported.")]
    public async Task LoadAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ProviderPolicy.Freeze();
            await InitializeAsync(cancellationToken);
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (_assemblies.Contains(fullPath))
                    continue;
                if (!RuntimeFeature.IsDynamicCodeSupported)
                    throw new NotSupportedException("Loading host plugin assemblies requires the JIT build of maui. NativeAOT hosts can register plugins in services instead.");
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("Host plugin assembly was not found.", fullPath);
                var context = new PluginLoadContext(fullPath);
                _contexts.Add(context);
                var assembly = context.LoadFromAssemblyPath(fullPath);
                var types = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(DevFlowHostExtension).IsAssignableFrom(t)).ToArray();
                if (types.Length == 0)
                    throw new InvalidOperationException($"'{fullPath}' contains no DevFlowHostExtension implementations.");
                var entries = RegisterBatch(types.Select<Type, Func<DevFlowHostExtension>>(type =>
                    () => CreatePlugin(type)));
                _assemblies.Add(fullPath);
                await WaitForStartsAsync(entries, cancellationToken);
            }
        }
        finally { _gate.Release(); }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Explicit plugin assemblies are external, untrimmed code loaded only when dynamic code is supported.")]
    private DevFlowHostExtension CreatePlugin(Type type)
        => (DevFlowHostExtension)ActivatorUtilities.CreateInstance(services, type);

    private async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized)
            return;
        var entries = RegisterBatch(services.GetServices<DevFlowHostExtensionRegistration>()
            .Select<DevFlowHostExtensionRegistration, Func<DevFlowHostExtension>>(registration => () => registration.Factory(services)));
        _initialized = true;
        await WaitForStartsAsync(entries, ct);
    }

    private Entry[] RegisterBatch(IEnumerable<Func<DevFlowHostExtension>> factories)
    {
        var entries = factories.Select(factory => new Entry(factory())).ToArray();
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (!namespaces.Add(entry.Plugin.Namespace) || _entries.ContainsKey($"host:{entry.Plugin.Namespace}"))
                    throw new InvalidOperationException($"Duplicate host plugin namespace: {entry.Plugin.Namespace}");
            foreach (var entry in entries)
                _entries[$"host:{entry.Plugin.Namespace}"] = entry;
            foreach (var entry in entries)
                entry.Start = Task.Run(async () =>
                {
                    var plugin = entry.Plugin;
                    try
                    {
                        var logger = services.GetService<ILoggerFactory>()?.CreateLogger($"DevFlow.Host.{plugin.Namespace}") ?? NullLogger.Instance;
                        plugin.Configure(entry.Catalog);
                        entry.Catalog.Freeze();
                        await plugin.StartAsync(new Context(services, logger), _lifetime.Token).ConfigureAwait(false);
                        if (!_lifetime.IsCancellationRequested && entry.State == "starting") entry.State = "ready";
                    }
                    catch (Exception ex)
                    {
                        entry.Error = ex.Message;
                        entry.State = _lifetime.IsCancellationRequested ? "stopping" : "faulted";
                        Console.Error.WriteLine($"[DevFlow host] Plugin '{plugin.Namespace}' failed to start: {ex.Message}");
                    }
                }, CancellationToken.None);
        }
        return entries;
    }

    private static async Task WaitForStartsAsync(Entry[] entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            try { await entry.Start.WaitAsync(TimeSpan.FromSeconds(30), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                entry.Error = ex.Message;
                entry.State = "faulted";
                Console.Error.WriteLine($"[DevFlow host] Plugin '{entry.Plugin.Namespace}' failed to start: {ex.Message}");
            }
        }
    }

    public async Task<Dictionary<string, ExtensionDescriptor>> DescribeAsync(CancellationToken ct = default)
    {
        await LoadAsync([], ct);
        return _entries.ToDictionary(pair => pair.Key, pair => new ExtensionDescriptor
        {
            Version = pair.Value.Plugin.Version,
            Description = pair.Value.Plugin.Description,
            ToolPrefix = $"host_{pair.Value.Plugin.ToolPrefix ?? pair.Value.Plugin.Namespace.Split('.')[^1]}",
            State = pair.Value.State,
            Error = pair.Value.Error,
            ExecutionLocation = "host",
            Tools = pair.Value.Catalog.Operations.Select(operation => new ExtensionToolInfo
            {
                Name = operation.Name, Description = operation.Description,
                Method = "HOST", Path = $"{pair.Key}/{operation.Name}",
                Parameters = operation.Parameters, Returns = operation.Returns,
                Target = operation.Target.ToString().ToLowerInvariant(),
                Annotations = operation.Annotations is { } a ? new ExtensionToolAnnotationsInfo
                {
                    ReadOnly = operation.IsReadOnly, Destructive = a.Destructive, Idempotent = a.Idempotent, Category = a.Category
                } : operation.Method == "GET" ? new ExtensionToolAnnotationsInfo { ReadOnly = true, Idempotent = true } : null
            }).ToList(),
            Streams = pair.Value.Catalog.Streams.Select(stream => new ExtensionStreamInfo
            {
                Name = stream.Name, Description = stream.Description, Columns = stream.Columns.ToList(),
                Path = $"{pair.Key}/{(stream.Name.EndsWith("events", StringComparison.OrdinalIgnoreCase) ? stream.Name : $"{stream.Name}_events")}"
            }).ToList()
        }, StringComparer.Ordinal);
    }

    public async Task<DevFlowResponse> InvokeAsync(string key, string name, JsonElement? parameters,
        DevFlowTarget? target = null, AgentClient? agent = null, CancellationToken ct = default)
    {
        await LoadAsync([], ct);
        if (!_entries.TryGetValue(key, out var entry))
            throw new KeyNotFoundException($"Host plugin '{key}' is not installed.");
        if (entry.State != "ready")
            return DevFlowResponse.Error($"Host plugin '{key}' is {entry.State}: {entry.Error}", 503, $"extension-{entry.State}");
        var operation = entry.Catalog.Operations.FirstOrDefault(o => o.Name == name)
            ?? throw new KeyNotFoundException($"Host operation '{key}/{name}' is not registered.");
        if (operation.Target == DevFlowTargetRequirement.Agent)
        {
            if (agent is null || target?.AgentHost is not { Length: > 0 } host || target.AgentPort is not (> 0 and <= 65535) ||
                !string.Equals(new Uri(agent.BaseUrl).GetLeftPart(UriPartial.Authority),
                    new UriBuilder("http", host, target.AgentPort.Value).Uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                return DevFlowResponse.Error("An agent-targeted host operation requires the selected agent's client.", 400, "invalid-target");
            if (!operation.IsReadOnly)
            {
                var lease = await agent.ControlMutationLeaseAsync("claim");
                if (!lease.YouHold)
                    throw new MutationLeaseException(lease);
            }
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            return await entry.Catalog.InvokeAsync(name, new(parameters, linked.Token, target)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
        catch (ArgumentException ex)
        {
            return DevFlowResponse.Error(ex.Message, 400, "invalid-parameters");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DevFlow host] Operation '{key}/{name}' failed: {ex.Message}");
            return DevFlowResponse.Error(ex.Message, 500, "extension-error");
        }
    }

    public async Task<DevFlowResponse> ReadStreamAsync(string key, string streamName, long? since, int limit, CancellationToken ct = default)
    {
        await LoadAsync([], ct);
        if (!_entries.TryGetValue(key, out var entry))
            return DevFlowResponse.NotFound($"Host plugin '{key}' is not installed.");
        if (entry.State != "ready")
            return DevFlowResponse.Error($"Host plugin '{key}' is {entry.State}: {entry.Error}", 503, $"extension-{entry.State}");
        var stream = entry.Catalog.Streams.FirstOrDefault(s => s.Name == streamName);
        return stream is null ? DevFlowResponse.NotFound($"Stream '{streamName}' is not registered.")
            : DevFlowResponse.Json(DevFlowOperationCatalog.StreamPage(stream.Buffer.Read(since ?? stream.Buffer.LastSequence, Math.Clamp(limit, 1, 1000))));
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            if (_stopTask is not null) return new(_stopTask);
            _disposed = true;
            _stopTask = StopAsync(_entries.Values.ToArray());
            return new(_stopTask);
        }
    }

    private async Task StopAsync(Entry[] entries)
    {
        if (Hosts.TryGetValue(services, out var current) && ReferenceEquals(current, this)) Hosts.Remove(services);
        var stops = entries.Select(entry => Task.Run(async () =>
        {
            entry.State = "stopping";
            try { await entry.Start.ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"[DevFlow host] Startup ended during shutdown: {ex.Message}"); }
            try { await entry.Plugin.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"[DevFlow host] Plugin shutdown failed: {ex.Message}"); }
        })).ToArray();
        var cancellation = Task.Run(async () =>
        {
            try { await _lifetime.CancelAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"[DevFlow host] Plugin cancellation callback failed: {ex.Message}"); }
        });
        var cleanup = Task.WhenAll(stops.Append(cancellation));
        _ = cleanup.ContinueWith(_ => _lifetime.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { Console.Error.WriteLine("[DevFlow host] Plugin shutdown exceeded five seconds."); }
    }

    private sealed class Entry(DevFlowHostExtension plugin)
    {
        public DevFlowHostExtension Plugin { get; } = plugin;
        public DevFlowOperationCatalog Catalog { get; } = new();
        public Task Start { get; set; } = Task.CompletedTask;
        public volatile string State = "starting";
        public volatile string? Error;
    }

    private sealed class Context(IServiceProvider services, ILogger logger) : IDevFlowExtensionContext
    {
        public IServiceProvider Services => services;
        public ILogger Logger => logger;
        public string Platform => RuntimeInformation.OSDescription;
        public string AgentVersion => typeof(DevFlowHostPlugins).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private sealed class PluginLoadContext(string path) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(DevFlowHostExtension).Assembly.GetName().Name ||
                name.Name?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true)
            {
                var shared = Default.Assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
                if (shared is not null) return shared;
            }
            return _resolver.ResolveAssemblyToPath(name) is { } resolved ? LoadFromAssemblyPath(resolved) : null;
        }
        protected override nint LoadUnmanagedDll(string name)
            => _resolver.ResolveUnmanagedDllToPath(name) is { } resolved ? LoadUnmanagedDllFromPath(resolved) : 0;
    }
}
