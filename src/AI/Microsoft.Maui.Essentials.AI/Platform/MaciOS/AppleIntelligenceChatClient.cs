using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreGraphics;
using CoreImage;
using ImageIO;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> implementation based on native Apple Intelligence APIs
/// </summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
[SupportedOSPlatform("tvos26.0")]
public sealed partial class AppleIntelligenceChatClient : IChatClient
{
	/// <summary>The provider name for this chat client.</summary>
	private const string ProviderName = "apple";

	/// <summary>The default model identifier.</summary>
	private const string DefaultModelId = "apple-intelligence";

	private readonly ILogger _logger;
	private readonly IServiceProvider? _functionInvocationServices;

	/// <summary>
	/// Initializes a new instance of the <see cref="AppleIntelligenceChatClient"/> class.
	/// </summary>
	public AppleIntelligenceChatClient()
		: this(null, null)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="AppleIntelligenceChatClient"/> class.
	/// </summary>
	/// <param name="loggerFactory">Optional logger factory for logging tool invocations.</param>
	/// <param name="functionInvocationServices">Optional service provider for dependency injection in tool functions.</param>
	public AppleIntelligenceChatClient(ILoggerFactory? loggerFactory = null, IServiceProvider? functionInvocationServices = null)
	{
		_logger = (ILogger?)loggerFactory?.CreateLogger<AppleIntelligenceChatClient>() ?? NullLogger.Instance;
		_functionInvocationServices = functionInvocationServices;
	}

