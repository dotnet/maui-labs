// Yoga-backed FlexLayout manager. Replaces the hand-rolled FlexLayoutManager with
// a thin adapter over Comet.Layout.Yoga. The layout object owns a root YogaNode
// that's rebuilt whenever the child list changes.

using System.Collections.Generic;
using System.Linq;
using Comet.Layout.Yoga;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

using YogaNode = Comet.Layout.Yoga.YogaNode;
using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;

namespace Comet.Layout
{
	/// <summary>
	/// ILayoutManager that delegates to Comet's Yoga port. Builds a root YogaNode on
	/// demand, inserts a leaf for each child via <see cref="YogaMeasureBridge"/>, then
	/// uses Yoga's computed positions to arrange children. Cached between Measure and
	/// ArrangeChildren; invalidated when the child count or identity changes.
	/// </summary>
	public class YogaFlexLayoutManager : ILayoutManager, IYogaLayoutInspector
	{
		readonly FlexLayout _layout;

		YogaNode? _root;
		List<IView>? _orderedChildren;
		int _lastChildCount = -1;

		public YogaFlexLayoutManager(FlexLayout layout)
		{
			_layout = layout;
		}

		public Size Measure(double widthConstraint, double heightConstraint)
		{
			var root = BuildOrReuseRoot();
			var w = SanitizeConstraint(widthConstraint);
			var h = SanitizeConstraint(heightConstraint);
			root.CalculateLayout(w, h, FlexLayoutDirection.LTR);
			return new Size(root.LayoutWidth, root.LayoutHeight);
		}

		public Size ArrangeChildren(Rect bounds)
		{
			var root = BuildOrReuseRoot();
			root.CalculateLayout((float)bounds.Width, (float)bounds.Height, FlexLayoutDirection.LTR);

			if (_orderedChildren is null)
				return bounds.Size;

			for (int i = 0; i < _orderedChildren.Count; i++)
			{
				var child = _orderedChildren[i];
				var childNode = root.GetChild(i);
				YogaMeasureBridge.ArrangeFromNode(child, childNode, bounds.Location);
			}

			return new Size(root.LayoutWidth, root.LayoutHeight);
		}

		YogaNode BuildOrReuseRoot()
		{
			// Snapshot current ordered children (respecting FlexOrder).
			var ordered = ((IEnumerable<View>)_layout)
				.Select((v, idx) => (view: (IView)v, order: v.GetFlexOrder(), idx))
				.OrderBy(x => x.order)
				.ThenBy(x => x.idx)
				.Select(x => x.view)
				.ToList();

			bool needsRebuild = _root is null
				|| _orderedChildren is null
				|| _orderedChildren.Count != ordered.Count
				|| _lastChildCount != ordered.Count;

			if (!needsRebuild && _orderedChildren is not null)
			{
				for (int i = 0; i < ordered.Count; i++)
				{
					if (!ReferenceEquals(_orderedChildren[i], ordered[i]))
					{
						needsRebuild = true;
						break;
					}
				}
			}

			if (needsRebuild)
			{
				_root = new YogaNode();
				_orderedChildren = ordered;
				_lastChildCount = ordered.Count;

				ApplyRootStyle(_root);

				for (int i = 0; i < ordered.Count; i++)
				{
					var child = ordered[i];
					var childNode = YogaMeasureBridge.CreateLeafNode(child, _layout.Direction);
					_root.InsertChild(childNode, i);
				}
			}
			else
			{
				// Refresh root style + per-child style so that env/property changes take effect
				// without losing the MeasureFunc-bearing child nodes.
				ApplyRootStyle(_root!);
				for (int i = 0; i < ordered.Count; i++)
				{
					YogaMeasureBridge.ApplyStyle(_root!.GetChild(i), ordered[i], _layout.Direction);
				}
			}

			return _root!;
		}

		void ApplyRootStyle(YogaNode root)
		{
			root.FlexDirection = _layout.Direction;
			root.FlexWrap = _layout.Wrap;
			root.JustifyContent = _layout.JustifyContent;
			root.AlignItems = _layout.AlignItems;
			root.AlignContent = _layout.AlignContent;

			// Padding from the container view.
			var padding = _layout.GetPadding();
			root.SetPadding(YogaEdge.Left, YogaValue.Point((float)padding.Left));
			root.SetPadding(YogaEdge.Top, YogaValue.Point((float)padding.Top));
			root.SetPadding(YogaEdge.Right, YogaValue.Point((float)padding.Right));
			root.SetPadding(YogaEdge.Bottom, YogaValue.Point((float)padding.Bottom));

			// Gap: default applies to all; row/column can override.
			if (_layout.Gap >= 0)
				root.SetGap(YogaGutter.All, (float)_layout.Gap);
			if (_layout.RowGap >= 0)
				root.SetGap(YogaGutter.Row, (float)_layout.RowGap);
			if (_layout.ColumnGap >= 0)
				root.SetGap(YogaGutter.Column, (float)_layout.ColumnGap);
		}

		static float SanitizeConstraint(double v)
		{
			if (double.IsNaN(v) || double.IsInfinity(v))
				return float.NaN; // Yoga treats NaN as "undefined"
			return (float)v;
		}

		// ── Inspector (IYogaLayoutInspector) ──

		public YogaLayoutSnapshot? GetLayoutSnapshot()
		{
			var root = _root;
			var ordered = _orderedChildren;
			if (root is null || ordered is null)
				return null;

			var children = new List<YogaLayoutChildSnapshot>(ordered.Count);
			for (int i = 0; i < ordered.Count; i++)
			{
				var child = ordered[i];
				var node = root.GetChild(i);
				var typeName = child.GetType().Name;
				string? automationId = (child as View)?.AutomationId;
				if (string.IsNullOrEmpty(automationId)) automationId = null;

				children.Add(new YogaLayoutChildSnapshot
				{
					ViewTypeName = typeName,
					AutomationId = automationId,
					Frame = (node.LayoutX, node.LayoutY, node.LayoutWidth, node.LayoutHeight),
					FlexGrow = node.FlexGrow,
					FlexShrink = node.FlexShrink,
					AlignSelf = node.AlignSelf.ToString(),
					PositionType = node.PositionType.ToString(),
				});
			}

			return new YogaLayoutSnapshot
			{
				Frame = (root.LayoutX, root.LayoutY, root.LayoutWidth, root.LayoutHeight),
				FlexDirection = root.FlexDirection.ToString(),
				AlignItems = root.AlignItems.ToString(),
				JustifyContent = root.JustifyContent.ToString(),
				AlignContent = root.AlignContent.ToString(),
				FlexWrap = root.FlexWrap.ToString(),
				Children = children,
			};
		}
	}
}
