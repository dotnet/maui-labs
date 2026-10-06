using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionContractTests
{
	[Fact]
	public void Contract_RequiredInventory_IsInternalAndProviderIndependent()
	{
		var types = typeof(DocumentPage).Assembly.GetTypes()
			.Where(type => type.Namespace == typeof(DocumentPage).Namespace && type.DeclaringType is null).ToArray();
		Assert.Equal(new[]
		{
			"DocumentBlock", "DocumentBlockKind", "DocumentBoundingBox", "DocumentBoundingRegion",
			"DocumentCoordinateOrigin", "DocumentCoordinateUnit", "DocumentElement",
			"DocumentExtractionClientMetadata", "DocumentExtractionOptions", "DocumentExtractionPageResult",
			"DocumentExtractionResult", "DocumentExtractionUsage", "DocumentPage", "DocumentPageDimensions",
			"DocumentPoint", "DocumentTable", "DocumentTableCell", "DocumentTableCellKind", "IDocumentExtractionClient",
		}, types.Select(type => type.Name).Order(StringComparer.Ordinal));
		Assert.All(types, type => Assert.True(type.IsNotPublic));
		Assert.DoesNotContain(types, type => type.Name.Contains("Apple", StringComparison.Ordinal));
		Assert.False(typeof(DocumentElement).IsAssignableFrom(typeof(DocumentTableCell)));
		Assert.Equal(["Pixel", "Point", "Inch", "Normalized"], Enum.GetNames<DocumentCoordinateUnit>());
		Assert.Equal(["TopLeft", "BottomLeft"], Enum.GetNames<DocumentCoordinateOrigin>());
	}

	[Theory]
	[InlineData(typeof(DocumentExtractionOptions), "AdditionalProperties,ModelId")]
	[InlineData(typeof(DocumentExtractionClientMetadata), "DefaultModelId,ProviderName,ProviderUri")]
	[InlineData(typeof(DocumentExtractionResult), "AdditionalProperties,Pages,RawRepresentation,Text,Usage")]
	[InlineData(typeof(DocumentExtractionPageResult), "AdditionalProperties,Page,PagesProcessed,RawRepresentation,TotalPages,Usage")]
	[InlineData(typeof(DocumentExtractionUsage), "AdditionalProperties,InputTokenCount,OutputTokenCount,PagesProcessed,TotalTokenCount")]
	[InlineData(typeof(DocumentPage), "AdditionalProperties,CoordinateOrigin,CoordinateUnit,Dimensions,Elements,PageNumber,RawRepresentation,Text")]
	[InlineData(typeof(DocumentElement), "AdditionalProperties,BoundingRegion,Confidence,RawRepresentation")]
	[InlineData(typeof(DocumentBlock), "AdditionalProperties,BoundingRegion,Confidence,Kind,RawRepresentation,Text")]
	[InlineData(typeof(DocumentTable), "AdditionalProperties,BoundingRegion,Cells,ColumnCount,Confidence,MarkdownRepresentation,RawRepresentation,RowCount")]
	[InlineData(typeof(DocumentTableCell), "AdditionalProperties,BoundingRegion,ColumnIndex,ColumnSpan,Confidence,Content,Elements,Kind,RawRepresentation,RowIndex,RowSpan")]
	[InlineData(typeof(DocumentBoundingRegion), "PageNumber,Polygon")]
	[InlineData(typeof(DocumentPageDimensions), "Height,Width")]
	[InlineData(typeof(DocumentPoint), "X,Y")]
	[InlineData(typeof(DocumentBoundingBox), "Bottom,Left,Right,Top")]
	[InlineData(typeof(DocumentBlockKind), "Figure,Paragraph,Title,Value")]
	[InlineData(typeof(DocumentTableCellKind), "ColumnHeader,Content,RowHeader,RowSection,Value")]
	public void Contract_Properties_MatchPinnedMembers(Type type, string properties)
	{
		Assert.Equal(properties.Split(','), type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
			.Select(property => property.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Constructors_RequiredReferences_AreGuardedAndIdentityPreserved()
	{
		Assert.Throws<ArgumentNullException>(() => new DocumentExtractionResult(null!));
		Assert.Throws<ArgumentNullException>(() => new DocumentExtractionPageResult(null!));
		Assert.Throws<ArgumentNullException>(() => new DocumentPage(1, null!));
		Assert.Throws<ArgumentNullException>(() => new DocumentBlock(null!));
		Assert.Throws<ArgumentNullException>(() => new DocumentTableCell(0, 0, null!));
		Assert.Throws<ArgumentNullException>(() => new DocumentBoundingRegion(1, null!));
		var page = new DocumentPage(3, "Text");
		DocumentPage[] pages = [page];
		var result = new DocumentExtractionResult(pages);
		Assert.Same(pages, result.Pages);
		Assert.Same(page, new DocumentExtractionPageResult(page).Page);
		Assert.Null(page.AdditionalProperties);
		Assert.Null(page.Dimensions);
		Assert.Null(page.CoordinateOrigin);
		Assert.Null(page.CoordinateUnit);
		Assert.Empty(page.Elements);
		Assert.Null(result.Usage);
		Assert.Null(new DocumentBlock("").Kind);
		Assert.Null(new DocumentTable(1, 2).Cells);
		foreach (var (type, property) in new (Type, string)[]
		{
			(typeof(DocumentPage), "Text"), (typeof(DocumentPage), "PageNumber"),
			(typeof(DocumentExtractionResult), "Pages"), (typeof(DocumentExtractionPageResult), "Page"),
			(typeof(DocumentBlock), "Text"), (typeof(DocumentTableCell), "Content"),
			(typeof(DocumentTableCell), "RowIndex"), (typeof(DocumentTableCell), "ColumnIndex"),
			(typeof(DocumentTable), "RowCount"), (typeof(DocumentTable), "ColumnCount"),
			(typeof(DocumentTable), "Cells"), (typeof(DocumentTable), "MarkdownRepresentation"),
			(typeof(DocumentBoundingRegion), "Polygon"), (typeof(DocumentBoundingRegion), "PageNumber"),
			(typeof(DocumentExtractionClientMetadata), "ProviderName"),
		})
			Assert.False(type.GetProperty(property)!.CanWrite);
	}

	[Fact]
	public void Options_Clone_CopiesDictionaryButKeepsValuesShallow()
	{
		var words = new[] { "Comet" };
		var nested = new Dictionary<string, string> { ["key"] = "value" };
		var options = new DocumentExtractionOptions
		{
			ModelId = "model",
			AdditionalProperties = new() { ["words"] = words, ["nested"] = nested },
		};
		var clone = options.Clone();
		Assert.NotSame(options, clone);
		Assert.Equal("model", clone.ModelId);
		Assert.NotSame(options.AdditionalProperties, clone.AdditionalProperties);
		Assert.Same(words, clone.AdditionalProperties!["words"]);
		Assert.Same(nested, clone.AdditionalProperties["nested"]);
		clone.AdditionalProperties["new"] = 42;
		Assert.False(options.AdditionalProperties.ContainsKey("new"));
		words[0] = "Changed";
		Assert.Equal("Changed", Assert.IsType<string[]>(clone.AdditionalProperties["words"])[0]);
		Assert.Null(new DocumentExtractionOptions().Clone().AdditionalProperties);
	}

	[Fact]
	public void OpenKinds_CaseInsensitiveEqualityAndStringJson_RoundTrip()
	{
		var first = new DocumentBlockKind("listItem");
		var second = new DocumentBlockKind("LISTITEM");
		Assert.True(first == second);
		Assert.False(first != second);
		Assert.True(first.Equals((object)second));
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
		Assert.Equal("listItem", first.Value);
		Assert.Equal("listItem", first.ToString());
		Assert.Equal("\"listItem\"", JsonSerializer.Serialize(first));
		Assert.Equal(first, JsonSerializer.Deserialize<DocumentBlockKind>("\"LISTITEM\""));
		var cellKind = new DocumentTableCellKind("providerKind");
		var other = new DocumentTableCellKind("PROVIDERKIND");
		Assert.True(cellKind == other);
		Assert.False(cellKind != other);
		Assert.True(cellKind.Equals((object)other));
		Assert.Equal(cellKind.GetHashCode(), other.GetHashCode());
		Assert.Equal("providerKind", cellKind.Value);
		Assert.Equal("providerKind", cellKind.ToString());
		Assert.Equal("\"providerKind\"", JsonSerializer.Serialize(cellKind));
		Assert.Equal(cellKind, JsonSerializer.Deserialize<DocumentTableCellKind>("\"PROVIDERKIND\""));
		foreach (var invalid in new string?[] { null, "", " " })
		{
			Assert.ThrowsAny<ArgumentException>(() => new DocumentBlockKind(invalid!));
			Assert.ThrowsAny<ArgumentException>(() => new DocumentTableCellKind(invalid!));
		}
	}

	[Fact]
	public void Geometry_FloatRecordsAndRectangleBounds_MatchContract()
	{
		var region = DocumentBoundingRegion.FromRectangle(7, 0.1f, 0.2f, 0.8f, 0.9f);
		Assert.Equal(7, region.PageNumber);
		Assert.Equal(new DocumentBoundingBox(0.1f, 0.2f, 0.8f, 0.9f), region.GetBounds());
		Assert.Equal([new(0.1f, 0.2f), new(0.8f, 0.2f), new(0.8f, 0.9f), new DocumentPoint(0.1f, 0.9f)], region.Polygon);
		Assert.Null(new DocumentBoundingRegion(1, []).GetBounds());
		Assert.Equal(new DocumentPageDimensions(2, 3), new DocumentPageDimensions(2, 3));
		Assert.Equal(typeof(float), typeof(DocumentPoint).GetProperty("X")!.PropertyType);
	}

	[Fact]
	public void TableCell_StandaloneContentNestedElementsAndSpans_AreIndependent()
	{
		var block = new DocumentBlock("Nested");
		var cell = new DocumentTableCell(2, 3, "Own content");
		Assert.Equal(1, cell.RowSpan);
		Assert.Equal(1, cell.ColumnSpan);
		Assert.Null(cell.Elements);
		Assert.Null(cell.Kind);
		cell.Elements = [block];
		cell.RowSpan = 2;
		cell.ColumnSpan = 3;
		var table = new DocumentTable(4, 6, [cell], "| native markdown |");
		Assert.Equal("Own content", Assert.Single(table.Cells!).Content);
		Assert.Same(block, Assert.Single(cell.Elements));
		Assert.Equal("| native markdown |", table.MarkdownRepresentation);
		Assert.Equal(2, cell.RowSpan);
		Assert.Equal(3, cell.ColumnSpan);
	}

	[Fact]
	public void PageProgress_IsDirectAndIndependentFromUsage()
	{
		var result = new DocumentExtractionPageResult(new DocumentPage(2, ""))
		{
			PagesProcessed = 2, TotalPages = 5,
			Usage = new() { PagesProcessed = 1, InputTokenCount = 4, OutputTokenCount = 6, TotalTokenCount = 10 },
		};
		Assert.Equal(2, result.PagesProcessed);
		Assert.Equal(5, result.TotalPages);
		Assert.Equal(1, result.Usage.PagesProcessed);
		Assert.Equal(4, result.Usage.InputTokenCount);
		Assert.Equal(6, result.Usage.OutputTokenCount);
		Assert.Equal(10, result.Usage.TotalTokenCount);
	}

	[Fact]
	public void Serialization_NormalizedElementsAndRawIgnore_MatchContract()
	{
		var page = new DocumentPage(1, "text")
		{
			RawRepresentation = new object(),
			Elements = [new DocumentBlock("text") { Kind = DocumentBlockKind.Paragraph, RawRepresentation = new object() },
				new DocumentTable(1, 1, [new(0, 0, "cell") { RawRepresentation = new object() }])],
		};
		var result = new DocumentExtractionPageResult(page) { RawRepresentation = new object(), PagesProcessed = 1, TotalPages = 2 };
		var json = JsonSerializer.Serialize(result);
		Assert.DoesNotContain("RawRepresentation", json);
		Assert.Contains("\"$type\":\"block\"", json);
		Assert.Contains("\"$type\":\"table\"", json);
		var restored = JsonSerializer.Deserialize<DocumentExtractionPageResult>(json)!;
		Assert.Equal(2, restored.TotalPages);
		Assert.Equal("text", Assert.IsType<DocumentBlock>(restored.Page.Elements[0]).Text);
		Assert.Equal("cell", Assert.Single(Assert.IsType<DocumentTable>(restored.Page.Elements[1]).Cells!).Content);
	}
}
