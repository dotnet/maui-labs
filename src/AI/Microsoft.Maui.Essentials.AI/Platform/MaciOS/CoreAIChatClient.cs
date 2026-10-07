#if ENABLE_CORE_AI
using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Experimental, local-only chat client for an app-owned Core AI language-model bundle.
/// Available only in unpublished builds with EnableCoreAI=true.
/// </summary>
[SupportedOSPlatform("ios27.0")]
[SupportedOSPlatform("maccatalyst27.0")]
[SupportedOSPlatform("macos27.0")]
public sealed class CoreAIChatClient : IChatClient
{
	private readonly FoundationModelsChatClient _inner;

	/// <summary>Records the model configuration. Native loading occurs asynchronously on the first request.</summary>
	/// <param name="modelDirectory">Directory containing metadata.json, the .aimodel asset and an embedded tokenizer.</param>
	/// <param name="loggerFactory">Optional logger factory for tool invocation logging.</param>
	/// <param name="functionInvocationServices">Optional services for tool dependency injection.</param>
	public CoreAIChatClient(string modelDirectory, ILoggerFactory? loggerFactory = null, IServiceProvider? functionInvocationServices = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
		if (!OperatingSystem.IsIOSVersionAtLeast(27) && !OperatingSystem.IsMacCatalystVersionAtLeast(27) && !OperatingSystem.IsMacOSVersionAtLeast(27))
			throw new PlatformNotSupportedException("Core AI requires iOS, Mac Catalyst or macOS 27.");

		_inner = new("coreai", Path.GetFullPath(modelDirectory),
			(ILogger?)loggerFactory?.CreateLogger<CoreAIChatClient>() ?? NullLogger.Instance,
			functionInvocationServices);
	}

	/// <inheritdoc />
	public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		=> _inner.GetResponseAsync(messages, options, cancellationToken);

	/// <inheritdoc />
	public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		=> _inner.GetStreamingResponseAsync(messages, options, cancellationToken);

	/// <inheritdoc />
	object? IChatClient.GetService(Type serviceType, object? serviceKey)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		return serviceKey is null && serviceType.IsInstanceOfType(this)
			? this
			: ((IChatClient)_inner).GetService(serviceType, serviceKey);
	}

	/// <inheritdoc />
	void IDisposable.Dispose() => ((IDisposable)_inner).Dispose();
}
#endif
