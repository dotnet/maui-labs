using System.Reflection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionContractTests
{
	[Fact]
	public void Contract_PublicInventory_IsLatestOriginalProposal()
	{
		var types = typeof(DocumentExtractionPage).Assembly.GetExportedTypes()
			.Where(type => type.Namespace == typeof(DocumentExtractionPage).Namespace && type.DeclaringType is null).ToArray();
		Assert.Equal(new[]
		{
			"DelegatingDocumentExtractionClient", "DocumentExtractionClientExtensions",
			"DocumentExtractionDocumentBuilder", "DocumentExtractionEvidence", "DocumentExtractionExtension",
			"DocumentExtractionImage", "DocumentExtractionObservation", "DocumentExtractionPage",
			"DocumentExtractionPageResultExtensions", "DocumentExtractionResult", "DocumentExtractionTable",
			"DocumentExtractionTableCell", "DocumentExtractionText", "IDocumentExtractionClient",
		}, types.Select(type => type.Name).Order(StringComparer.Ordinal));
		Assert.DoesNotContain(types, type => type.Name.Contains("Apple", StringComparison.Ordinal));
		Assert.Equal(["ExtractPagesAsync", "GetService"], typeof(IDocumentExtractionClient).GetMethods()
			.Select(method => method.Name).Order(StringComparer.Ordinal));
		Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IDocumentExtractionClient)));
		Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(DocumentExtractionResult)));
		Assert.NotEqual(typeof(Document).Assembly, typeof(DocumentExtractionPage).Assembly);
	}

	[Theory]
	[InlineData(typeof(DocumentExtractionResult), "Document,IsDisposed,Pages")]
	[InlineData(typeof(DocumentExtractionPage), "Markdown,Observations,PageNumber,PageReferences")]
	[InlineData(typeof(DocumentExtractionObservation), "Artifacts,Evidence,Markdown,Order,PageReferences,ProviderId")]
	[InlineData(typeof(DocumentExtractionText), "Artifacts,Evidence,HeaderLevel,Kind,Markdown,Order,PageReferences,ProviderId,Text")]
	[InlineData(typeof(DocumentExtractionTable), "Artifacts,Cells,ColumnCount,Evidence,Markdown,Order,PageReferences,ProviderId,RowCount")]
	[InlineData(typeof(DocumentExtractionTableCell), "Column,Content,Row")]
	public void Contract_Properties_MatchPinnedMembers(Type type, string properties)
	{
		Assert.Equal(properties.Split(','), type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
			.Select(property => property.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Constructors_GuardRequiredReferencesAndRetainSupportedSemantics()
	{
		Assert.Throws<ArgumentNullException>(() => new DocumentExtractionPage(1, null!));
		Assert.ThrowsAny<ArgumentException>(() => new DocumentExtractionText("", 0, DocumentTextKind.Paragraph, "Text"));
		Assert.Throws<ArgumentNullException>(() => new DocumentExtractionTableCell(0, 0, null!));
		Assert.ThrowsAny<ArgumentException>(() => new DocumentExtractionText("header", 0, DocumentTextKind.Header, "Heading",
			headerLevel: 11));
		var text = Text("text", 1, "Actual text");
		Assert.Empty(text.Evidence);
		Assert.Empty(text.Artifacts);
		Assert.Empty(text.PageReferences);
		Assert.Null(text.HeaderLevel);
		var page = new DocumentExtractionPage(3, [text], "Exact provider markdown");
		Assert.Equal("Exact provider markdown", page.Markdown);
		Assert.Equal(3, page.PageNumber);
		using var result = new[] { page }.ToDocumentExtractionResult("identity");
		Assert.Equal("identity", result.Document.Identifier);
		Assert.Same(page, Assert.Single(result.Pages));
		Assert.Equal("Actual text", Assert.Single(Assert.Single(result.Document.Sections).Elements).Text);
	}

	[Fact]
	public void Finalization_OrdersObservationsWithoutCollapsingEqualText()
	{
		var page = new DocumentExtractionPage(1, [Text("second", 2, "Same"), Text("first", 1, "Same")]);
		using var result = new[] { page }.ToDocumentExtractionResult("identity");
		var elements = Assert.Single(result.Document.Sections).Elements;
		Assert.Equal(2, elements.Count);
		Assert.NotEqual(elements[0].LocalId, elements[1].LocalId);
		Assert.Equal(["Same", "Same"], elements.Select(element => element.Text));
		using var distinct = new[] { new DocumentExtractionPage(1, [Text("second", 2, "Second"), Text("first", 1, "First")]) }
			.ToDocumentExtractionResult("ordered");
		Assert.Equal(["First", "Second"], Assert.Single(distinct.Document.Sections).Elements.Select(element => element.Text));
	}

	[Fact]
	public void Finalization_ProviderIdsAreUniqueAcrossPagesAndNestedCells()
	{
		Assert.ThrowsAny<ArgumentException>(() => new[]
		{
			new DocumentExtractionPage(1, [Text("duplicate", 0, "A")]),
			new DocumentExtractionPage(2, [Text("duplicate", 0, "B")]),
		}.ToDocumentExtractionResult("identity"));
		Assert.ThrowsAny<ArgumentException>(() => new[]
		{
			new DocumentExtractionPage(1, [Text("duplicate", 0, "A"),
				new DocumentExtractionTable("table", 1, "Table", 1, 1, [new(0, 0, Text("duplicate", 0, "Cell"))])]),
		}.ToDocumentExtractionResult("identity"));
	}

	[Fact]
	public void Finalization_SparseTableRetainsOnlyAnchoredContent()
	{
		var table = new DocumentExtractionTable("table", 0, "Display table", 2, 2, [new(0, 0, Text("cell", 0, "Merged"))]);
		using var result = new[] { new DocumentExtractionPage(1, [table]) }.ToDocumentExtractionResult("identity");
		var mapped = Assert.IsType<DocumentTableElement>(Assert.Single(Assert.Single(result.Document.Sections).Elements));
		Assert.Equal("Merged", mapped.Cells[0, 0]!.Content.Text);
		Assert.Null(mapped.Cells[0, 1]);
		Assert.Null(mapped.Cells[1, 0]);
		Assert.Null(mapped.Cells[1, 1]);
		Assert.False(mapped.HasMetadata);
	}

	[Fact]
	public void Result_DisposesCanonicalOwnerAndArtifactsExactlyOnce()
	{
		using var content = new MemoryStream([1, 2]);
		var artifact = new DocumentArtifact("image/png", content);
		var observation = new DocumentExtractionText("text", 0, DocumentTextKind.Paragraph, "Text", artifacts: [artifact]);
		var result = new[] { new DocumentExtractionPage(1, [observation]) }.ToDocumentExtractionResult("identity");
		Assert.Same(artifact, Assert.Single(result.Document.Artifacts));
		Assert.False(result.IsDisposed);
		result.Dispose();
		result.Dispose();
		Assert.True(result.IsDisposed);
		Assert.True(result.Document.IsDisposed);
		Assert.True(artifact.IsDisposed);
		Assert.False(content.CanRead);
	}

	[Fact]
	public void UnfinishedOrFailedBuilder_DisposesArtifacts()
	{
		foreach (var finalize in new[] { false, true })
		{
			var content = new MemoryStream([1]);
			var artifact = new DocumentArtifact("image/png", content);
			using (var builder = new DocumentExtractionDocumentBuilder("identity"))
			{
				builder.AddPage(new DocumentExtractionPage(1,
					[new DocumentExtractionText("same", 0, DocumentTextKind.Paragraph, "Text", artifacts: [artifact])]));
				if (finalize)
				{
					builder.AddPage(new DocumentExtractionPage(2, [Text("same", 0, "duplicate")]));
					Assert.ThrowsAny<ArgumentException>(() => builder.Build());
				}
			}
			Assert.True(artifact.IsDisposed);
			Assert.False(content.CanRead);
		}
	}

	private static DocumentExtractionText Text(string id, int order, string value) =>
		new(id, order, DocumentTextKind.Paragraph, value, value);
}
