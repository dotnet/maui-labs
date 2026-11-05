#nullable enable
using Comet.Backend;

namespace Comet
{
	public partial class TextEditor
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			if (Text?.CurrentValue is { } text)
				node.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(text));

			if (this.GetEnvironment<string>("Placeholder") is { } placeholder)
				node.ApplyProperty(PropertyIds.TextField_Placeholder, PropertyValue.From(placeholder));

			if (this.GetEnvironment<Microsoft.Maui.Graphics.Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.TextField_TextColor, PropertyValue.From(color));

			if (this.GetEnvironment<bool?>(this, "Comet.TextFieldBorderless", false) == true)
				node.ApplyProperty(PropertyIds.TextField_Borderless, PropertyValue.From(true));
		}

		/// <summary>Renders this editor without native container or indicator chrome so the
		/// surrounding surface owns the field boundary.</summary>
		public TextEditor Borderless()
		{
			this.SetEnvironment("Comet.TextFieldBorderless", true, false);
			return this;
		}

		protected internal override void OnBackendEvent<T>(EventId id, T payload)
		{
			if (id != EventIds.TextChanged || payload is not string text)
				return;

			Node?.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(text));
			Text?.Set(text);
			this.GetEnvironment<System.Action<string>>(EnvironmentKeys.Entry.TextChanged)?.Invoke(text);
		}
	}
}
