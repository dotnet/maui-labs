#nullable enable
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet
{
	// Backend property emission for Button. Merges with the generated `partial class Button`.
	public partial class Button
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			var text = Text?.CurrentValue;
			if (text is not null)
				node.ApplyProperty(PropertyIds.Button_Text, PropertyValue.From(text));

			if (this.GetEnvironment<Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.Button_TextColor, PropertyValue.From(color));

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.Size) is { } fontSize)
				node.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(fontSize));

			if (this.GetEnvironment<string>(EnvironmentKeys.Fonts.Family) is { Length: > 0 } family)
			{
				var registration = FontFamilyRegistry.Resolve(family);
				node.ApplyProperty(PropertyIds.Text_FontFamily, PropertyValue.From(registration.FaceName));
				if (this.GetEnvironment<Microsoft.Maui.FontWeight?>(EnvironmentKeys.Fonts.Weight) is not { } &&
					registration.PreferredWeight is { } registeredWeight)
					node.ApplyProperty(PropertyIds.Text_FontWeight, PropertyValue.From((int)registeredWeight));
			}

			if (this.GetEnvironment<Microsoft.Maui.FontWeight?>(EnvironmentKeys.Fonts.Weight) is { } weight)
				node.ApplyProperty(PropertyIds.Text_FontWeight, PropertyValue.From((int)weight));

			if (this.GetEnvironment<Microsoft.Maui.FontSlant?>(EnvironmentKeys.Fonts.Slant) is Microsoft.Maui.FontSlant.Italic)
				node.ApplyProperty(PropertyIds.Text_Italic, PropertyValue.From(true));

			if (this.GetEnvironment<int?>(EnvironmentKeys.Text.MaxLines) is { } maxLines and > 0)
				node.ApplyProperty(PropertyIds.Text_MaxLines, PropertyValue.From(maxLines));

			if (this.GetEnvironment<double?>(nameof(Microsoft.Maui.ITextStyle.CharacterSpacing)) is { } spacing)
				node.ApplyProperty(PropertyIds.Text_CharacterSpacing, PropertyValue.From(spacing));

			if (this.GetEnvironment<Microsoft.Maui.LineBreakMode?>(EnvironmentKeys.LineBreakMode.Mode) is { } mode)
				node.ApplyProperty(PropertyIds.Text_LineBreakMode, PropertyValue.From(mode switch
				{
					Microsoft.Maui.LineBreakMode.CharacterWrap => 1,
					Microsoft.Maui.LineBreakMode.NoWrap => 2,
					Microsoft.Maui.LineBreakMode.HeadTruncation => 3,
					Microsoft.Maui.LineBreakMode.TailTruncation => 4,
					Microsoft.Maui.LineBreakMode.MiddleTruncation => 5,
					_ => 0,
				}));

			// Emit the outlined flag whenever it was set (true or false) so a reactive toggle back to
			// filled reaches the node — the set-only patch would otherwise leave it stuck outlined.
			if (this.GetEnvironment<bool?>(this, "Comet.ButtonOutlined", false) is { } outlined)
				node.ApplyProperty(PropertyIds.Button_Outlined, PropertyValue.From(outlined));

			if (this.GetEnvironment<bool?>(this, "Comet.ButtonTextButton", false) == true)
				node.ApplyProperty(PropertyIds.Button_TextButton, PropertyValue.From(true));

			// Presence is distinct from value: Padding(0) intentionally removes the native
			// Button default content padding, while an unset Padding preserves the native default.
			if (this.TryGetEnvironment<Microsoft.Maui.Thickness>(
				EnvironmentKeys.Layout.Padding, out _, false))
				node.ApplyProperty(PropertyIds.Button_HasExplicitPadding, PropertyValue.From(true));
		}

		/// <summary>Renders this button as a Material <c>TextButton</c> (no fill, no border — just the
		/// label in the content color), the gold standard's dialog/confirm button style.</summary>
		public Button TextButton()
		{
			this.SetEnvironment("Comet.ButtonTextButton", true, false);
			return this;
		}

		/// <summary>Renders this button as a Material <c>OutlinedButton</c> (a bordered, no-fill
		/// button) instead of the default filled style.</summary>
		public Button Outlined() => Outlined(true);

		/// <summary>Toggles the Material <c>OutlinedButton</c> (bordered, no-fill) vs the default
		/// filled style. Settable both ways so it can be flipped reactively (e.g. a Send button that
		/// is outlined while empty and fills once there's text).</summary>
		public Button Outlined(bool on)
		{
			this.SetEnvironment("Comet.ButtonOutlined", (object)on, false);
			return this;
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			if (id == Backend.EventIds.Clicked)
				Clicked?.Invoke();
		}
	}
}
