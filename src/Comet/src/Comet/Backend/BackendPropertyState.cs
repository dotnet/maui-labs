#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>
	/// Tracks the declarative property set currently applied to a retained backend node.
	/// A replacement declaration is captured first, removed properties are reset, then
	/// the replacement values are replayed so overlapping native state is deterministic.
	/// </summary>
	internal static class BackendPropertyState
	{
		static readonly ConditionalWeakTable<ICometBackendNode, TrackingNode> Nodes = new();

		public static void Apply(View view, ICometBackendNode node)
		{
			var tracker = Nodes.GetValue(node, static retained => new TrackingNode(retained));
			tracker.BeginCapture();
			try
			{
				view.ApplyAllSetProperties(tracker);
			}
			catch
			{
				tracker.CancelCapture();
				throw;
			}

			tracker.CommitCapture();
		}

		sealed class TrackingNode : ICometBackendNode
		{
			readonly ICometBackendNode _inner;
			readonly object _gate = new();
			readonly HashSet<PropertyId> _explicit = new();
			List<(PropertyId Id, PropertyValue Value)>? _capture;

			public TrackingNode(ICometBackendNode inner) => _inner = inner;

			public void BeginCapture()
			{
				lock (_gate)
				{
					if (_capture is not null)
						throw new InvalidOperationException("Backend property capture is already active.");
					_capture = new List<(PropertyId, PropertyValue)>();
				}
			}

			public void CancelCapture()
			{
				lock (_gate)
				{
					_capture = null;
				}
			}

			public void CommitCapture()
			{
				lock (_gate)
				{
					var captured = _capture ??
						throw new InvalidOperationException("Backend property capture is not active.");
					_capture = null;

					var current = new HashSet<PropertyId>();
					foreach (var (id, _) in captured)
						current.Add(id);

					foreach (var oldId in _explicit)
					{
						if (!current.Contains(oldId))
						{
							var reset = BackendPropertyDefaults.Get(oldId);
							_inner.ApplyProperty(oldId, in reset);
						}
					}

					foreach (var (id, value) in captured)
						_inner.ApplyProperty(id, in value);

					_explicit.Clear();
					foreach (var id in current)
						_explicit.Add(id);
				}
			}

			public void ApplyProperty(PropertyId id, in PropertyValue value)
			{
				lock (_gate)
				{
					if (_capture is { } capture)
					{
						capture.Add((id, value));
						return;
					}

					_inner.ApplyProperty(id, in value);
				}
			}

			public void InsertChild(int index, ICometBackendNode child)
				=> _inner.InsertChild(index, child);

			public void RemoveChildAt(int index)
				=> _inner.RemoveChildAt(index);

			public void MoveChild(int fromIndex, int toIndex)
				=> _inner.MoveChild(fromIndex, toIndex);

			public Size Measure(double widthConstraint, double heightConstraint)
				=> _inner.Measure(widthConstraint, heightConstraint);

			public double? MeasureBaseline(double width, double height)
				=> _inner.MeasureBaseline(width, height);

			public void SetContentTopInset(double dp)
				=> _inner.SetContentTopInset(dp);

			public void Arrange(Rect frame)
				=> _inner.Arrange(frame);

			public void SetEventSink(ICometEventSink? sink)
				=> _inner.SetEventSink(sink);

			public void OnOwnerViewChanged(View newView, bool isHotReload)
				=> _inner.OnOwnerViewChanged(newView, isHotReload);

			public void Dispose()
			{
				// The retained node owns its lifecycle. This adapter is only an emission
				// recorder and must never dispose the node it wraps.
			}
		}
	}

	internal static class BackendPropertyDefaults
	{
		static readonly IReadOnlyDictionary<PropertyId, PropertyValue> Values =
			new Dictionary<PropertyId, PropertyValue>
		{
			[PropertyIds.Opacity] = PropertyValue.From(1d),
			[PropertyIds.IsVisible] = PropertyValue.From(true),
			[PropertyIds.BackgroundColor] = PropertyValue.From((Color?)null),
			[PropertyIds.TranslationX] = PropertyValue.From(0d),
			[PropertyIds.TranslationY] = PropertyValue.From(0d),
			[PropertyIds.ScaleX] = PropertyValue.From(1d),
			[PropertyIds.ScaleY] = PropertyValue.From(1d),
			[PropertyIds.Rotation] = PropertyValue.From(0d),
			[PropertyIds.RotationX] = PropertyValue.From(0d),
			[PropertyIds.RotationY] = PropertyValue.From(0d),
			[PropertyIds.AnchorX] = PropertyValue.From(0.5d),
			[PropertyIds.AnchorY] = PropertyValue.From(0.5d),
			[PropertyIds.IsEnabled] = PropertyValue.From(true),
			[PropertyIds.ClipShape] = PropertyValue.From(false),
			[PropertyIds.Shadow] = PropertyValue.From(0d),
			[PropertyIds.Border] = PropertyValue.FromObject(null),
			[PropertyIds.CornerRadius] = PropertyValue.FromObject(default(CornerRadii)),
			[PropertyIds.AutomationId] = PropertyValue.From(string.Empty),
			[PropertyIds.HasTapGesture] = PropertyValue.From(false),
			[PropertyIds.Padding] = PropertyValue.FromObject(default(Thickness)),
			[PropertyIds.HasRecordGesture] = PropertyValue.From(false),
			[PropertyIds.HasLongPressGesture] = PropertyValue.From(false),

			[PropertyIds.Text_Value] = PropertyValue.From(string.Empty),
			[PropertyIds.Text_Color] = PropertyValue.From((Color?)null),
			[PropertyIds.Text_FontSize] = PropertyValue.From(0d),
			[PropertyIds.Text_FontFamily] = PropertyValue.From(string.Empty),
			[PropertyIds.Text_FontWeight] = PropertyValue.From(0),
			[PropertyIds.Text_HorizontalAlignment] = PropertyValue.From(0),
			[PropertyIds.Text_VerticalAlignment] = PropertyValue.From(0),
			[PropertyIds.Text_LineBreakMode] = PropertyValue.From(0),
			[PropertyIds.Text_MaxLines] = PropertyValue.From(0),
			[PropertyIds.Text_Runs] = PropertyValue.FromObject(Array.Empty<TextRun>()),
			[PropertyIds.Text_LineHeight] = PropertyValue.From(0d),
			[PropertyIds.Text_LineBreak] = PropertyValue.From(0),
			[PropertyIds.Text_Italic] = PropertyValue.From(false),
			[PropertyIds.Text_CharacterSpacing] = PropertyValue.From(0d),

			[PropertyIds.Button_Text] = PropertyValue.From(string.Empty),
			[PropertyIds.Button_TextColor] = PropertyValue.From((Color?)null),
			[PropertyIds.Button_FontSize] = PropertyValue.From(0d),
			[PropertyIds.Button_Outlined] = PropertyValue.From(false),
			[PropertyIds.Button_TextButton] = PropertyValue.From(false),
			[PropertyIds.Button_HasExplicitPadding] = PropertyValue.From(false),

			[PropertyIds.TextField_Text] = PropertyValue.From(string.Empty),
			[PropertyIds.TextField_Placeholder] = PropertyValue.From(string.Empty),
			[PropertyIds.TextField_TextColor] = PropertyValue.From((Color?)null),
			[PropertyIds.TextField_PlaceholderColor] = PropertyValue.From((Color?)null),
			[PropertyIds.TextField_IsPassword] = PropertyValue.From(false),
			[PropertyIds.TextField_Keyboard] = PropertyValue.From(0),
			[PropertyIds.TextField_Borderless] = PropertyValue.From(false),
			[PropertyIds.TextField_ReturnType] = PropertyValue.From(0),
			[PropertyIds.TextField_Outlined] = PropertyValue.From(false),
			[PropertyIds.TextField_LeadingIcon] = PropertyValue.From(string.Empty),
			[PropertyIds.TextField_FocusRequested] = PropertyValue.From(false),

			[PropertyIds.Image_Source] = PropertyValue.From(string.Empty),
			[PropertyIds.Image_Aspect] = PropertyValue.From(0),
			[PropertyIds.Image_Data] = PropertyValue.FromObject(Array.Empty<byte>()),
			[PropertyIds.Image_FontGlyph] = PropertyValue.From(string.Empty),
			[PropertyIds.Image_FontFamily] = PropertyValue.From(string.Empty),
			[PropertyIds.Image_FontSize] = PropertyValue.From(0d),
			[PropertyIds.Image_FontWeight] = PropertyValue.From(0),
			[PropertyIds.Image_FontItalic] = PropertyValue.From(false),
			[PropertyIds.Image_FontColor] = PropertyValue.From((Color?)null),
			[PropertyIds.Image_FontAutoScaling] = PropertyValue.From(false),

			[PropertyIds.Stack_Orientation] = PropertyValue.From(0),
			[PropertyIds.Stack_Spacing] = PropertyValue.From(0d),
			[PropertyIds.Container_Surface] = PropertyValue.From(false),
			[PropertyIds.Container_Card] = PropertyValue.From(false),
			[PropertyIds.GradientBackground] = PropertyValue.FromObject(null),
			[PropertyIds.GradientBorder] = PropertyValue.FromObject(null),

			[PropertyIds.Toggle_IsOn] = PropertyValue.From(false),
			[PropertyIds.Slider_Value] = PropertyValue.From(0d),
			[PropertyIds.Slider_Minimum] = PropertyValue.From(0d),
			[PropertyIds.Slider_Maximum] = PropertyValue.From(1d),
			[PropertyIds.List_Version] = PropertyValue.From(0),

			[PropertyIds.Icon_Symbol] = PropertyValue.From(string.Empty),
			[PropertyIds.Icon_Tint] = PropertyValue.From((Color?)null),
			[PropertyIds.Icon_Size] = PropertyValue.From(0d),
			[PropertyIds.Icon_Glyph] = PropertyValue.From(string.Empty),
			[PropertyIds.Icon_FontFamily] = PropertyValue.From(string.Empty),
			[PropertyIds.Icon_FillFrame] = PropertyValue.From(false),

			[PropertyIds.Drawer_IsOpen] = PropertyValue.From(false),
			[PropertyIds.Dialog_IsOpen] = PropertyValue.From(false),
			[PropertyIds.SelectorPanel_Index] = PropertyValue.From(0),
			[PropertyIds.Nav_SelectedIndex] = PropertyValue.From(0),
			[PropertyIds.ListDetail_IsDetailOpen] = PropertyValue.From(false),
			[PropertyIds.ContentSwitcher_Index] = PropertyValue.From(0),
			[PropertyIds.DatePicker_IsOpen] = PropertyValue.From(false),
			[PropertyIds.DatePicker_IsDialogMode] = PropertyValue.From(false),
			[PropertyIds.DatePicker_SelectedTicks] = PropertyValue.From(0L),
			[PropertyIds.DatePicker_MinimumTicks] = PropertyValue.From(0L),
			[PropertyIds.DatePicker_MaximumTicks] = PropertyValue.From(0L),
			[PropertyIds.Refresh_IsRefreshing] = PropertyValue.From(false),
		};

		public static PropertyValue Get(PropertyId id)
			=> Values.TryGetValue(id, out var value)
				? value
				: throw new InvalidOperationException(
					$"No backend reset value is registered for {id}.");
	}
}
