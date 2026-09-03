// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Adapts a vision-capable <see cref="IChatClient"/> to the <see cref="IImageClassificationClient"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The injected chat client must support image <see cref="DataContent"/>. The adapter prefers structured JSON output,
/// but also accepts a top-level JSON string array when a client ignores the requested response format. The label
/// allowlist is snapshotted during construction and matched using ordinal comparison.
/// </para>
/// <para>
/// Disposing this adapter does not dispose the injected chat client. The caller owns both the chat client and all
/// input streams.
/// </para>
/// </remarks>
public sealed class ChatClientImageClassificationClient : IImageClassificationClient
{
	private readonly IChatClient _chatClient;
	private readonly string[] _labels;
	private readonly HashSet<string> _labelSet;
	private readonly ImageClassificationClientMetadata _metadata;

	/// <summary>Initializes a new instance of the <see cref="ChatClientImageClassificationClient"/> class.</summary>
	/// <param name="chatClient">The dedicated vision-capable chat client to adapt.</param>
	/// <param name="labels">The non-empty set of classification labels the model may return.</param>
	/// <param name="metadata">
	/// Optional metadata for this adapter. When omitted, metadata is copied from the injected chat client when available.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="chatClient"/> or <paramref name="labels"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="labels"/> is empty, contains an empty label, or contains duplicate labels.
	/// </exception>
	public ChatClientImageClassificationClient(
		IChatClient chatClient,
		IEnumerable<string> labels,
		ImageClassificationClientMetadata? metadata = null)
	{
		ArgumentNullException.ThrowIfNull(chatClient);
		ArgumentNullException.ThrowIfNull(labels);

		_chatClient = chatClient;
		_labels = labels.ToArray();

		if (_labels.Length == 0)
		{
			throw new ArgumentException("At least one classification label is required.", nameof(labels));
		}

		if (_labels.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException("Classification labels must not be empty or whitespace.", nameof(labels));
		}

		_labelSet = new HashSet<string>(_labels, StringComparer.Ordinal);
		if (_labelSet.Count != _labels.Length)
		{
			throw new ArgumentException("Classification labels must be unique.", nameof(labels));
		}

		_metadata = metadata ?? CreateMetadata(chatClient);
	}

	/// <inheritdoc />
	public async Task<ImageClassificationResult> ClassifyImageAsync(
		Stream imageStream,
		string imageMediaType,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(imageStream);
		ArgumentNullException.ThrowIfNull(imageMediaType);

		ImageClassificationOptions optionsSnapshot = options?.Clone() ?? new();

		if (!imageStream.CanRead)
		{
			throw new ArgumentException("The image stream must be readable.", nameof(imageStream));
		}

		if (optionsSnapshot.MinimumConfidence is not null)
		{
			throw new NotSupportedException(
				$"{nameof(ChatClientImageClassificationClient)} does not produce confidence values.");
		}

		var image = new DataContent(ReadOnlyMemory<byte>.Empty, imageMediaType);
		if (!image.HasTopLevelMediaType("image"))
		{
			throw new ArgumentException("The content media type must be an image media type.", nameof(imageMediaType));
		}

		byte[] imageBytes = await ImageClassificationInput.ReadBytesAsync(
			imageStream,
			optionsSnapshot.MaximumInputBytes,
			cancellationToken,
			nameof(imageStream)).ConfigureAwait(false);

		if (imageBytes.Length == 0)
		{
			throw new ArgumentException("The image stream must not be empty.", nameof(imageStream));
		}

		image = new DataContent(imageBytes, imageMediaType);

		var messages = new[]
		{
			new ChatMessage(
				ChatRole.User,
				[
					new TextContent(CreatePrompt()),
					image
				])
		};
		var chatOptions = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<ChatClientImageClassificationResponse>(
				ChatClientImageClassificationJsonContext.Default.Options)
		};

		ChatResponse response = await _chatClient
			.GetResponseAsync(messages, chatOptions, cancellationToken)
			.ConfigureAwait(false);

		string[] labels;
		try
		{
			using JsonDocument document = JsonDocument.Parse(response.Text);
			labels = document.RootElement.ValueKind switch
			{
				JsonValueKind.Object when HasExactLabelsEnvelope(document.RootElement) => JsonSerializer.Deserialize(
					response.Text,
					ChatClientImageClassificationJsonContext.Default.ChatClientImageClassificationResponse)?.Labels,
				JsonValueKind.Array => JsonSerializer.Deserialize(
					response.Text,
					ChatClientImageClassificationJsonContext.Default.StringArray),
				_ => null
			} ?? throw new JsonException("The response must be a labels object or a string array.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException("The chat client returned a malformed image classification response.", exception);
		}

		var seenLabels = new HashSet<string>(StringComparer.Ordinal);
		foreach (string? label in labels)
		{
			if (label is null || !_labelSet.Contains(label) || !seenLabels.Add(label))
			{
				throw new InvalidOperationException(
					"The chat client returned a label outside the allowlist or returned a duplicate label.");
			}
		}

		IEnumerable<string> selectedLabels = labels;
		if (optionsSnapshot.MaximumPredictions is int maximumPredictions)
		{
			selectedLabels = selectedLabels.Take(maximumPredictions);
		}

		return new ImageClassificationResult(
			selectedLabels.Select(static label => new ImageClassificationPrediction(label)))
		{
			ModelId = response.ModelId ?? _metadata.DefaultModelId,
			RawRepresentation = response,
			AdditionalProperties = response.AdditionalProperties
		};
	}

	private static bool HasExactLabelsEnvelope(JsonElement root)
	{
		JsonElement.ObjectEnumerator properties = root.EnumerateObject();
		return properties.MoveNext() &&
			properties.Current.NameEquals("labels") &&
			!properties.MoveNext();
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is null)
		{
			if (serviceType == typeof(ImageClassificationClientMetadata))
			{
				return _metadata;
			}

			if (serviceType.IsInstanceOfType(this))
			{
				return this;
			}

			if (serviceType.IsInstanceOfType(_chatClient))
			{
				return _chatClient;
			}
		}

		return _chatClient.GetService(serviceType, serviceKey);
	}

	/// <summary>
	/// Releases this adapter without disposing the injected chat client, which remains owned by the caller.
	/// </summary>
	public void Dispose()
	{
	}

	private static ImageClassificationClientMetadata CreateMetadata(IChatClient chatClient)
	{
		var chatMetadata = chatClient.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
		return new ImageClassificationClientMetadata(
			chatMetadata?.ProviderName,
			chatMetadata?.ProviderUri,
			chatMetadata?.DefaultModelId);
	}

	private string CreatePrompt()
		=> "Classify the attached image. Return only a JSON object exactly matching {\"labels\":[...]}, " +
			"with labels in descending relevance order. Use each label at most once and use only labels from this allowlist: " +
			JsonSerializer.Serialize(_labels, ChatClientImageClassificationJsonContext.Default.StringArray);
}

internal sealed class ChatClientImageClassificationResponse
{
	public required string[] Labels { get; init; }
}

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ChatClientImageClassificationResponse))]
[JsonSerializable(typeof(string[]))]
internal partial class ChatClientImageClassificationJsonContext : JsonSerializerContext
{
}
