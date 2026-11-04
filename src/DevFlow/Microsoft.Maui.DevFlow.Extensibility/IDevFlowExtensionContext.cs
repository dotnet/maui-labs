using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>What the agent hands a plugin when it starts.</summary>
public interface IDevFlowExtensionContext
{
    /// <summary>The app's service provider, or null when the host has none (a plain .NET agent).</summary>
    IServiceProvider? Services { get; }

    /// <summary>Logger for the plugin. Output goes to the app log the CLI reads.</summary>
    ILogger Logger { get; }

    /// <summary>Platform the agent runs on, e.g. <c>iOS</c>, <c>Android</c>, <c>MacCatalyst</c>.</summary>
    string Platform { get; }

    /// <summary>Version of the DevFlow agent hosting the plugin.</summary>
    string AgentVersion { get; }
}
