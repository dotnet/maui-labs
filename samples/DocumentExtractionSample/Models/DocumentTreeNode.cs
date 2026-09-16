using Microsoft.Maui.Essentials.AI;

namespace DocumentExtractionSample.Models;

/// <summary>A single flattened row in the recursive document/element tree shown in the results list.</summary>
public sealed class DocumentTreeNode
{
	/// <summary>Gets the indentation depth of this node (0 = page root).</summary>
	public required int Depth { get; init; }

	/// <summary>Gets the primary display text for this node (element kind/summary).</summary>
	public required string Title { get; init; }

	/// <summary>Gets secondary display text for this node (content preview), if any.</summary>
	public string? Subtitle { get; init; }

	/// <summary>Gets the Apple Vision node reference backing this row, when one is available.</summary>
	public AppleVisionDocumentNodeReference? RawReference { get; init; }

	/// <summary>Gets a value indicating whether <see cref="Subtitle"/> has visible content.</summary>
	public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

	/// <summary>Gets a value indicating whether raw Apple JSON can be viewed for this node.</summary>
	public bool HasRawJson => RawReference is not null;

	/// <summary>Gets the left margin used to visually indent this node in the results list.</summary>
	public Thickness Indent => new(Depth * 16, 2, 2, 2);
}
