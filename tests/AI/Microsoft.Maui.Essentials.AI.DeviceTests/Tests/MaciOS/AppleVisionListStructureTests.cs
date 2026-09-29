#if IOS || MACCATALYST

using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public sealed class AppleVisionListStructureTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData("flat-list.png", "Apples")]
	[InlineData("single-item-list.png", "Signed application")]
	[InlineData("nested-list.png", "Passport")]
	[InlineData("list-in-table.png", "Passport copy")]
	public async Task ExtractAsync_ListFixture_ReportsVisionContainerShape(
		string assetName,
		string expectedText)
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}

		var page = await AppleVisionDocumentCorpus.ExtractAsync(assetName);
		Assert.Contains(expectedText, page.Text, StringComparison.OrdinalIgnoreCase);
		var nodes = AppleVisionDocumentCorpus.GetRawNodes(page);
		var listNodes = nodes
			.Where(static node =>
				node.GetProperty("kind").GetString() is "list" or "listItem")
			.Select(static node => new
			{
				Kind = node.GetProperty("kind").GetString(),
				Path = node.GetProperty("path").GetString(),
				ParentPath = AppleVisionDocumentCorpus.GetOptionalString(node, "parentPath"),
				Text = AppleVisionDocumentCorpus.GetOptionalString(node, "text"),
				ItemString = AppleVisionDocumentCorpus.GetOptionalString(node, "itemString"),
				MarkerString = AppleVisionDocumentCorpus.GetOptionalString(node, "markerString"),
				MarkerType = AppleVisionDocumentCorpus.GetOptionalString(node, "markerType"),
				Polygon = node.GetProperty("polygon")
					.EnumerateArray()
					.Select(static value => value.GetDouble())
					.ToArray(),
			})
			.ToArray();
		var normalizedListItems = AppleVisionDocumentCorpus
			.EnumerateElements(page.Elements)
			.OfType<DocumentBlock>()
			.Where(static block => block.Kind?.Value == "listItem")
			.ToArray();

		switch (assetName)
		{
			case "flat-list.png":
				Assert.Equal(
					["Apples", "Coffee", "Bread"],
					normalizedListItems
						.Select(static item => GetProperty(item, "apple.vision.itemString"))
						.ToArray());
				Assert.True(AppleVisionDocumentCorpus.GetLongProperty(
					page,
					"apple.vision.repeatedContainersPruned") > 0);
				break;
			case "nested-list.png":
				Assert.Equal(
					["Documents", "Passport", "Visa", "Packing", "Jacket", "Shoes", "Charger"],
					normalizedListItems
						.Select(static item => GetProperty(item, "apple.vision.itemString"))
						.ToArray());
				Assert.Equal(
					["bullet", "hyphen", "hyphen", "bullet", "hyphen", "hyphen", "hyphen"],
					normalizedListItems
						.Select(static item => GetProperty(item, "apple.vision.markerType"))
						.ToArray());
				Assert.True(AppleVisionDocumentCorpus.GetLongProperty(
					page,
					"apple.vision.repeatedContainersPruned") > 0);
				break;
			case "single-item-list.png":
				Assert.Empty(normalizedListItems);
				Assert.Contains(page.Elements.OfType<DocumentBlock>(), static block =>
					block.Text.Contains(
						"Signed application form",
						StringComparison.OrdinalIgnoreCase));
				break;
			case "list-in-table.png":
				Assert.Empty(normalizedListItems);
				var table = Assert.Single(page.Elements.OfType<DocumentTable>());
				Assert.Contains(table.Cells!, static cell =>
					cell.Content.Contains("Passport copy", StringComparison.OrdinalIgnoreCase));
				break;
		}

		var normalizedLists = normalizedListItems
			.Select(static item => new
			{
				Path = GetProperty(item, "apple.vision.sourcePath"),
				MarkerString = GetProperty(item, "apple.vision.markerString"),
				MarkerType = GetProperty(item, "apple.vision.markerType"),
				ItemString = GetProperty(item, "apple.vision.itemString"),
			})
			.ToArray();
		var diagnostics = new
		{
			Asset = assetName,
			Transcript = page.Text,
			ProjectedNodeCount = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.projectedNodeCount"),
			MaximumTraversalDepth = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.maximumTraversalDepth"),
			RepeatedContainersPruned = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.repeatedContainersPruned"),
			RepeatedContainerExamples = AppleVisionDocumentCorpus.GetStringArrayProperty(
				page,
				"apple.vision.repeatedContainerExamples"),
			RawLists = listNodes,
			RawItemContentNodes = nodes
				.Where(static node =>
					node.GetProperty("path").GetString()?.Contains(
						"/items/",
						StringComparison.Ordinal) == true &&
					node.GetProperty("path").GetString()?.Contains(
						"/content/",
						StringComparison.Ordinal) == true)
				.Select(static node => new
				{
					Kind = node.GetProperty("kind").GetString(),
					Path = node.GetProperty("path").GetString(),
					Text = AppleVisionDocumentCorpus.GetOptionalString(node, "text"),
					Polygon = node.GetProperty("polygon")
						.EnumerateArray()
						.Select(static value => value.GetDouble())
						.ToArray(),
				})
				.ToArray(),
			NormalizedLists = normalizedLists,
		};

		output.WriteLine(JsonSerializer.Serialize(
			diagnostics,
			new JsonSerializerOptions { WriteIndented = true }));
	}

	private static string GetProperty(DocumentElement element, string key) =>
		element.AdditionalProperties?.TryGetValue(key, out var value) == true &&
		value is string text
			? text
			: string.Empty;
}

#endif
