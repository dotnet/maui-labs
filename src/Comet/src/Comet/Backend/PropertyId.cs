#nullable enable
using System;

namespace Comet.Backend
{
	/// <summary>
	/// A dense, comparison-cheap identifier for a renderable property, replacing the
	/// string keys used by the legacy environment dictionary on the render hot path.
	/// </summary>
	/// <remarks>
	/// Ids are assigned in stable, contiguous ranges (see <see cref="PropertyIds"/>):
	/// <c>0–63</c> are reserved for <c>View</c>-common properties; control-specific
	/// ranges start at 64. Phase 1 hand-authors the registry; a source generator will
	/// later emit the same shape so unused controls' ids fall away with the controls.
	/// </remarks>
	public readonly struct PropertyId : IEquatable<PropertyId>
	{
		public ushort Value { get; }

		public PropertyId(ushort value) => Value = value;

		public bool Equals(PropertyId other) => Value == other.Value;
		public override bool Equals(object? obj) => obj is PropertyId other && Equals(other);
		public override int GetHashCode() => Value;
		public override string ToString() => $"PropertyId({Value})";

		public static bool operator ==(PropertyId a, PropertyId b) => a.Value == b.Value;
		public static bool operator !=(PropertyId a, PropertyId b) => a.Value != b.Value;
	}

	/// <summary>
	/// The stable registry of <see cref="PropertyId"/> constants. View-common ids occupy
	/// 0–63; each control claims a 16-wide range starting at 64.
	/// </summary>
	public static class PropertyIds
	{
		// --- View common (0–63) ---
		public static readonly PropertyId Opacity = new(1);
		public static readonly PropertyId IsVisible = new(2);
		public static readonly PropertyId BackgroundColor = new(3);
		public static readonly PropertyId TranslationX = new(4);
		public static readonly PropertyId TranslationY = new(5);
		public static readonly PropertyId ScaleX = new(6);
		public static readonly PropertyId ScaleY = new(7);
		public static readonly PropertyId Rotation = new(8);
		public static readonly PropertyId RotationX = new(9);
		public static readonly PropertyId RotationY = new(10);
		public static readonly PropertyId AnchorX = new(11);
		public static readonly PropertyId AnchorY = new(12);
		public static readonly PropertyId IsEnabled = new(13);
		public static readonly PropertyId ClipShape = new(14);
		public static readonly PropertyId Shadow = new(15);
		public static readonly PropertyId Border = new(16);
		public static readonly PropertyId CornerRadius = new(17);
		public static readonly PropertyId AutomationId = new(18);
		public static readonly PropertyId HasTapGesture = new(19);
		public static readonly PropertyId Padding = new(20);

		// --- Text (64–79) ---
		public static readonly PropertyId Text_Value = new(64);
		public static readonly PropertyId Text_Color = new(65);
		public static readonly PropertyId Text_FontSize = new(66);
		public static readonly PropertyId Text_FontFamily = new(67);
		public static readonly PropertyId Text_FontWeight = new(68);
		public static readonly PropertyId Text_HorizontalAlignment = new(69);
		public static readonly PropertyId Text_VerticalAlignment = new(70);
		public static readonly PropertyId Text_LineBreakMode = new(71);
		public static readonly PropertyId Text_MaxLines = new(72);
		public static readonly PropertyId Text_Runs = new(73);   // FormattedText: IReadOnlyList<TextRun>
		public static readonly PropertyId Text_LineHeight = new(74);  // explicit line height (sp)

		// --- Button (80–95) ---
		public static readonly PropertyId Button_Text = new(80);
		public static readonly PropertyId Button_TextColor = new(81);
		public static readonly PropertyId Button_FontSize = new(82);
		public static readonly PropertyId Button_Outlined = new(83);
		public static readonly PropertyId Button_TextButton = new(84);   // Material TextButton (no fill/border)

		// --- TextField / Entry (96–111) ---
		public static readonly PropertyId TextField_Text = new(96);
		public static readonly PropertyId TextField_Placeholder = new(97);
		public static readonly PropertyId TextField_TextColor = new(98);
		public static readonly PropertyId TextField_PlaceholderColor = new(99);
		public static readonly PropertyId TextField_IsPassword = new(100);
		public static readonly PropertyId TextField_Keyboard = new(101);
		public static readonly PropertyId TextField_Borderless = new(102);

		// --- Image (112–127) ---
		public static readonly PropertyId Image_Source = new(112);
		public static readonly PropertyId Image_Aspect = new(113);

		// --- Stack layouts (128–143) ---
		public static readonly PropertyId Stack_Orientation = new(128);
		public static readonly PropertyId Stack_Spacing = new(129);
		public static readonly PropertyId Container_Surface = new(130);

		// --- Toggle / Switch (144–159) ---
		public static readonly PropertyId Toggle_IsOn = new(144);

		// --- Slider (160–175) ---
		public static readonly PropertyId Slider_Value = new(160);
		public static readonly PropertyId Slider_Minimum = new(161);
		public static readonly PropertyId Slider_Maximum = new(162);

		// --- List (176–191) ---
		// Bumped whenever the list's data changes, to recompose the LazyColumn.
		public static readonly PropertyId List_Version = new(176);

		// --- Icon (192–207) ---
		public static readonly PropertyId Icon_Symbol = new(192);
		public static readonly PropertyId Icon_Tint = new(193);
		public static readonly PropertyId Icon_Size = new(194);
		public static readonly PropertyId Icon_Glyph = new(195);        // icon-font codepoint string
		public static readonly PropertyId Icon_FontFamily = new(196);   // icon-font family name

		// --- Drawer (208–223) ---
		public static readonly PropertyId Drawer_IsOpen = new(208);

		// --- Dialog / AlertDialog (224–239) ---
		public static readonly PropertyId Dialog_IsOpen = new(224);
	}
}
