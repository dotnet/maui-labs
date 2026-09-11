#if IOS || MACCATALYST

using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

internal static class AppleVisionDocumentCorpus
{
	internal const string AssetRoot = "DocumentExtraction/";

	internal static async Task<DocumentPage> ExtractAsync(
		string fileName,
		DocumentExtractionOptions? options = null)
	{
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(
			$"{AssetRoot}{fileName}");
		var result = await client.ExtractAsync(stream, "image/png", options);
		return result.Pages.Count == 1
			? result.Pages[0]
			: throw new InvalidOperationException(
				$"Expected one page from corpus fixture '{fileName}', but received {result.Pages.Count}.");
	}

	internal static JsonElement GetRawObservation(DocumentPage page)
	{
		var raw = page.RawRepresentation as AppleVisionDocumentNodeReference
			?? throw new InvalidOperationException("The corpus page has no Apple Vision raw reference.");
		using var document = JsonDocument.Parse(raw.GetRawJson());
		var observations = document.RootElement.EnumerateArray().ToArray();
		return observations.Length == 1
			? observations[0].Clone()
			: throw new InvalidOperationException(
				$"Expected one Apple Vision observation, but received {observations.Length}.");
	}

	internal static JsonElement[] GetRawNodes(DocumentPage page) =>
		GetRawObservation(page)
			.GetProperty("nodes")
			.EnumerateArray()
			.Select(static node => node.Clone())
			.ToArray();

	internal static JsonElement[] GetDetectedData(DocumentPage page)
	{
		var matches = new List<JsonElement>();
		foreach (var element in EnumerateElements(page.Elements))
		{
			if (element.AdditionalProperties?.TryGetValue(
				"apple.detectedData",
				out var value) != true ||
				value is not string json)
			{
				continue;
			}

			using var document = JsonDocument.Parse(json);
			matches.AddRange(document.RootElement
				.EnumerateArray()
				.Select(static match => match.Clone()));
		}
		return matches.ToArray();
	}

	internal static IEnumerable<DocumentElement> EnumerateElements(
		IEnumerable<DocumentElement>? elements)
	{
		if (elements is null)
		{
			yield break;
		}

		foreach (var element in elements)
		{
			yield return element;
			switch (element)
			{
				case DocumentTable table:
					foreach (var nested in (table.Cells ?? []).SelectMany(static cell =>
						EnumerateElements(cell.Elements)))
					{
						yield return nested;
					}
					break;
				case AppleListElement list:
					foreach (var item in list.Items)
					{
						yield return item;
						foreach (var nested in EnumerateElements(item.Elements))
						{
							yield return nested;
						}
					}
					break;
				case AppleListItemElement item:
					foreach (var nested in EnumerateElements(item.Elements))
					{
						yield return nested;
					}
					break;
			}
		}
	}

	internal static long GetLongProperty(DocumentPage page, string key) =>
		page.AdditionalProperties?.TryGetValue(key, out var value) == true &&
		value is long number
			? number
			: 0;

	internal static string[] GetStringArrayProperty(DocumentPage page, string key) =>
		page.AdditionalProperties?.TryGetValue(key, out var value) == true &&
		value is string[] strings
			? strings
			: [];

	internal static string? GetOptionalString(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	internal static bool IsSupported() =>
		OperatingSystem.IsIOSVersionAtLeast(26) ||
		OperatingSystem.IsMacCatalystVersionAtLeast(26);
}

#endif
