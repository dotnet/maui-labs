using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Creates JSON options that support Apple-specific document elements.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public static class AppleDocumentExtractionJson
{
	/// <summary>Gets read-only serializer options for normalized and Apple-specific document extraction results.</summary>
	public static JsonSerializerOptions Default { get; } = CreateDefaultOptions();

	/// <summary>Creates serializer options for normalized and Apple-specific document extraction results.</summary>
	public static JsonSerializerOptions CreateOptions() => new(Default);

	private static JsonSerializerOptions CreateDefaultOptions()
	{
		var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
		options.TypeInfoResolverChain.Insert(
			0,
			AppleDocumentExtractionJsonContext.Default.WithAddedModifier(
				static typeInfo =>
				{
					if (typeInfo.Type == typeof(DocumentElement))
					{
						typeInfo.PolymorphismOptions = null;
					}
				}));
		options.Converters.Insert(0, new AppleDocumentElementConverter());
		options.MakeReadOnly();
		return options;
	}
}

internal sealed class AppleDocumentElementConverter : JsonConverter<DocumentElement>
{
	public override DocumentElement? Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		if (!document.RootElement.TryGetProperty("$type", out var discriminatorElement))
		{
			throw new JsonException("Document elements require a '$type' discriminator.");
		}

		var type = discriminatorElement.GetString() switch
		{
			"block" => typeof(DocumentBlock),
			"table" => typeof(DocumentTable),
			"image" => typeof(DocumentImage),
			"apple.barcode" => typeof(AppleBarcodeElement),
			"apple.list" => typeof(AppleListElement),
			"apple.listItem" => typeof(AppleListItemElement),
			var discriminator => throw new JsonException(
				$"Unsupported document element discriminator '{discriminator}'."),
		};

		return (DocumentElement?)JsonSerializer.Deserialize(
			document.RootElement.GetRawText(),
			options.GetTypeInfo(type));
	}

	public override void Write(
		Utf8JsonWriter writer,
		DocumentElement value,
		JsonSerializerOptions options)
	{
		var (type, discriminator) = value switch
		{
			DocumentBlock => (typeof(DocumentBlock), "block"),
			DocumentTable => (typeof(DocumentTable), "table"),
			DocumentImage => (typeof(DocumentImage), "image"),
			AppleBarcodeElement => (typeof(AppleBarcodeElement), "apple.barcode"),
			AppleListElement => (typeof(AppleListElement), "apple.list"),
			AppleListItemElement => (typeof(AppleListItemElement), "apple.listItem"),
			_ => throw new JsonException(
				$"Unsupported document element type '{value.GetType().FullName}'."),
		};
		var serialized = JsonSerializer.SerializeToElement(
			value,
			options.GetTypeInfo(type));

		writer.WriteStartObject();
		writer.WriteString("$type", discriminator);
		foreach (var property in serialized.EnumerateObject())
		{
			if (!property.NameEquals("$type"))
			{
				property.WriteTo(writer);
			}
		}
		writer.WriteEndObject();
	}
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DocumentExtractionResult))]
[JsonSerializable(typeof(DocumentExtractionPageResult))]
[JsonSerializable(typeof(DocumentPage))]
[JsonSerializable(typeof(DocumentBlock))]
[JsonSerializable(typeof(DocumentTable))]
[JsonSerializable(typeof(DocumentTableCell))]
[JsonSerializable(typeof(DocumentImage))]
[JsonSerializable(typeof(AppleBarcodeElement))]
[JsonSerializable(typeof(AppleListElement))]
[JsonSerializable(typeof(AppleListItemElement))]
[JsonSerializable(typeof(AdditionalPropertiesDictionary))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(double[]))]
internal sealed partial class AppleDocumentExtractionJsonContext : JsonSerializerContext;
