// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

// Required subset adapted from dotnet/extensions#7588, pinned to:
// https://github.com/luisquintanilla/extensions/tree/a215825ae2c96723e922e068c226ff77122c7c94/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions
internal interface IDocumentExtractionClient : IDisposable
{
	Task<DocumentExtractionResult> ExtractAsync(Stream document, string mediaType,
		DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default);
	IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(Stream document, string mediaType,
		DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default);
	object? GetService(Type serviceType, object? serviceKey = null);
}

internal class DocumentExtractionOptions
{
	public string? ModelId { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
	public DocumentExtractionOptions Clone() => new()
	{
		ModelId = ModelId,
		AdditionalProperties = AdditionalProperties?.Clone(),
	};
}

internal class DocumentExtractionClientMetadata(
	string? providerName = null, Uri? providerUri = null, string? defaultModelId = null)
{
	public string? ProviderName { get; } = providerName;
	public Uri? ProviderUri { get; } = providerUri;
	public string? DefaultModelId { get; } = defaultModelId;
}

internal class DocumentExtractionResult
{
	public DocumentExtractionResult(IReadOnlyList<DocumentPage> pages)
	{
		Pages = pages ?? throw new ArgumentNullException(nameof(pages));
	}

	public IReadOnlyList<DocumentPage> Pages { get; }
	public string Text => string.Join("\n\n", Pages.Select(page => page.Text));
	public DocumentExtractionUsage? Usage { get; set; }
	public object? RawRepresentation { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

internal class DocumentExtractionUsage
{
	public int? PagesProcessed { get; set; }
	public int? InputTokenCount { get; set; }
	public int? OutputTokenCount { get; set; }
	public int? TotalTokenCount { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

internal class DocumentExtractionPageResult
{
	[JsonConstructor]
	public DocumentExtractionPageResult(DocumentPage page)
	{
		Page = page ?? throw new ArgumentNullException(nameof(page));
	}

	public DocumentPage Page { get; }
	public int? PagesProcessed { get; set; }
	public int? TotalPages { get; set; }
	public DocumentExtractionUsage? Usage { get; set; }
	[JsonIgnore]
	public object? RawRepresentation { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

internal class DocumentPage
{
	public DocumentPage(int pageNumber, string text)
	{
		PageNumber = pageNumber;
		Text = text ?? throw new ArgumentNullException(nameof(text));
	}

	public int PageNumber { get; }
	public string Text { get; }
	public IReadOnlyList<DocumentElement> Elements { get; set; } = [];
	public DocumentPageDimensions? Dimensions { get; set; }
	public DocumentCoordinateUnit? CoordinateUnit { get; set; }
	public DocumentCoordinateOrigin? CoordinateOrigin { get; set; }
	[JsonIgnore]
	public object? RawRepresentation { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

internal enum DocumentCoordinateUnit { Pixel, Point, Inch, Normalized }
internal enum DocumentCoordinateOrigin { TopLeft, BottomLeft }
internal readonly record struct DocumentPageDimensions(float Width, float Height);
internal readonly record struct DocumentPoint(float X, float Y);
internal readonly record struct DocumentBoundingBox(float Left, float Top, float Right, float Bottom);

internal class DocumentBoundingRegion
{
	public DocumentBoundingRegion(int pageNumber, IReadOnlyList<DocumentPoint> polygon)
	{
		PageNumber = pageNumber;
		Polygon = polygon ?? throw new ArgumentNullException(nameof(polygon));
	}

	public int PageNumber { get; }
	public IReadOnlyList<DocumentPoint> Polygon { get; }
	public static DocumentBoundingRegion FromRectangle(int pageNumber, float left, float top, float right, float bottom) =>
		new(pageNumber, [new(left, top), new(right, top), new(right, bottom), new(left, bottom)]);

	public DocumentBoundingBox? GetBounds()
	{
		if (Polygon.Count == 0)
			return null;
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		foreach (var point in Polygon)
		{
			minX = Math.Min(minX, point.X);
			maxX = Math.Max(maxX, point.X);
			minY = Math.Min(minY, point.Y);
			maxY = Math.Max(maxY, point.Y);
		}
		return new(minX, minY, maxX, maxY);
	}
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DocumentBlock), typeDiscriminator: "block")]
[JsonDerivedType(typeof(DocumentTable), typeDiscriminator: "table")]
internal abstract class DocumentElement
{
	protected DocumentElement() { }
	public DocumentBoundingRegion? BoundingRegion { get; set; }
	public double? Confidence { get; set; }
	[JsonIgnore]
	public object? RawRepresentation { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

internal class DocumentBlock : DocumentElement
{
	public DocumentBlock(string text)
	{
		Text = text ?? throw new ArgumentNullException(nameof(text));
	}

	public string Text { get; }
	public DocumentBlockKind? Kind { get; set; }
}

[JsonConverter(typeof(DocumentBlockKind.Converter))]
internal readonly struct DocumentBlockKind : IEquatable<DocumentBlockKind>
{
	public static DocumentBlockKind Paragraph { get; } = new("paragraph");
	public static DocumentBlockKind Title { get; } = new("title");
	public static DocumentBlockKind Figure { get; } = new("figure");
	public string Value { get; }
	[JsonConstructor]
	public DocumentBlockKind(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		Value = value;
	}

	public static bool operator ==(DocumentBlockKind left, DocumentBlockKind right) => left.Equals(right);
	public static bool operator !=(DocumentBlockKind left, DocumentBlockKind right) => !left.Equals(right);
	public override bool Equals(object? obj) => obj is DocumentBlockKind other && Equals(other);
	public bool Equals(DocumentBlockKind other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
	public override string ToString() => Value;

	internal sealed class Converter : JsonConverter<DocumentBlockKind>
	{
		public override DocumentBlockKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			new(reader.GetString()!);
		public override void Write(Utf8JsonWriter writer, DocumentBlockKind value, JsonSerializerOptions options)
		{
			ArgumentNullException.ThrowIfNull(writer);
			writer.WriteStringValue(value.Value);
		}
	}
}

internal class DocumentTable(int rowCount, int columnCount,
	IReadOnlyList<DocumentTableCell>? cells = null, string? markdownRepresentation = null) : DocumentElement
{
	public int RowCount { get; } = rowCount;
	public int ColumnCount { get; } = columnCount;
	public IReadOnlyList<DocumentTableCell>? Cells { get; } = cells;
	public string? MarkdownRepresentation { get; } = markdownRepresentation;
}

internal class DocumentTableCell
{
	public DocumentTableCell(int rowIndex, int columnIndex, string content)
	{
		RowIndex = rowIndex;
		ColumnIndex = columnIndex;
		Content = content ?? throw new ArgumentNullException(nameof(content));
	}

	public DocumentTableCellKind? Kind { get; set; }
	public int RowIndex { get; }
	public int ColumnIndex { get; }
	public int RowSpan { get; set; } = 1;
	public int ColumnSpan { get; set; } = 1;
	public string Content { get; }
	public IReadOnlyList<DocumentElement>? Elements { get; set; }
	public DocumentBoundingRegion? BoundingRegion { get; set; }
	public double? Confidence { get; set; }
	[JsonIgnore]
	public object? RawRepresentation { get; set; }
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

[JsonConverter(typeof(DocumentTableCellKind.Converter))]
internal readonly struct DocumentTableCellKind : IEquatable<DocumentTableCellKind>
{
	public static DocumentTableCellKind ColumnHeader { get; } = new("columnHeader");
	public static DocumentTableCellKind Content { get; } = new("content");
	public static DocumentTableCellKind RowHeader { get; } = new("rowHeader");
	public static DocumentTableCellKind RowSection { get; } = new("rowSection");
	public string Value { get; }
	[JsonConstructor]
	public DocumentTableCellKind(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		Value = value;
	}

	public static bool operator ==(DocumentTableCellKind left, DocumentTableCellKind right) => left.Equals(right);
	public static bool operator !=(DocumentTableCellKind left, DocumentTableCellKind right) => !left.Equals(right);
	public override bool Equals(object? obj) => obj is DocumentTableCellKind other && Equals(other);
	public bool Equals(DocumentTableCellKind other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
	public override string ToString() => Value;

	internal sealed class Converter : JsonConverter<DocumentTableCellKind>
	{
		public override DocumentTableCellKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			new(reader.GetString()!);
		public override void Write(Utf8JsonWriter writer, DocumentTableCellKind value, JsonSerializerOptions options)
		{
			ArgumentNullException.ThrowIfNull(writer);
			writer.WriteStringValue(value.Value);
		}
	}
}
