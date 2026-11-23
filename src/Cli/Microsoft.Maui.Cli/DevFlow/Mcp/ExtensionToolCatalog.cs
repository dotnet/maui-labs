using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

/// <summary>
/// Publishes the connected agent's extension tools as first-class MCP tools (<c>ext_{prefix}_{tool}</c>),
/// so an MCP client sees plugin tools in <c>tools/list</c> without knowing about
/// <c>maui_extension_call</c>. Trusted local host tools share discovery; changes after tool calls
/// reach the client as <c>notifications/tools/list_changed</c>.
/// </summary>
public sealed partial class ExtensionToolCatalog
{
	internal const string ToolNamePrefix = "ext_";
	const int MaxToolNameLength = 64;
	static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

	readonly McpAgentSession _session;
	readonly McpServerPrimitiveCollection<McpServerTool>? _tools;
	readonly SemaphoreSlim _gate = new(1, 1);
	readonly Dictionary<string, ExtensionMcpTool> _published = new(StringComparer.Ordinal);
	int? _lastSelectedPort;
	string? _lastSelectedHost;
	DateTime _lastRefresh = DateTime.MinValue;
	internal DevFlowHostPlugins HostPlugins { get; set; } = DevFlowHostPlugins.Current;

	public ExtensionToolCatalog(McpAgentSession session, IOptions<McpServerOptions> options)
	{
		_session = session;
		_tools = options.Value.ToolCollection;
	}

	/// <summary>Names currently published, for tests and diagnostics.</summary>
	internal IReadOnlyCollection<string> PublishedToolNames
	{
		get { lock (_published) return _published.Keys.ToArray(); }
	}

	/// <summary>
	/// Re-reads operation providers, including actions whose hot-reload changes have no extension hash. Throttled, because it runs
	/// beside unrelated tool calls and while no app is running.
	/// </summary>
	public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
	{
		if (_tools is null)
			return;

		// Selecting another agent always refreshes: it usually follows a failed, ambiguous refresh.
		var selectedPort = _session.DefaultAgentPort;
		var selectedHost = _session.DefaultAgentHost;
		if (!force && selectedPort == _lastSelectedPort && selectedHost == _lastSelectedHost && DateTime.UtcNow - _lastRefresh < RefreshInterval)
			return;
		if (!await _gate.WaitAsync(0, cancellationToken))
			return;

		var hosts = new Dictionary<string, ExtensionDescriptor>();
		try
		{
			_lastRefresh = DateTime.UtcNow;
			_lastSelectedPort = selectedPort;
			_lastSelectedHost = selectedHost;
			hosts = await HostPlugins.DescribeAsync(cancellationToken);
			using var client = await _session.GetAgentClientAsync();
			var extensions = await client.GetOperationProvidersAsync();
			foreach (var pair in hosts)
			{
				if (!extensions.TryAdd(pair.Key, pair.Value))
					throw new InvalidOperationException($"Agent catalog conflicts with locally installed host identity '{pair.Key}'.");
			}
			Sync(extensions);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// Keep local tools usable when no app is selected or reachable; never route stale app tools.
			Console.Error.WriteLine($"[DevFlow MCP] Extension tools not refreshed: {ex.Message}");
			Sync(hosts);
		}
		finally
		{
			_gate.Release();
		}
	}

	internal void Sync(Dictionary<string, ExtensionDescriptor>? extensions)
	{
		var wanted = BuildTools(extensions ?? new Dictionary<string, ExtensionDescriptor>());

		// Each add or remove raises the collection's Changed event, so the client gets one
		// tools/list_changed per tool that changed. ModelContextProtocol 1.1.0 has no way to batch them;
		// 2.0 adds McpServerPrimitiveCollection.DeferChangedEvents(), which this should use once the
		// repo moves to it.
		lock (_published)
		{
			foreach (var name in wanted.Keys)
				if (_tools is not null && _tools.Any(tool => tool.ProtocolTool.Name == name &&
				    (!_published.TryGetValue(name, out var existing) || !ReferenceEquals(existing, tool))))
					throw new InvalidOperationException($"Provider MCP tool conflicts with an existing tool: {name}");

			foreach (var name in _published.Keys.Where(name => !wanted.ContainsKey(name)).ToArray())
			{
				_tools?.Remove(_published[name]);
				_published.Remove(name);
			}

			foreach (var (name, tool) in wanted)
			{
				if (_published.TryGetValue(name, out var existing))
				{
					if (existing.Signature == tool.Signature)
						continue;
					_tools?.Remove(existing);
				}

				if (_tools is not null && !_tools.TryAdd(tool))
					throw new InvalidOperationException($"Provider MCP tool could not be registered: {name}");
				_published[name] = tool;
			}
		}
	}