	/// <summary>
	/// Reads the current availability of <c>SystemLanguageModel.default</c> and the
	/// effective capabilities exposed by this provider. The result is not cached readiness.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the status request before the native read.</param>
	/// <returns>The native availability reason and capabilities. Apple provides no make-ready operation.</returns>
	public static Task<ChatModelStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(CreateModelStatus(
			ChatClientNative.GetModelAvailability(),
			ChatClientNative.GetModelCapabilities() is { } capabilities
				? (AppleIntelligenceModelCapabilitiesNative?)capabilities.UInt64Value
				: null));
	}

	internal static ChatModelStatus CreateModelStatus(
		string nativeAvailability,
		AppleIntelligenceModelCapabilitiesNative? nativeCapabilities)
	{
		var availability = nativeAvailability switch
		{
			AppleIntelligenceModelAvailabilityNative.Available => ChatModelAvailability.Available,
			AppleIntelligenceModelAvailabilityNative.DeviceNotEligible => ChatModelAvailability.NotSupported,
			AppleIntelligenceModelAvailabilityNative.AppleIntelligenceNotEnabled => ChatModelAvailability.Disabled,
			AppleIntelligenceModelAvailabilityNative.ModelNotReady => ChatModelAvailability.NotReady,
			_ => ChatModelAvailability.Unknown,
		};

		List<ChatModelCapability> capabilities =
		[
			ChatModelCapability.Text, ChatModelCapability.Streaming,
			ChatModelCapability.StructuredOutput, ChatModelCapability.ToolCalling
		];
		if (nativeCapabilities is { } flags)
		{
			if ((flags & AppleIntelligenceModelCapabilitiesNative.Vision) != 0)
				capabilities.Add(ChatModelCapability.ImageInput);
			capabilities.Add(ChatModelCapability.TokenUsage);
		}

		return new(
			availability,
			availability is ChatModelAvailability.Available ? null : nativeAvailability,
			capabilities);
	}

	// static AppleIntelligenceChatClient()
	// {
	// 	// Enable native logging for debugging purposes, this is quite verbose.
	// 	AppleIntelligenceLogger.Log = (message) => System.Diagnostics.Debug.WriteLine("[Native] " + message);
	// }

	/// <summary>
	/// Lazily-initialized metadata describing the implementation.
	/// </summary>
	private ChatClientMetadata? _metadata;

	internal static AIJsonSchemaTransformCache StrictSchemaTransformCache { get; } =
		new(new()
		{
			DisallowAdditionalProperties = true,
			ConvertBooleanSchemas = true,
			MoveDefaultKeywordToDescription = true,
			RequireAllProperties = true,
			TransformSchemaNode = (ctx, node) =>
			{
				// Handle objects
				if (node is JsonObject obj && obj.TryGetPropertyValue("type", out var typeNode) && typeNode?.GetValue<string>() == "object")
				{
					// All objects need a title
					if (!obj.ContainsKey("title"))
					{
						obj["title"] = Guid.NewGuid().ToString("N");
					}
				}

				return node;
			},
		});

	/// <inheritdoc />
	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var nativeMessages = ToNative(messages, options);
		var nativeOptions = ToNative(options, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		var native = new ChatClientNative();
		var handler = new NonStreamingResponseHandler();

		// Set up cancellation registration before invoking native to avoid race
		CancellationTokenNative? nativeToken = null;
		var registration = cancellationToken.Register(() => nativeToken?.Cancel());

		nativeToken = native.GetResponse(
			nativeMessages,
			nativeOptions,
			onUpdate: (_) => { },
			onComplete: (response, error) =>
			{
				registration.Dispose();
				if (error is not null)
				{
					if (error.Domain == nameof(ChatClientNative) && error.Code == (int)ChatClientError.Cancelled)
						handler.CompleteCancelled(cancellationToken);
					else
						handler.CompleteWithError(new NSErrorException(error));
				}
				else
				{
					try
					{
						handler.Complete(FromNativeChatResponse(response));
					}
					catch (Exception ex)
					{
						handler.CompleteWithError(ex);
					}
				}
			});

		// If cancellation was already requested before native call started
		if (cancellationToken.IsCancellationRequested)
		{
			nativeToken?.Cancel();
		}

		return handler.Task;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var nativeMessages = ToNative(messages, options);
		var nativeOptions = ToNative(options, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		var native = new ChatClientNative();
		// The Swift bridge converts both plain-text snapshots and structured content into
		// append-only deltas before crossing the native boundary.
		var handler = new StreamingResponseHandler(DefaultModelId);

		// Set up cancellation registration before invoking native to avoid race
		CancellationTokenNative? nativeToken = null;
		var registration = cancellationToken.Register(() => nativeToken?.Cancel());

		nativeToken = native.StreamResponse(
			nativeMessages,
			nativeOptions,
			onUpdate: (update) =>
			{
				try
				{
					switch (update.UpdateType)
					{
						case ResponseUpdateTypeNative.Content:
							handler.ProcessContent(update.Text);
							break;
						case ResponseUpdateTypeNative.ToolCall:
							handler.ProcessToolCall(update.ToolCallId, update.ToolCallName, update.ToolCallArguments);
							break;
						case ResponseUpdateTypeNative.ToolResult:
							handler.ProcessToolResult(update.ToolCallId, update.ToolCallResult);
							break;
						default:
							throw new NotSupportedException($"Unsupported update type: {update.UpdateType}");
					}
				}
				catch (Exception ex)
				{
					nativeToken?.Cancel();
					handler.CompleteWithError(ex);
				}
			},
			onComplete: (finalResult, error) =>
			{
				registration.Dispose();
				if (error is not null)
				{
					if (error.Domain == nameof(ChatClientNative) && error.Code == (int)ChatClientError.Cancelled)
						handler.CompleteWithError(new OperationCanceledException(cancellationToken));
					else
						handler.CompleteWithError(new NSErrorException(error));
				}
				else
				{
					try
					{
						handler.Complete(FromNativeChatResponse(finalResult).Usage);
					}
					catch (Exception ex)
					{
						handler.CompleteWithError(ex);
					}
				}
			});

		// If cancellation was already requested before native call started
		if (cancellationToken.IsCancellationRequested)
		{
			nativeToken?.Cancel();
		}

		try
		{
			await foreach (var update in handler.ReadAllAsync(cancellationToken))
			{
				yield return update;
			}
		}
		finally
		{
			// Cancel native operation if consumer stopped iterating early (break, Take, etc.)
			nativeToken?.Cancel();
			registration.Dispose();
		}
	}

	/// <inheritdoc />
	object? IChatClient.GetService(Type serviceType, object? serviceKey)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is not null)
		{
			return null;
		}

		if (serviceType == typeof(ChatClientMetadata))
		{
			return _metadata ??= new ChatClientMetadata(
				providerName: ProviderName,
				defaultModelId: DefaultModelId);
		}

		if (serviceType.IsInstanceOfType(this))
		{
			return this;
		}

		return null;
	}

	/// <inheritdoc />
	void IDisposable.Dispose()
	{
		// Nothing to dispose.
	}

	internal static ChatMessageNative[] ToNative(IEnumerable<ChatMessage> messages, ChatOptions? options)
	{
		ArgumentNullException.ThrowIfNull(messages);

		// Match the OpenAI adapters: unknown roles do not contribute to the request.
		var messagesList = messages.Where(message =>
			message.Role == ChatRole.System ||
			message.Role == ChatRole.User ||
			message.Role == ChatRole.Assistant ||
			message.Role == ChatRole.Tool).ToList();

		var callIdToName = new Dictionary<string, string>();
		var toConvert = !string.IsNullOrWhiteSpace(options?.Instructions)
			? messagesList.Prepend(new(ChatRole.System, options.Instructions))
			: messagesList;

		var nativeMessages = new List<ChatMessageNative>();

		// Resolve tool names in encounter order: native tool outputs require an earlier assistant call.
		foreach (var message in toConvert)
		{
			if (message.Role == ChatRole.Assistant)
			{
				foreach (var call in message.Contents.OfType<FunctionCallContent>())
					callIdToName[call.CallId] = call.Name;
			}
			else if (message.Role == ChatRole.Tool)
			{
				foreach (var result in message.Contents.OfType<FunctionResultContent>())
				{
					if (result.CallId is null || !callIdToName.ContainsKey(result.CallId))
					{
						throw new ArgumentException(
							$"Function result '{result.CallId}' must match a preceding assistant function call.",
							nameof(messages));
					}
				}
			}

			var nativeMessage = ToNative(message, callIdToName);
			if (nativeMessage.Contents.Length > 0)
				nativeMessages.Add(nativeMessage);
		}

		if (nativeMessages.Count == 0)
		{
			throw new ArgumentException("No messages with convertible content found. Ensure at least one message contains TextContent, FunctionCallContent, or FunctionResultContent.", nameof(messages));
		}

		return [.. nativeMessages];
	}

	internal static void ValidateNativeResponse(ChatResponseNative? response)
	{
		if (response is null || response.Messages is null || response.Messages.Length == 0)
			throw new InvalidOperationException("Apple Intelligence returned a null or empty native response.");

		if (response.Messages.Any(message => message is null || message.Contents is null ||
			message.Contents.Any(content => content is null)) ||
			!response.Messages.SelectMany(message => message.Contents)
				.Any(content => content is not null && content is not TextContentNative { Text: null or "" }))
			throw new InvalidOperationException("Apple Intelligence returned a malformed or empty native response.");
	}

	internal static ChatResponse FromNativeChatResponse(ChatResponseNative? response)
	{
		ValidateNativeResponse(response);

		// Convert all native messages to ChatMessage objects
		var messages = response!.Messages
			.Select(FromNative)
			.ToList();

		// Create ChatResponse with all messages and native usage when available.
		return new ChatResponse(messages)
		{
			ModelId = DefaultModelId,
			Usage = FromNativeUsage(response.Usage)
		};
	}

	private static UsageDetails? FromNativeUsage(UsageDetailsNative? usage) =>
		usage is null
			? null
			: new UsageDetails
			{
				InputTokenCount = usage.InputTokenCount,
				OutputTokenCount = usage.OutputTokenCount,
				TotalTokenCount = usage.TotalTokenCount,
				CachedInputTokenCount = usage.CachedInputTokenCount,
				ReasoningTokenCount = usage.ReasoningTokenCount
			};

	private static ChatMessage FromNative(ChatMessageNative nativeMessage)
	{
		var message = new ChatMessage
		{
			Role = FromNative(nativeMessage.Role)
		};

		if (nativeMessage.Contents is not null)
		{
			foreach (var content in nativeMessage.Contents)
			{
				message.Contents.Add(FromNative(content));
			}
		}

		return message;
	}

	private static ChatRole FromNative(ChatRoleNative role) =>
		role switch
		{
			ChatRoleNative.User => ChatRole.User,
			ChatRoleNative.Assistant => ChatRole.Assistant,
			ChatRoleNative.System => ChatRole.System,
			ChatRoleNative.Tool => ChatRole.Tool,
			_ => throw new ArgumentOutOfRangeException(nameof(role), $"Unknown role: {role}")
		};

	private static AIContent FromNative(AIContentNative content) =>
		content switch
		{
			TextContentNative textContent =>
				new TextContent(textContent.Text),

			FunctionCallContentNative functionCall =>
#pragma warning disable IL3050, IL2026
				new FunctionCallContent(
					functionCall.CallId,
					functionCall.Name,
					JsonSerializer.Deserialize<AIFunctionArguments>(
						functionCall.Arguments,
						AIJsonUtilities.DefaultOptions))
				{ InformationalOnly = true },
#pragma warning restore IL3050, IL2026

			FunctionResultContentNative functionResult =>
				new FunctionResultContent(
					functionResult.CallId,
					functionResult.Result),

			ImageContentNative image =>
				FromNative(image),

			_ => throw new ArgumentException($"Unsupported content type: {content.GetType().Name}", nameof(content))
		};

	private static ChatMessageNative ToNative(ChatMessage message, Dictionary<string, string> callIdToName)
	{
		if (message.Role == ChatRole.Tool && message.Contents.Any(content => content switch
		{
			DataContent data => IsImage(data.MediaType),
			UriContent uri => IsImage(uri.MediaType),
			_ => false
		}))
			throw new NotSupportedException("Apple Intelligence tool history does not support image attachments outside FunctionResultContent.");

		AIContentNative[] contents = [.. message.Contents
			.Where(content => content switch
			{
				FunctionCallContent => message.Role == ChatRole.Assistant,
				FunctionResultContent => message.Role == ChatRole.Tool,
				_ => message.Role != ChatRole.Tool
			})
			.SelectMany(content => ToNative(content, callIdToName))];

		// Empty and unknown-only inline turns still occupy a place in the native transcript.
		if (contents.Length == 0 && (message.Role == ChatRole.User ||
			message.Role == ChatRole.Assistant || message.Role == ChatRole.System))
			contents = [new TextContentNative(string.Empty)];

		return new()
		{
			Role = ToNative(message.Role),
			Contents = contents
		};
	}

	private static ChatRoleNative ToNative(ChatRole role)
	{
		if (role == ChatRole.User)
			return ChatRoleNative.User;
		else if (role == ChatRole.Assistant)
			return ChatRoleNative.Assistant;
		else if (role == ChatRole.System)
			return ChatRoleNative.System;
		else if (role == ChatRole.Tool)
			return ChatRoleNative.Tool;
		else
			throw new ArgumentOutOfRangeException(nameof(role), $"The role '{role}' is not supported by Apple Intelligence chat APIs.");
	}

	internal ChatOptionsNative? ToNative(ChatOptions? options, CancellationToken cancellationToken)
		=> ToNative(options, cancellationToken, SupportsOS27Features);

	internal ChatOptionsNative? ToNative(ChatOptions? options, CancellationToken cancellationToken, bool supportsOS27)
	{
		if (options is null)
		{
			return null;
		}

		if (options.MaxOutputTokens is <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options), "The MaxOutputTokens option must be greater than zero.");
		}

		if (options.TopK is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options), "The TopK option must be greater than zero.");

		if (options.TopP is { } topP && (!float.IsFinite(topP) || topP is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options), "The TopP option must be finite and between zero and one.");

		if (options.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options), "The Temperature option must be finite and between zero and one for Apple Intelligence.");

		if (options.Seed is < 0)
			throw new ArgumentOutOfRangeException(nameof(options), "The Seed option must be nonnegative.");

		var tools = options.Tools;
		var toolMode = options.ToolMode;
		var required = toolMode is not null and not AutoChatToolMode and not NoneChatToolMode;
		if (required)
		{
			if (!supportsOS27)
				throw new NotSupportedException("Required tool calling requires Apple OS 27 or later.");

			if (toolMode is RequiredChatToolMode { RequiredFunctionName: { } name })
			{
				tools = tools?.Where(tool => tool.Name == name).ToList();
				if (tools is not { Count: > 0 })
					throw new ArgumentException($"The required tool '{name}' was not supplied.", nameof(options));
			}

			if (tools is not { Count: > 0 })
				throw new ArgumentException("Required tool calling needs at least one tool.", nameof(options));
		}

		return new ChatOptionsNative
		{
			TopK = ToNative(options.TopK),
			TopP = ToNative(options.TopP),
			Seed = ToNative(options.Seed),
			Temperature = ToNative(options.Temperature),
			MaxOutputTokens = ToNative(options.MaxOutputTokens),
			ResponseJsonSchema = ToNative(options.ResponseFormat),
			ToolCallingMode = toolMode is NoneChatToolMode
				? ToolCallingModeNative.Disallowed
				: required ? ToolCallingModeNative.Required : ToolCallingModeNative.Allowed,
			Tools = toolMode is NoneChatToolMode
				? null
				: ToNative(tools, cancellationToken, _functionInvocationServices)
		};
	}

	private AIFunctionToolAdapter[]? ToNative(IList<AITool>? tools, CancellationToken cancellationToken, IServiceProvider? services)
	{
		AIFunctionToolAdapter[]? adapters = null;

		if (tools is { Count: > 0 })
		{
			var unsupportedTools = tools.Where(t => t is not AIFunction).ToList();
			if (unsupportedTools.Count > 0)
			{
				throw new NotSupportedException(
					$"Only AIFunction tools are supported by Apple Intelligence. " +
					$"Convert other tool types using AIFunctionFactory. " +
					$"Unsupported tools: {string.Join(", ", unsupportedTools.Select(t => t.GetType().Name))}");
			}

			adapters = tools
				.OfType<AIFunction>()
				.Select(function => new AIFunctionToolAdapter(function, _logger, cancellationToken, services))
				.ToArray();
		}

		return adapters;
	}

	private static NSString? ToNative(ChatResponseFormat? format) =>
		format switch
		{
			ChatResponseFormatJson { Schema: not null } jsonFormat when StrictSchemaTransformCache.GetOrCreateTransformedSchema(jsonFormat) is { } jsonSchema =>
				(NSString?)jsonSchema.GetRawText(),
			ChatResponseFormatJson jsonFormat when jsonFormat.Schema is not null =>
				throw new InvalidOperationException("Failed to transform JSON schema for Apple Intelligence chat API."),
			_ => null
		};

	private static IEnumerable<AIContentNative> ToNative(AIContent content, Dictionary<string, string> callIdToName) =>
		content switch
		{
			// Apple Intelligence performs better when each text content chunk is separated
			TextContent textContent when textContent.Text is not null => [new TextContentNative(textContent.Text)],
			TextContent => Array.Empty<AIContentNative>(),

			DataContent data when IsImage(data.MediaType) => [ToNativeImageInput(data)],
			UriContent uri when IsImage(uri.MediaType) => [ToNativeImageInput(uri)],

			// Preserve prior tool activity through the native transcript's tool entries.
			FunctionCallContent functionCall => [new FunctionCallContentNative(
				functionCall.CallId ?? string.Empty,
				functionCall.Name,
#pragma warning disable IL3050, IL2026
				functionCall.Arguments is not null ? JsonSerializer.Serialize(functionCall.Arguments, AIJsonUtilities.DefaultOptions) : "{}")],
#pragma warning restore IL3050, IL2026

			FunctionResultContent { CallId: { } callId } functionResult => [new FunctionResultContentNative(
				callId,
				callIdToName[callId],
#pragma warning disable IL3050, IL2026
				// If Result is already a string (common when replaying history), pass through to avoid double-serialization
				functionResult.Result switch
				{
					string s => s,
					not null => JsonSerializer.Serialize(functionResult.Result, AIJsonUtilities.DefaultOptions),
					_ => "{}"
				})],
#pragma warning restore IL3050, IL2026

			// Match the OpenAI adapters by ignoring unrecognized content.
			_ => []
		};

	private static ImageContentNative ToNativeImageInput(AIContent content)
	{
		ValidateImageInput(SupportsOS27Features);
		return content is DataContent data ? ToNative(data) : ToNative((UriContent)content);
	}

	internal static void ValidateImageInput(bool supportsOS27)
	{
		if (!supportsOS27)
			throw new NotSupportedException("Apple Intelligence image input requires Apple OS 27 or later.");
		// The Swift session also checks the current model's vision capability before generation.
	}

	private static bool SupportsOS27Features =>
		OperatingSystem.IsIOSVersionAtLeast(27) ||
		OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
		OperatingSystem.IsMacOSVersionAtLeast(27);

	private static NSNumber? ToNative(int? value) =>
		value.HasValue ? NSNumber.FromInt32(value.Value) : null;

	private static NSNumber? ToNative(double? value) =>
		value.HasValue ? NSNumber.FromDouble(value.Value) : null;

	private static NSNumber? ToNative(long? value) =>
		value.HasValue ? NSNumber.FromInt64(value.Value) : null;

	// internal for unit testing (InternalsVisibleTo).
	internal static bool IsImage(string? mediaType) =>
		mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

	internal static ImageContentNative ToNative(DataContent data)
	{
		if (ToNativeImage(data.RawRepresentation) is { } native)
			return native;

		// Byte fallback: the Swift shim decodes the bytes to a CGImage.
		var bytes = data.Data.ToArray();
		return new ImageContentNative(NSData.FromArray(bytes), data.MediaType ?? "image/png", default, null);
	}

	internal static ImageContentNative ToNative(UriContent uri)
	{
		if (ToNativeImage(uri.RawRepresentation) is { } native)
			return native;

		if (uri.Uri.IsFile)
			return new ImageContentNative(NSUrl.FromFilename(uri.Uri.LocalPath), default, null);

		throw new NotSupportedException(
			"Apple Intelligence image prompts require in-memory DataContent or a file:// UriContent. " +
			"Remote http(s) image URLs are not downloaded automatically.");
	}

	private static ImageContentNative? ToNativeImage(object? representation)
	{
		switch (representation)
		{
			case CGImage cg:
				return new ImageContentNative(cg, default, null);
#if IOS || MACCATALYST
			case UIKit.UIImage ui when ui.CGImage is { } cg:
				return new ImageContentNative(cg, ui.Orientation switch
				{
					UIKit.UIImageOrientation.Up => CGImagePropertyOrientation.Up,
					UIKit.UIImageOrientation.UpMirrored => CGImagePropertyOrientation.UpMirrored,
					UIKit.UIImageOrientation.Down => CGImagePropertyOrientation.Down,
					UIKit.UIImageOrientation.DownMirrored => CGImagePropertyOrientation.DownMirrored,
					UIKit.UIImageOrientation.LeftMirrored => CGImagePropertyOrientation.LeftMirrored,
					UIKit.UIImageOrientation.Right => CGImagePropertyOrientation.Right,
					UIKit.UIImageOrientation.RightMirrored => CGImagePropertyOrientation.RightMirrored,
					UIKit.UIImageOrientation.Left => CGImagePropertyOrientation.Left,
					_ => throw new ArgumentOutOfRangeException(nameof(representation), "Unsupported UIImage orientation.")
				}, null);
#elif MACOS
			case AppKit.NSImage ns when ToCGImage(ns) is { } cg:
				return new ImageContentNative(cg, default, null);
#endif
			default:
				return null;
		}
	}

	internal static AIContent FromNative(ImageContentNative image)
	{
		var orientation = image.Orientation;
		if (orientation is { } value && (int)value is < 1 or > 8)
			throw new InvalidDataException($"Unsupported EXIF orientation: {(int)value}.");

		if (orientation is { } orientedValue && orientedValue != CGImagePropertyOrientation.Up)
		{
			using var decoded = image.CgImage is null ? DecodeImage(image) : null;
			using var source = CIImage.FromCGImage(image.CgImage ?? decoded!);
			using var oriented = source.CreateByApplyingOrientation(orientedValue);
			using var context = new CIContext();
			var rendered = context.CreateCGImage(oriented, oriented.Extent)
				?? throw new InvalidDataException("Failed to render the oriented native image.");
			return new DataContent(EncodePng(rendered), "image/png") { RawRepresentation = rendered };
		}

		var mediaType = image.MimeType ?? "image/png";

		if (image.ImageUrl?.AbsoluteString is { } uri)
			return new UriContent(uri, mediaType) { RawRepresentation = image.CgImage };

		if (image.Data is { } data)
			return new DataContent(data.ToArray(), mediaType) { RawRepresentation = image.CgImage };

		if (image.CgImage is { } cg)
			return new DataContent(EncodePng(cg), "image/png") { RawRepresentation = cg };

		throw new InvalidDataException("The native image attachment has no image payload or file URL.");
	}

	private static CGImage DecodeImage(ImageContentNative image)
	{
		using var source = image.Data is { } data
			? CGImageSource.FromData(data)
			: image.ImageUrl is { IsFileUrl: true } url
				? CGImageSource.FromUrl(url)
				: throw new InvalidDataException("An oriented native image requires bytes or a local file URL.");
		return source?.CreateImage(0, new CGImageOptions())
			?? throw new InvalidDataException("Failed to decode the oriented native image.");
	}

	private static byte[] EncodePng(CGImage image)
	{
		using var data = new NSMutableData();
		using var destination = CGImageDestination.Create(data, "public.png", 1)
			?? throw new InvalidOperationException("Failed to create a PNG image destination.");
		destination.AddImage(image);
		if (!destination.Close())
			throw new InvalidOperationException("Failed to encode the image to PNG.");
		return data.ToArray();
	}

