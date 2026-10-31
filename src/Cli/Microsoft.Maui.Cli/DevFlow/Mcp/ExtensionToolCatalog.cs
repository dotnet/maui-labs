using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

/// <summary>
/// Publishes the connected agent's extension tools as first-class MCP tools (<c>ext_{prefix}_{tool}</c>),
/// so an MCP client sees plugin tools in <c>tools/list</c> without knowing about
/// <c>maui_extension_call</c>. The set is refreshed after tool calls and whenever the agent's extension
/// hash changes; changes reach the client as <c>notifications/tools/list_changed</c>.
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
	string? _hash;
	string? _agentUrl;
	int? _lastSelectedPort;
	DateTime _lastRefresh = DateTime.MinValue;

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
	/// Re-reads the agent's extensions when the hash changed. Throttled; never throws, because it runs
	/// beside unrelated tool calls and while no app is running.
	/// </summary>
	public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
	{
		if (_tools is null)
			return;

		// Selecting another agent always refreshes: it usually follows a failed, ambiguous refresh.
		var selectedPort = _session.DefaultAgentPort;
		if (!force && selectedPort == _lastSelectedPort && DateTime.UtcNow - _lastRefresh < RefreshInterval)
			return;
		if (!await _gate.WaitAsync(0, cancellationToken))
			return;

		try
		{
			_lastRefresh = DateTime.UtcNow;
			_lastSelectedPort = selectedPort;
			using var client = await _session.GetAgentClientAsync();
			var status = await client.GetStatusAsync();
			if (status is null)
			{
				Sync(null, null, null);
				return;
			}

			var hash = status.Extensions?.Hash ?? string.Empty;
			if (!force && hash == _hash && client.BaseUrl == _agentUrl)
				return;

			var extensions = (status.Extensions?.Count ?? 0) == 0
				? new Dictionary<string, ExtensionDescriptor>()
				: await client.GetExtensionsAsync();
			Sync(extensions, hash, client.BaseUrl);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// No agent, ambiguous agents, or a stale port: keep what was published and try again later.
			Console.Error.WriteLine($"[DevFlow MCP] Extension tools not refreshed: {ex.Message}");
		}
		finally
		{
			_gate.Release();
		}
	}

	internal void Sync(Dictionary<string, ExtensionDescriptor>? extensions, string? hash, string? agentUrl)
	{
		var wanted = BuildTools(extensions ?? new Dictionary<string, ExtensionDescriptor>());
		lock (_published)
		{
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

				_published[name] = tool;
				_tools?.TryAdd(tool);
			}
		}

		_hash = hash;
		_agentUrl = agentUrl;
	}

	Dictionary<string, ExtensionMcpTool> BuildTools(Dictionary<string, ExtensionDescriptor> extensions)
	{
		var tools = new Dictionary<string, ExtensionMcpTool>(StringComparer.Ordinal);
		var prefixes = extensions.ToDictionary(e => e.Key, e => Sanitize(e.Value.ToolPrefix ?? LastSegment(e.Key)), StringComparer.Ordinal);

		foreach (var (ns, extension) in extensions.OrderBy(e => e.Key, StringComparer.Ordinal))
		{
			if (extension.State == "faulted")
				continue;

			// Two extensions with the same short prefix fall back to their full namespaces.
			var prefix = prefixes.Count(p => p.Value == prefixes[ns]) > 1 ? Sanitize(ns) : prefixes[ns];
			foreach (var tool in extension.Tools)
			{
				var name = BuildToolName(prefix, tool.Name);
				if (tools.ContainsKey(name) || name.Length == ToolNamePrefix.Length)
					continue;
				tools[name] = new ExtensionMcpTool(_session, name, ns, extension, tool);
			}
		}

		return tools;
	}

	internal static string BuildToolName(string prefix, string toolName)
	{
		var name = $"{ToolNamePrefix}{prefix}_{Sanitize(toolName)}";
		return name.Length <= MaxToolNameLength ? name : name[..MaxToolNameLength].TrimEnd('_');
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

		public ExtensionMcpTool(McpAgentSession session, string name, string ns, ExtensionDescriptor extension, ExtensionToolInfo tool)
		{
			_session = session;
			_extensionNamespace = ns;
			_toolName = tool.Name;
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
			using var client = await _session.GetAgentClientAsync();

			// Resolve the route from the agent at call time: the app may have restarted on another port
			// or changed its plugins since the tool list was published.
			var extensions = await client.GetExtensionsAsync();
			if (!extensions.TryGetValue(_extensionNamespace, out var extension))
				throw new McpException($"Extension '{_extensionNamespace}' is no longer registered on the connected agent.");
			if (extension.State == "faulted")
				throw new McpException($"Extension '{_extensionNamespace}' is faulted: {extension.Error}");
			var tool = extension.Tools.FirstOrDefault(t => string.Equals(t.Name, _toolName, StringComparison.Ordinal))
				?? throw new McpException($"Tool '{_toolName}' is no longer registered by extension '{_extensionNamespace}'.");

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

			string body;
			try
			{
				body = await client.CallExtensionToolAsync(tool.Method, tool.Path, parameters);
			}
			catch (HttpRequestException ex)
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