	Dictionary<string, ExtensionMcpTool> BuildTools(Dictionary<string, ExtensionDescriptor> extensions)
	{
		var tools = new Dictionary<string, ExtensionMcpTool>(StringComparer.Ordinal);
		var prefixes = extensions.ToDictionary(e => e.Key, e => Sanitize(e.Value.ToolPrefix ?? LastSegment(e.Key)), StringComparer.Ordinal);
		var candidates = new List<(string Namespace, ExtensionDescriptor Extension, ExtensionToolInfo Tool, string Prefix, string Name, bool Namespaced)>();

		foreach (var (ns, extension) in extensions.OrderBy(e => e.Key, StringComparer.Ordinal))
		{
			if (extension.State is not (null or "ready"))
				continue;

			// Two extensions with the same short prefix fall back to their full namespaces.
			var prefix = prefixes.Count(p => p.Value == prefixes[ns]) > 1 ? Sanitize(ns) : prefixes[ns];
			var grantedPrefix = HostPlugins.ProviderPolicy.GetBindings(ns)?.McpToolPrefix;
			prefix = grantedPrefix ?? prefix;
			foreach (var tool in extension.Tools)
			{
				candidates.Add((ns, extension, tool, prefix, BuildToolName(prefix, tool.Name, $"{ns}\n{tool.Name}",
					namespaced: grantedPrefix is null), grantedPrefix is null));
			}
		}
		foreach (var group in candidates.GroupBy(c => c.Name, StringComparer.Ordinal))
		{
			if (group.Count() > 1 && group.Any(c => !c.Namespaced))
				throw new InvalidOperationException($"Approved provider MCP names conflict: {group.Key}");
			foreach (var candidate in group)
			{
				var name = group.Count() > 1
					? BuildToolName(candidate.Prefix, candidate.Tool.Name, $"{candidate.Namespace}\n{candidate.Tool.Name}", disambiguate: true,
						namespaced: candidate.Namespaced)
					: candidate.Name;
				if (!tools.TryAdd(name, new ExtensionMcpTool(_session, HostPlugins, name, candidate.Namespace, candidate.Extension, candidate.Tool)))
					throw new InvalidOperationException($"Duplicate extension MCP tool identity: {candidate.Namespace}/{candidate.Tool.Name}");
			}
		}

		return tools;
	}