#if MACOS
	private static CGImage? ToCGImage(AppKit.NSImage image)
	{
		var rect = new CGRect(0, 0, image.Size.Width, image.Size.Height);
		return image.AsCGImage(ref rect, null, null);
	}
#endif

	private sealed partial class AIFunctionToolAdapter(AIFunction function, ILogger logger, CancellationToken cancellationToken, IServiceProvider? services) : AIToolNative
	{
		// Swift invokes tools on native threads without the managed request context.
		private readonly NativeCallbackContext _requestContext = new();

		public override string Name => function.Name;

		public override string Desc => function.Description;

		public override string ArgumentsSchema => function.JsonSchema.GetRawText();

		public override string OutputSchema => function.ReturnJsonSchema?.GetRawText() ?? "{\"type\":\"string\"}";

#pragma warning disable IL3050, IL2026 // DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled
		public override async void CallWithArguments(NSString arguments, AIToolCompletionHandler completionHandler)
		{
			await _requestContext.RunAsync(() => InvokeAsync(arguments, completionHandler));
		}

		private async Task InvokeAsync(NSString arguments, AIToolCompletionHandler completionHandler)
		{
			try
			{
				ArgumentNullException.ThrowIfNull(arguments);

				var argsString = (string)arguments;

				// Log before invocation — matches FunctionInvokingChatClient pattern
				if (logger.IsEnabled(LogLevel.Trace))
				{
					LogInvokingSensitive(logger, function.Name, argsString);
				}
				else if (logger.IsEnabled(LogLevel.Debug))
				{
					LogInvoking(logger, function.Name);
				}

				var startingTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

				var aiArgs = JsonSerializer.Deserialize<AIFunctionArguments>(argsString, AIJsonUtilities.DefaultOptions) ?? new();
				aiArgs.Services = services;

				var result = await function.InvokeAsync(aiArgs, cancellationToken: cancellationToken);

				var resultJson = result is not null
					? JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions)
					: "{}";

				// Log after invocation
				var duration = System.Diagnostics.Stopwatch.GetElapsedTime(startingTimestamp);
				if (logger.IsEnabled(LogLevel.Trace))
				{
					LogInvocationCompletedSensitive(logger, function.Name, duration, resultJson);
				}
				else if (logger.IsEnabled(LogLevel.Debug))
				{
					LogInvocationCompleted(logger, function.Name, duration);
				}

				completionHandler(new NSString(resultJson), null);
			}
			catch (OperationCanceledException)
			{
				LogInvocationCanceled(logger, function.Name);

				var error = new NSError(new NSString(nameof(ChatClientNative)), (int)ChatClientError.Cancelled);

				completionHandler(null, error);
			}
			catch (Exception ex)
			{
				LogInvocationFailed(logger, function.Name, ex);

				var userInfo = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
					[new NSString(ex.Message)],
					[NSError.LocalizedDescriptionKey]);

				var error = new NSError(new NSString("AIFunctionToolAdapterErrorDomain"), -1, userInfo);

				completionHandler(null, error);
			}
		}
#pragma warning restore IL3050, IL2026

		[LoggerMessage(LogLevel.Debug, "Invoking {MethodName}.", SkipEnabledCheck = true)]
		private static partial void LogInvoking(ILogger logger, string methodName);

		[LoggerMessage(LogLevel.Trace, "Invoking {MethodName}({Arguments}).", SkipEnabledCheck = true)]
		private static partial void LogInvokingSensitive(ILogger logger, string methodName, string arguments);

		[LoggerMessage(LogLevel.Debug, "{MethodName} invocation completed. Duration: {Duration}")]
		private static partial void LogInvocationCompleted(ILogger logger, string methodName, TimeSpan duration);

		[LoggerMessage(LogLevel.Trace, "{MethodName} invocation completed. Duration: {Duration}. Result: {Result}")]
		private static partial void LogInvocationCompletedSensitive(ILogger logger, string methodName, TimeSpan duration, string result);

		[LoggerMessage(LogLevel.Debug, "{MethodName} invocation canceled.")]
		private static partial void LogInvocationCanceled(ILogger logger, string methodName);

		[LoggerMessage(LogLevel.Error, "{MethodName} invocation failed.")]
		private static partial void LogInvocationFailed(ILogger logger, string methodName, Exception error);
	}

}
