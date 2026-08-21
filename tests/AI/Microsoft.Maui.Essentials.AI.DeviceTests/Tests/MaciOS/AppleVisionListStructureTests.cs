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
		var normalizedListElements = AppleVisionDocumentCorpus
			.EnumerateElements(page.Elements)
			.OfType<AppleListElement>()
			.ToArray();
		Assert.All(
			normalizedListElements.SelectMany(static list => list.Items),
			static item => Assert.Empty(item.Elements));

		switch (assetName)
		{
			case "flat-list.png":
				var flatList = Assert.Single(normalizedListElements);
				Assert.Equal(
					["Apples", "Coffee", "Bread"],
					flatList.Items
						.Select(static item => item.ItemString ?? string.Empty)
						.ToArray());
				Assert.True(AppleVisionDocumentCorpus.GetLongProperty(
					page,
					"apple.vision.repeatedContainersPruned") > 0);
				break;
			case "nested-list.png":
				var indentedList = Assert.Single(normalizedListElements);
				Assert.Equal(
					["Documents", "Passport", "Visa", "Packing", "Jacket", "Shoes", "Charger"],
					indentedList.Items
						.Select(static item => item.ItemString ?? string.Empty)
						.ToArray());
				Assert.Equal(
					["bullet", "hyphen", "hyphen", "bullet", "hyphen", "hyphen", "hyphen"],
					indentedList.Items
						.Select(static item => item.MarkerType ?? string.Empty)
						.ToArray());
				Assert.True(AppleVisionDocumentCorpus.GetLongProperty(
					page,
					"apple.vision.repeatedContainersPruned") > 0);
				break;
			case "single-item-list.png":
				Assert.Empty(normalizedListElements);
				Assert.Contains(page.Elements.OfType<DocumentBlock>(), static block =>
					block.Text.Contains(
						"Signed application form",
						StringComparison.OrdinalIgnoreCase));
				break;
			case "list-in-table.png":
				Assert.Empty(normalizedListElements);
				var table = Assert.Single(page.Elements.OfType<DocumentTable>());
				Assert.Contains(table.Cells!, static cell =>
					cell.Content.Contains("Passport copy", StringComparison.OrdinalIgnoreCase));
				break;
		}

		var normalizedLists = normalizedListElements
			.Select(static list => new
			{
				Path = (list.RawRepresentation as AppleVisionDocumentNodeReference)?.Path,
				ItemCount = list.Items.Count,
				Items = list.Items.Select(static item => new
				{
					item.MarkerString,
					item.MarkerType,
					item.ItemString,
					NestedListCount = AppleVisionDocumentCorpus.EnumerateElements(item.Elements)
						.OfType<AppleListElement>()
						.Count(),
				}).ToArray(),
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
}

#endif
