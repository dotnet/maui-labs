using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Provides an <see cref="IChatClient"/> backed by the native Apple Intelligence system model.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
[SupportedOSPlatform("tvos26.0")]
public sealed partial class AppleIntelligenceChatClient : IChatClient
{
	private readonly FoundationModelsChatClient _inner;

	/// <summary>Initializes a client using the system model.</summary>
	public AppleIntelligenceChatClient() : this(null, null) { }

	/// <summary>Initializes a client using the system model.</summary>
	/// <param name="loggerFactory">Optional logger factory for tool invocation logging.</param>
	/// <param name="functionInvocationServices">Optional services for tool dependency injection.</param>
	public AppleIntelligenceChatClient(ILoggerFactory? loggerFactory = null, IServiceProvider? functionInvocationServices = null)
	{
		_inner = new("apple", null,
			(ILogger?)loggerFactory?.CreateLogger<AppleIntelligenceChatClient>() ?? NullLogger.Instance,
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

	internal static AIJsonSchemaTransformCache StrictSchemaTransformCache => FoundationModelsChatClient.StrictSchemaTransformCache;
	internal ChatOptionsNative? ToNative(ChatOptions? options, CancellationToken cancellationToken) => _inner.ToNative(options, cancellationToken);
	internal static bool IsImage(string? mediaType) => FoundationModelsChatClient.IsImage(mediaType);
	internal static ImageContentNative ToNative(DataContent data) => FoundationModelsChatClient.ToNative(data);
	internal static ImageContentNative ToNative(UriContent uri) => FoundationModelsChatClient.ToNative(uri);
	internal static AIContent FromNative(ImageContentNative image) => FoundationModelsChatClient.FromNative(image);
}
