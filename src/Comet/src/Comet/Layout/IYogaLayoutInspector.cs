// Public inspection surface for Comet's Yoga-backed layout managers.
// Exposes the cached YogaNode root + per-child frame and key flex properties
// as plain CLR types (strings instead of Yoga enums) so external consumers —
// notably the DevFlow agent — can serialize a layout snapshot without taking
// any dependency on Comet.Layout.Yoga internals.

using System;
using System.Collections.Generic;

namespace Comet.Layout;

/// <summary>
/// Implemented by Comet layout managers that own a cached Yoga root.
/// Returns a snapshot of the most recently computed layout, or null if no
/// measure pass has run yet.
/// </summary>
public interface IYogaLayoutInspector
{
	/// <summary>
	/// Returns a snapshot of the cached Yoga layout, or null if the manager
	/// has not yet run a measure pass.
	/// </summary>
	YogaLayoutSnapshot? GetLayoutSnapshot();
}

/// <summary>
/// Snapshot of a Comet Yoga-managed layout's root frame and flex configuration,
/// plus per-child frames and key flex properties.
/// </summary>
public sealed class YogaLayoutSnapshot
{
	public (float X, float Y, float Width, float Height) Frame { get; init; }
	public string FlexDirection { get; init; } = "";
	public string AlignItems { get; init; } = "";
	public string JustifyContent { get; init; } = "";
	public string AlignContent { get; init; } = "";
	public string FlexWrap { get; init; } = "";
	public IReadOnlyList<YogaLayoutChildSnapshot> Children { get; init; } = Array.Empty<YogaLayoutChildSnapshot>();
}

/// <summary>
/// Per-child snapshot: view type name, optional automation id, computed frame
/// (from YogaNode.LayoutX/Y/Width/Height), and the flex properties that
/// influenced placement.
/// </summary>
public sealed class YogaLayoutChildSnapshot
{
	public string ViewTypeName { get; init; } = "";
	public string? AutomationId { get; init; }
	public (float X, float Y, float Width, float Height) Frame { get; init; }
	public float FlexGrow { get; init; }
	public float FlexShrink { get; init; }
	public string AlignSelf { get; init; } = "";
	public string PositionType { get; init; } = "";
}
