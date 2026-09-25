using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI.ContentSafety;
using Microsoft.Windows.AI.Text;
using Windows.Foundation;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> implementation backed by the Windows AI language model.
/// </summary>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class WindowsAIChatClient : IChatClient
{
	/// <summary>The provider name for this chat client.</summary>
	private const string ProviderName = "windows";

	/// <summary>The default model identifier.</summary>
	private const string DefaultModelId = "windows-ai-language-model";

	/// <summary>Lazily-initialized task that creates the underlying <see cref="LanguageModel"/>.</summary>
	private Task<LanguageModel> _modelTask;

	/// <summary>Whether this instance owns the <see cref="LanguageModel"/> and is responsible for disposing it.</summary>
	private readonly bool _ownsModel;

	/// <summary>
	/// Lazily-initialized metadata describing the implementation.
	/// </summary>
	private ChatClientMetadata? _metadata;

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class.
	/// </summary>
	/// <remarks>
	/// The client will create a <see cref="LanguageModel"/> and reuse it for all requests.
	/// </remarks>
	public WindowsAIChatClient()
	{
		_modelTask = WindowsAIModelFactory.CreateModelAsync();
		_ownsModel = true;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class
	/// with the specified <see cref="LanguageModel"/>.
	/// </summary>
	/// <param name="model">The <see cref="LanguageModel"/> to use for chat interactions.</param>
	/// <remarks>
	/// When using this constructor, the client does not own the <see cref="LanguageModel"/>
	/// and will not dispose it. The caller is responsible for disposing the model.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is <see langword="null"/>.</exception>
	public WindowsAIChatClient(LanguageModel model)
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
		ValidateOptions(options);
		var model = await _modelTask;

		var (systemPrompt, history) = NormalizeChatMessages(chatMessages, options);

		var prompt = ConvertToPrompt(history);
		if (history.Count == 0 && string.IsNullOrEmpty(systemPrompt))
			throw new ArgumentException("At least one message with content is required.", nameof(chatMessages));

		var modelOptions = ConvertToLanguageModelOptions(options);

		// Use StreamingResponseHandler without a chunker — the Windows AI API
		// already provides incremental deltas via the Progress callback.
		var handler = new StreamingResponseHandler();

		// Structured generation has no LanguageModelContext overload, so the system prompt
		// is folded into the prompt text.
		var jsonSchema = GetConstraintSchema(options);

		LanguageModelContext? context = null;
		Action cancel;
		try
		{
			if (jsonSchema is not null)
			{
				var structuredPrompt = string.IsNullOrEmpty(systemPrompt)
					? prompt
					: $"{systemPrompt}{Environment.NewLine}{Environment.NewLine}{prompt}";

				var structuredOperation = model.GenerateStructuredJsonResponseAsync(
					structuredPrompt,
					jsonSchema,
					modelOptions);

				WireUp(structuredOperation, handler, cancellationToken, static result => result.Status switch
				{
					GenerateStructuredJsonResponseStatus.Complete => result.Text,
					GenerateStructuredJsonResponseStatus.PromptLargerThanContext =>
						throw new InvalidOperationException(
							"The prompt is larger than the Windows AI language model's context window. Shorten the conversation or start a new one.",
							result.ExtendedError),
					_ => throw new InvalidOperationException(
						$"Structured response generation failed: {result.Status}", result.ExtendedError)
				});

				cancel = structuredOperation.Cancel;
			}
			else
			{
				context = string.IsNullOrEmpty(systemPrompt)
					? model.CreateContext()
					: model.CreateContext(systemPrompt, new ContentFilterOptions());

				var operation = model.GenerateResponseAsync(context, prompt, modelOptions);

				// Text generation streams through Progress, so the result is only inspected to
				// surface a prompt that did not fit. Other statuses are left alone: the API reports
				// Error for benign cases such as an empty prompt, and callers rely on those
				// completing quietly with no content.
				WireUp(operation, handler, cancellationToken, static result =>
					result.Status is LanguageModelResponseStatus.PromptLargerThanContext
						? throw new InvalidOperationException(
							"The prompt is larger than the Windows AI language model's context window. Shorten the conversation or start a new one.",
							result.ExtendedError)
						: result.Text);

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
	/// <param name="readResult">
	/// Validates the completed result and returns the response text, throwing when the status is not
	/// a success. It is always called, so the status is checked either way, but the text it returns
	/// is only emitted when the operation reported no incremental progress.
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
		Func<TResult, string?> readResult)
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
					// If progress was already reported, only inspect the final status.
					var text = readResult(op.GetResults());

					if (!reportedProgress)
						handler.ProcessContent(text);
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

	/// <summary>
	/// Produces the JSON schema to constrain generation with, or <see langword="null"/> when the
	/// caller did not ask for structured output.
	/// </summary>
	/// <remarks>
	/// Every object in the schema is closed with <c>additionalProperties: false</c>. Constrained
	/// decoding only forbids what the schema forbids, and an open schema lets the model answer under
	/// a property name of its own choosing: asked to fill in a declared <c>text</c> property it has
	/// produced <c>body</c>, <c>response</c> and <c>message</c> instead. Those replies satisfy the
	/// schema, so nothing fails, but the declared property is missing and deserializing it yields
	/// null. Schemas generated from a type by <c>ChatResponseFormat.ForJsonSchema</c> are open by
	/// default, so this affects ordinary structured output and not just tool calling.
	/// </remarks>
	private static string? GetConstraintSchema(ChatOptions? options)
	{
		if ((options?.ResponseFormat as ChatResponseFormatJson)?.Schema is not { } schema)
			return null;

		var node = JsonNode.Parse(schema.GetRawText());
		if (node is null)
			return schema.GetRawText();

		Close(node);

		return node.ToJsonString();

		static void Close(JsonNode? node)
		{
			switch (node)
			{
				case JsonObject obj:
					if (obj.TryGetPropertyValue("properties", out var properties) &&
						properties is JsonObject)
					{
						obj["additionalProperties"] = false;
					}

					foreach (var property in obj.ToList())
						Close(property.Value);
					break;

				case JsonArray array:
					foreach (var item in array)
						Close(item);
					break;
			}
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
		if (_ownsModel)
			DisposeWhenReady(_modelTask);
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

	private static (string SystemPrompt, List<ChatMessage> History) NormalizeChatMessages(
		IEnumerable<ChatMessage> chatMessages,
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
	private static string ConvertToPrompt(IEnumerable<ChatMessage> history)
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
					throw new NotSupportedException(
						"The Windows AI language model accepts text only. Describe images separately before passing them to this client.");
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
#pragma warning disable MEAI001 // Image-generation tool content is experimental in Microsoft.Extensions.AI.
				else if (content is ImageGenerationToolCallContent)
				{
					promptParts.Add($"{rolePrefix}[Image generation requested]");
				}
				else if (content is ImageGenerationToolResultContent imageResult)
				{
					var count = imageResult.Outputs?.OfType<DataContent>()
						.Count(image => image.HasTopLevelMediaType("image")) ?? 0;
					promptParts.Add($"[Image generation result: {count} image(s)]");
				}
#pragma warning restore MEAI001
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

		if (options.Tools is { Count: > 0 })
			throw new NotSupportedException("The Windows AI language model does not support tool calling through the Windows App SDK.");
		if ((options.ToolMode is not null && options.ToolMode != ChatToolMode.Auto &&
			options.ToolMode != ChatToolMode.None) || options.AllowMultipleToolCalls is not null)
			throw new NotSupportedException("The Windows AI language model does not support tool-calling options through the Windows App SDK.");
	}
}
