#nullable enable
using Microsoft.Maui.Graphics;

namespace Comet.Backend;

/// <summary>
/// The native item's box, distinct from the template's content box. Its zero-spacing
/// layout parent includes the template root's margins in the item's measured extent.
/// The list retains logical ownership of the template; the row generation owns the
/// wrapper and content nodes, and the host disposes this wrapper after the generation.
/// </summary>
internal sealed class NativeListRow : VStack
{
	NativeListRow(View content) : base(spacing: 0)
	{
		Parent = content.Parent;
		Views.Add(content);
	}

	public bool IsArranged { get; private set; }

	public static NativeListRow Materialize(View content, OwnedContentGeneration generation)
	{
		var parent = content.Parent;
		var navigation = content.Navigation;
		var row = new NativeListRow(content);
		try
		{
			generation.Materialize(row);
			return row;
		}
		catch
		{
			row.Dispose();
			throw;
		}
		finally
		{
			// The bridge establishes the native child hierarchy, but ListView<T> uses
			// this logical parent to dispose cached templates and their subscriptions.
			content.Parent = parent;
			content.Navigation = navigation;
		}
	}

	public Size Layout(double width)
	{
		var size = CometBackendLayoutEngine.LayoutContent(this, width);
		IsArranged = true;
		return size;
	}

	/// <summary>Measures an existing item without creating wrappers, reparenting its
	/// content, allocating an owned generation, or pushing arranged frames.</summary>
	public Size MeasureExtent(double width)
		=> CometBackendLayoutEngine.MeasureContent(this, width);

	public Size MeasureIntrinsicExtent()
		=> CometBackendLayoutEngine.Measure(this);

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			Views.Clear();
		base.Dispose(disposing);
	}
}
