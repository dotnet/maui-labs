using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI.ContentSafety;
using Microsoft.Windows.AI.Imaging;
using Microsoft.Windows.AI.Text;
using Microsoft.Windows.AI.Text.Experimental;
using Windows.Foundation;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> implementation based on native Windows Copilot Runtime (Phi Silica)
/// </summary>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class PhiSilicaChatClient : IChatClient
{
	/// <summary>The provider name for this chat client.</summary>
	private const string ProviderName = "windows";

	/// <summary>The default model identifier.</summary>
	private const string DefaultModelId = "phi-silica";

	/// <summary>Lazily-initialized task that creates the underlying <see cref="LanguageModel"/>.</summary>
	private Task<LanguageModel> _modelTask;

	/// <summary>Whether this instance owns the <see cref="LanguageModel"/> and is responsible for disposing it.</summary>
	private readonly bool _ownsModel;

	/// <summary>
	/// Lazily-initialized on-device image description model, created only when a request actually
	/// carries image content.
	/// </summary>
	private Task<ImageDescriptionGenerator>? _imageDescriptionTask;

	/// <summary>
	/// Lazily-created experimental wrapper used for schema-constrained generation.
	/// </summary>
	/// <remarks>
	/// Disposing the wrapper also closes the underlying <see cref="LanguageModel"/>, so a single
	/// instance is cached for the lifetime of the client instead of being created per request.
	/// </remarks>
#pragma warning disable CS8305 // LanguageModelExperimental has no stable equivalent in this SDK line.
	private LanguageModelExperimental? _experimentalModel;
#pragma warning restore CS8305

	/// <summary>
	/// Lazily-initialized metadata describing the implementation.
	/// </summary>
	private ChatClientMetadata? _metadata;

	/// <summary>
	/// Initializes a new instance of the <see cref="PhiSilicaChatClient"/> class.
	/// </summary>
	/// <remarks>
	/// The client will create a <see cref="LanguageModel"/> and reuse it for all requests.
	/// </remarks>
	public PhiSilicaChatClient()
	{
		_modelTask = PhiSilicaModelFactory.CreateModelAsync();
		_ownsModel = true;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PhiSilicaChatClient"/> class
	/// with the specified <see cref="LanguageModel"/>.
	/// </summary>
	/// <param name="model">The <see cref="LanguageModel"/> to use for chat interactions.</param>
	/// <remarks>
	/// When using this constructor, the client does not own the <see cref="LanguageModel"/>
	/// and will not dispose it. The caller is responsible for disposing the model.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is <see langword="null"/>.</exception>
	public PhiSilicaChatClient(LanguageModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		_modelTask = Task.FromResult(model);
		_ownsModel = false;
	}

	/// <inheritdoc />
	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default) =>
		GetStreamingResponseAsync(chatMessages, options, cancellationToken).ToChatResponseAsync(cancellationToken: cancellationToken);

	/// <inheritdoc />
	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var model = await _modelTask;

		var (systemPrompt, history) = NormalizeChatMessages(chatMessages, options);

		var prompt = await ConvertToPromptAsync(history);
		if (history.Count == 0 && string.IsNullOrEmpty(systemPrompt))
			throw new ArgumentException("At least one message with content is required.", nameof(chatMessages));

		ValidateOptions(options);

		var modelOptions = ConvertToLanguageModelOptions(options);

		// Use StreamingResponseHandler without a chunker — the Windows AI API
		// already provides incremental deltas via the Progress callback.
		var handler = new StreamingResponseHandler();

		// A JSON schema turns this into a constrained generation. GenerateStructuredJsonResponseAsync
		// lives on LanguageModelExperimental and has no LanguageModelContext overload, so the system
		// prompt is folded into the prompt text.
		var jsonSchema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText();

		LanguageModelContext? context = null;
		Action cancel;
		try
		{
			if (jsonSchema is not null)
			{
				var structuredPrompt = string.IsNullOrEmpty(systemPrompt)
					? prompt
					: $"{systemPrompt}{Environment.NewLine}{Environment.NewLine}{prompt}";

				// CS8305: schema-constrained generation is only exposed on the experimental
				// LanguageModelExperimental surface in Windows App SDK 2.2.x. There is no stable
				// equivalent on this SDK line, so the warning is acknowledged rather than avoided.
#pragma warning disable CS8305
				var structuredModel = _experimentalModel ??= new LanguageModelExperimental(model);

				var structuredOperation = structuredModel.GenerateStructuredJsonResponseAsync(
					structuredPrompt,
					jsonSchema,
					LanguageModelOptionsExperimental.GetForLanguageModelOptions(modelOptions));

				// Structured generation does not report incremental progress: the constrained JSON
				// is only available from the completed result.
				WireUp(structuredOperation, handler, cancellationToken, static result =>
					result.Status is GenerateStructuredJsonResponseStatus.Complete
						? result.Text
						: throw new InvalidOperationException(
							$"Structured response generation failed: {result.Status}", result.ExtendedError));

				cancel = structuredOperation.Cancel;
#pragma warning restore CS8305
			}
			else
			{
				context = string.IsNullOrEmpty(systemPrompt)
					? model.CreateContext()
					: model.CreateContext(systemPrompt, new ContentFilterOptions());

				var operation = model.GenerateResponseAsync(context, prompt, modelOptions);
				WireUp(operation, handler, cancellationToken);
				cancel = operation.Cancel;
			}

			var registration = cancellationToken.Register(cancel);
			try
			{
				await foreach (var update in handler.ReadAllAsync(cancellationToken))
				{
					yield return update;
				}
			}
			finally
			{
				cancel();
				registration.Dispose();
			}
		}
		finally
		{
			context?.Dispose();
		}
	}

	/// <summary>
	/// Bridges a WinRT async operation that reports incremental text into the
	/// <see cref="StreamingResponseHandler"/> pipeline.
	/// </summary>
	/// <param name="operation">The WinRT operation to observe.</param>
	/// <param name="handler">The pipeline to feed.</param>
	/// <param name="cancellationToken">The token reported when the operation is cancelled.</param>
	/// <param name="getFinalText">
	/// Reads the response from the completed result. Used by operations that deliver their whole
	/// response at the end instead of through <c>Progress</c>; it is only consulted when no
	/// progress was reported.
	/// </param>
	/// <remarks>
	/// Both <c>GenerateResponseAsync</c> and <c>GenerateStructuredJsonResponseAsync</c> return
	/// <see cref="IAsyncOperationWithProgress{TResult, TProgress}"/> with a <see cref="string"/>
	/// progress type, but their result types differ, so this is generic over the result.
	/// </remarks>
	private static void WireUp<TResult>(
		IAsyncOperationWithProgress<TResult, string> operation,
		StreamingResponseHandler handler,
		CancellationToken cancellationToken,
		Func<TResult, string?>? getFinalText = null)
	{
		var reportedProgress = false;

		operation.Progress = (_, progress) =>
		{
			if (!string.IsNullOrEmpty(progress))
			{
				reportedProgress = true;
				handler.ProcessContent(progress);
			}
		};

		operation.Completed = (op, status) =>
		{
			if (status == AsyncStatus.Completed)
			{
				try
				{
					if (!reportedProgress && getFinalText is not null)
						handler.ProcessContent(getFinalText(op.GetResults()));
				}
				catch (Exception ex)
				{
					handler.CompleteWithError(ex);
					return;
				}

				handler.Complete();
			}
			else if (status == AsyncStatus.Error)
			{
				handler.CompleteWithError(op.ErrorCode);
			}
			else if (status == AsyncStatus.Canceled)
			{
				handler.CompleteWithError(new OperationCanceledException(cancellationToken));
			}
		};
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
		// The image description model is always created by this instance, so it is always disposed.
		if (_imageDescriptionTask is { } imageDescriptionTask)
			DisposeWhenReady(imageDescriptionTask);

		if (_ownsModel)
		{
			// Disposing the experimental wrapper also closes the underlying model, so it is only
			// safe to dispose when this instance owns that model.
#pragma warning disable CS8305
			_experimentalModel?.Dispose();
#pragma warning restore CS8305

			DisposeWhenReady(_modelTask);
		}
	}

	private static void DisposeWhenReady<T>(Task<T> task)
		where T : IDisposable
	{
		if (task.IsCompletedSuccessfully)
			task.Result.Dispose();
		else
			task.ContinueWith(
				t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); },
				TaskContinuationOptions.ExecuteSynchronously);
	}

	/// <summary>
	/// Produces a text description of an image using the on-device Windows image description model.
	/// </summary>
	/// <param name="image">The image content to describe.</param>
	/// <returns>A caption describing the image.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the image could not be described.</exception>
	private async Task<string> DescribeImageAsync(DataContent image)
	{
		var generator = await (_imageDescriptionTask ??= PhiSilicaModelFactory.CreateImageDescriptionGeneratorAsync());

		using var buffer = await PhiSilicaImageBuffers.DecodeAsync(image.Data);

		var result = await generator.DescribeAsync(
			buffer,
			ImageDescriptionKind.DetailedDescription,
			new ContentFilterOptions());

		if (result.Status is not ImageDescriptionResultStatus.Complete)
			throw new InvalidOperationException($"Image description failed: {result.Status}");

		return result.Description;
	}

	private static (string SystemPrompt, List<ChatMessage> History) NormalizeChatMessages(		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null)
	{
		var messages = chatMessages.ToList();

		// Use system instructions as the system prompt if provided
		if (options?.Instructions is { } system)
			return (system, messages);

		// Extract the first system message as the system prompt
		if (messages.Count > 0 && messages[0].Role == ChatRole.System)
		{
			var systemPrompt = messages[0].Text;
			messages.RemoveAt(0);

			return (systemPrompt, messages);
		}

		return (string.Empty, messages);
	}

	/// <summary>
	/// Flattens the conversation into the single prompt string the language model accepts.
	/// </summary>
	/// <remarks>
	/// Phi Silica is a text-only model, so image content cannot be passed through the way a cloud
	/// multimodal model would accept it. Instead each image is run through the on-device Windows
	/// image description model and the resulting caption is spliced into the prompt in place of the
	/// image. Everything stays local; nothing is uploaded.
	/// </remarks>
	private async Task<string> ConvertToPromptAsync(IEnumerable<ChatMessage> history)
	{
		var promptParts = new List<string>();

		foreach (var message in history)
		{
			// Add role prefix so the model can distinguish speakers in multi-turn conversations.
			// System messages after the first (which becomes the context system prompt) are
			// injected as instructions. User/Assistant labels help the model track the conversation.
			var rolePrefix = message.Role == ChatRole.User ? "User: "
				: message.Role == ChatRole.Assistant ? "Assistant: "
				: message.Role == ChatRole.System ? "System: "
				: "";

			foreach (var content in message.Contents)
			{
				if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
				{
					promptParts.Add($"{rolePrefix}{textContent.Text}");
				}
				else if (content is DataContent data && data.HasTopLevelMediaType("image"))
				{
					var description = await DescribeImageAsync(data);
					promptParts.Add($"{rolePrefix}[Image: {description}]");
				}
				else if (content is FunctionCallContent functionCall)
				{
#pragma warning disable IL3050, IL2026
					var argsJson = functionCall.Arguments is not null
						? System.Text.Json.JsonSerializer.Serialize(functionCall.Arguments)
						: "{}";
					promptParts.Add($"{rolePrefix}[Tool call: {functionCall.Name}({argsJson})]");
#pragma warning restore IL3050, IL2026
				}
				else if (content is FunctionResultContent functionResult)
				{
#pragma warning disable IL3050, IL2026
					var resultStr = functionResult.Result switch
					{
						string s => s,
						not null => System.Text.Json.JsonSerializer.Serialize(functionResult.Result),
						_ => "{}"
					};
#pragma warning restore IL3050, IL2026
					promptParts.Add($"[Tool result: {resultStr}]");
				}
				else if (content is not TextContent)
				{
					throw new ArgumentException($"Unsupported content type: {content.GetType().Name}", nameof(history));
				}
			}
		}

		return string.Join(Environment.NewLine, promptParts);
	}

	private static LanguageModelOptions ConvertToLanguageModelOptions(ChatOptions? options)
	{
		if (options is null)
			return new();

		var languageModelOptions = new LanguageModelOptions();

		if (options.Temperature is { } temp)
			languageModelOptions.Temperature = temp;

		if (options.TopK is { } topK)
		{
			if (topK < 0)
				throw new ArgumentOutOfRangeException(nameof(options), "TopK must be non-negative.");

			languageModelOptions.TopK = (uint)topK;
		}

		if (options.TopP is { } topP)
			languageModelOptions.TopP = topP;

		return languageModelOptions;
	}

	private static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.MaxOutputTokens is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options), "MaxOutputTokens must be greater than zero.");

		// Validate tool types — only AIFunction tools are supported
		if (options.Tools is { Count: > 0 })
		{
			var unsupportedTools = options.Tools.Where(t => t is not AIFunction).ToList();
			if (unsupportedTools.Count > 0)
			{
				throw new NotSupportedException(
					$"Only AIFunction tools are supported by Phi Silica. " +
					$"Unsupported tools: {string.Join(", ", unsupportedTools.Select(t => t.GetType().Name))}");
			}
		}
	}
}
