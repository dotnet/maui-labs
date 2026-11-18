using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>
/// Base class for a DevFlow agent plugin. A plugin declares tools, routes and event streams in
/// <see cref="Configure"/>; the agent serves them under <c>/api/v1/ext/{namespace}/…</c> and the
/// <c>maui</c> CLI, MCP server and inspector discover them at runtime.
/// </summary>
/// <remarks>
/// Add a plugin to the agent with <c>AgentOptions.AddExtension&lt;T&gt;()</c>. The agent creates it from
/// the app's service provider when one is available, so the constructor can take app services.
/// A plugin must not throw out of the app: failures in <see cref="Configure"/> or
/// <see cref="StartAsync"/> are logged and reported as a faulted extension.
/// </remarks>
public abstract class DevFlowExtension
{
    private static readonly Regex NamespacePattern = new("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);

    internal static void ValidateIdentity(string ns, string version)
    {
        if (!NamespacePattern.IsMatch(ns))
            throw new ArgumentException("Extension namespace must use reverse-domain notation, e.g. 'com.example.tool'.", "namespace");
        if (!VersionPattern.IsMatch(version))
            throw new ArgumentException("Extension version must be a semantic version, e.g. '1.0.0'.", nameof(version));
    }
    /// <param name="namespace">Reverse-domain namespace, e.g. <c>org.shiny.ble</c>.</param>
    /// <param name="description">One line describing what the plugin exposes.</param>
    /// <param name="version">Semantic version of the plugin's tool surface.</param>
    protected DevFlowExtension(string @namespace, string description, string version = "1.0.0")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        Namespace = @namespace.Trim();
        Description = description?.Trim() ?? string.Empty;
        Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim();
        ValidateIdentity(Namespace, Version);
    }

    /// <summary>Reverse-domain namespace that scopes the plugin's routes.</summary>
    public string Namespace { get; }

    /// <summary>One line describing what the plugin exposes.</summary>
    public string Description { get; }

    /// <summary>Semantic version of the plugin's tool surface.</summary>
    public string Version { get; }

    /// <summary>
    /// Short prefix used when the MCP server publishes this plugin's tools, e.g. <c>ble</c> gives
    /// <c>ext_ble_status</c>. Defaults to the last segment of <see cref="Namespace"/>.
    /// </summary>
    public virtual string? ToolPrefix => null;

    /// <summary>Feature names reported in agent capabilities. Defaults to the tool names.</summary>
    public virtual IEnumerable<string> Features => [];

    /// <summary>
    /// Declares the plugin's routes, tools and streams. Called once, before the agent's HTTP server
    /// starts listening.
    /// </summary>
    protected internal abstract void Configure(IDevFlowExtensionBuilder builder);

    /// <summary>
    /// Starts observing whatever the plugin monitors. Called after the agent's HTTP server starts.
    /// </summary>
    protected internal virtual Task StartAsync(IDevFlowExtensionContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>Stops observing. Called when the agent shuts down.</summary>
    protected internal virtual Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
