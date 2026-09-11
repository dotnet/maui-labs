using System.Runtime.Versioning;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Represents a barcode returned by Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleBarcodeElement : DocumentElement
{
	/// <summary>Initializes a new barcode element.</summary>
	/// <param name="symbology">The Apple Vision barcode symbology.</param>
	public AppleBarcodeElement(string symbology)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbology);
		Symbology = symbology;
	}

	/// <summary>Gets the Apple Vision barcode symbology.</summary>
	public string Symbology { get; }

	/// <summary>Gets or sets the decoded text payload.</summary>
	public string? PayloadString { get; set; }

	/// <summary>Gets or sets the decoded binary payload.</summary>
	public ReadOnlyMemory<byte>? PayloadData { get; set; }

	/// <summary>Gets or sets whether the barcode is a GS1 data carrier.</summary>
	public bool? IsGs1DataCarrier { get; set; }

	/// <summary>Gets or sets whether the barcode colors are inverted.</summary>
	public bool? IsColorInverted { get; set; }

	/// <summary>Gets or sets the supplemental text payload.</summary>
	public string? SupplementalPayloadString { get; set; }

	/// <summary>Gets or sets the supplemental binary payload.</summary>
	public ReadOnlyMemory<byte>? SupplementalPayloadData { get; set; }

	/// <summary>Gets or sets the supplemental composite barcode type.</summary>
	public string? SupplementalCompositeType { get; set; }
}

/// <summary>Represents a structured list returned by Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleListElement : DocumentElement
{
	/// <summary>Initializes a new list element.</summary>
	/// <param name="items">The ordered list items.</param>
	public AppleListElement(IReadOnlyList<AppleListItemElement> items)
	{
		ArgumentNullException.ThrowIfNull(items);
		Items = items;
	}

	/// <summary>Gets the ordered list items.</summary>
	public IReadOnlyList<AppleListItemElement> Items { get; }
}

/// <summary>Represents one item in an Apple Vision document list.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleListItemElement : DocumentElement
{
	/// <summary>Initializes a new list item.</summary>
	/// <param name="text">The flattened text content of the item.</param>
	public AppleListItemElement(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		Text = text;
	}

	/// <summary>Gets the flattened text content.</summary>
	public string Text { get; }

	/// <summary>Gets or sets the item string reported by Apple Vision.</summary>
	public string? ItemString { get; set; }

	/// <summary>Gets or sets the rendered list marker.</summary>
	public string? MarkerString { get; set; }

	/// <summary>Gets or sets the Apple Vision marker type.</summary>
	public string? MarkerType { get; set; }

	/// <summary>Gets or sets structured content nested inside this item.</summary>
	public IReadOnlyList<DocumentElement> Elements { get; set; } = [];
}