	internal static string BuildToolName(string prefix, string toolName, string? identity = null, bool disambiguate = false, bool namespaced = true)
	{
		var sanitizedTool = Sanitize(toolName);
		var name = $"{(namespaced ? ToolNamePrefix : "")}{prefix}_{sanitizedTool}";
		if (!disambiguate && name.Length <= MaxToolNameLength && string.Equals(toolName, sanitizedTool, StringComparison.Ordinal))
			return name;
		var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity ?? $"{prefix}\n{toolName}")))[..16].ToLowerInvariant();
		return $"{name[..Math.Min(name.Length, MaxToolNameLength - suffix.Length - 1)].TrimEnd('_')}_{suffix}";
	}

	static string LastSegment(string ns)
	{
		var index = ns.LastIndexOf('.');
		return index >= 0 ? ns[(index + 1)..] : ns;
	}

	internal static string Sanitize(string value)
		=> NonToolNameCharacters().Replace(value.ToLowerInvariant(), "_").Trim('_');

	[GeneratedRegex("[^a-z0-9]+")]
	private static partial Regex NonToolNameCharacters();

	sealed class ExtensionMcpTool : McpServerTool
	{
		static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

		readonly McpAgentSession _session;
		readonly string _extensionNamespace;
		readonly string _toolName;
		readonly DevFlowHostPlugins _hosts;

		public ExtensionMcpTool(McpAgentSession session, DevFlowHostPlugins hosts, string name, string ns, ExtensionDescriptor extension, ExtensionToolInfo tool)
		{
			_session = session;
			_extensionNamespace = ns;
			_toolName = tool.Name;
			_hosts = hosts;
			var schema = tool.Parameters is { ValueKind: JsonValueKind.Object } parameters ? parameters : EmptyObjectSchema;
			var description = string.IsNullOrWhiteSpace(extension.Description)
				? tool.Description
				: $"{tool.Description} (DevFlow extension {ns}: {extension.Description})";

			ProtocolTool = new Tool
			{
				Name = name,
				Description = description,
				InputSchema = schema,
				Annotations = tool.Annotations is null ? null : new ToolAnnotations
				{
					ReadOnlyHint = tool.Annotations.ReadOnly,
					DestructiveHint = tool.Annotations.Destructive,
					IdempotentHint = tool.Annotations.Idempotent,
					OpenWorldHint = false
				}
			};
			Signature = $"{ns}|{tool.Method}|{tool.Path}|{description}|{schema.GetRawText()}|{tool.Annotations?.ReadOnly}|{tool.Annotations?.Destructive}|{tool.Annotations?.Idempotent}";
		}

		public string Signature { get; }

		public override Tool ProtocolTool { get; }

		public override IReadOnlyList<object> Metadata { get; } = [];

		public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
		{
			JsonElement? parameters = null;
			if (request.Params?.Arguments is { Count: > 0 } arguments)
			{
				using var buffer = new MemoryStream();
				using (var writer = new Utf8JsonWriter(buffer))
				{
					writer.WriteStartObject();
					foreach (var (key, value) in arguments)
					{
						writer.WritePropertyName(key);
						value.WriteTo(writer);
					}
					writer.WriteEndObject();
				}
				using var document = JsonDocument.Parse(buffer.ToArray());
				parameters = document.RootElement.Clone();
			}
			if (_extensionNamespace.StartsWith("host:", StringComparison.Ordinal))
			{
				var providers = await _hosts.DescribeAsync(cancellationToken);
				var operation = providers.GetValueOrDefault(_extensionNamespace)?.Tools.FirstOrDefault(t => t.Name == _toolName)
					?? throw new McpException($"Host operation '{_extensionNamespace}/{_toolName}' is no longer installed.");
				using var agent = operation.Target == "agent" ? await _session.GetAgentClientAsync() : null;
				var target = new DevFlowTarget(_session.HostDeviceId ?? (_session.DefaultAgentPort is { } port ? _session.GetAndroidDeviceSerial(port) : null),
					agent is null ? null : _session.DefaultAgentHost, agent is null ? null : new Uri(agent.BaseUrl).Port);
				DevFlowResponse response;
				try
				{
					response = await _hosts.InvokeAsync(_extensionNamespace, _toolName, parameters, target, agent, cancellationToken);
				}
				catch (Exception ex) when (ex is HttpRequestException or MutationLeaseException)
				{
					throw new McpException($"Host operation '{_extensionNamespace}/{_toolName}' failed: {ex.Message}");
				}
				if (response.StatusCode >= 400)
					throw new McpException(response.Body ?? $"Host operation failed with HTTP {response.StatusCode}.");
				return new CallToolResult { Content = [new TextContentBlock { Text = response.Body ?? Convert.ToBase64String(response.BodyBytes ?? []) }] };
			}
			using var client = await _session.GetAgentClientAsync();

			// Resolve the route from the agent at call time: the app may have restarted on another port
			// or changed its plugins since the tool list was published.
			var extensions = await client.GetOperationProvidersAsync();
			if (!extensions.TryGetValue(_extensionNamespace, out var extension))
				throw new McpException($"Extension '{_extensionNamespace}' is no longer registered on the connected agent.");
			if (extension.State is not (null or "ready"))
				throw new McpException($"Extension '{_extensionNamespace}' is {extension.State}: {extension.Error}");
			var tool = extension.Tools.FirstOrDefault(t => string.Equals(t.Name, _toolName, StringComparison.Ordinal))
				?? throw new McpException($"Tool '{_toolName}' is no longer registered by extension '{_extensionNamespace}'.");
			if (!_hosts.ProviderPolicy.AllowsPath(_extensionNamespace, tool.Path))
				throw new McpException($"Route '{tool.Path}' is outside the bindings approved for '{_extensionNamespace}'.");

			string body;
			try
			{
				body = await client.CallExtensionToolAsync(tool.Method, tool.Path, parameters);
			}
			catch (Exception ex) when (ex is HttpRequestException or MutationLeaseException)
			{
				throw new McpException($"Extension tool '{_extensionNamespace}/{_toolName}' failed: {ex.Message}");
			}

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = body }]
			};
		}
	}
}

/// <summary>Publishes extension tools once at startup, in the background, if an app is already running.</summary>
sealed class ExtensionToolCatalogStartup(ExtensionToolCatalog catalog) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = Task.Run(() => catalog.RefreshAsync(force: true), CancellationToken.None);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
