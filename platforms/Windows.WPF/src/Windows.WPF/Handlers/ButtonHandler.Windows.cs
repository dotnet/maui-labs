using WButton = System.Windows.Controls.Button;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ButtonHandler : WPFViewHandler<IButton, WButton>
	{
		protected override WButton CreatePlatformView() => new WButton();

		protected override void ConnectHandler(WButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Click += OnClick;
		}

		protected override void DisconnectHandler(WButton platformView)
		{
			platformView.Click -= OnClick;
			base.DisconnectHandler(platformView);
		}

		void OnClick(object sender, System.Windows.RoutedEventArgs e)
		{
			try
			{
				VirtualView?.Clicked();
				VirtualView?.Released();
			}
			catch (System.InvalidOperationException) { }
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapText(ButtonHandler handler, IButton button)
		{
			handler.PlatformView.Content = (button as IText)?.Text ?? string.Empty;
		}

		public static void MapTextColor(ButtonHandler handler, IButton button)
		{
			if (button is ITextStyle ts)
			{
				var brush = ToBrush(ts.TextColor);
				if (brush != null)
					handler.PlatformView.Foreground = brush;
			}
		}

		public static void MapFont(ButtonHandler handler, IButton button)
		{
			if (button is not ITextStyle textStyle)
				return;

			if (textStyle.Font.Size > 0)
				handler.PlatformView.FontSize = textStyle.Font.Size;

			handler.PlatformView.FontWeight = textStyle.Font.Weight >= FontWeight.Bold
				? System.Windows.FontWeights.Bold
				: System.Windows.FontWeights.Normal;

			handler.PlatformView.FontStyle =
				(textStyle.Font.Slant == FontSlant.Italic || textStyle.Font.Slant == FontSlant.Oblique)
					? System.Windows.FontStyles.Italic
					: System.Windows.FontStyles.Normal;

			if (!string.IsNullOrEmpty(textStyle.Font.Family))
				handler.PlatformView.FontFamily = new System.Windows.Media.FontFamily(textStyle.Font.Family);
		}

		public static void MapCharacterSpacing(ButtonHandler handler, IButton button)
		{
			// WPF Button does not have a direct CharacterSpacing property.
		}

		public static void MapPadding(ButtonHandler handler, IButton button)
		{
			handler.PlatformView.Padding = new System.Windows.Thickness(
				button.Padding.Left, button.Padding.Top,
				button.Padding.Right, button.Padding.Bottom);
		}

		public static void MapImageSource(ButtonHandler handler, IButton button)
		{
			// ImageSource mapping requires an image loading pipeline; not yet available for WPF.
		}

		public static void MapBackground(ButtonHandler handler, IButton button)
		{
			if (button.Background is Microsoft.Maui.Graphics.SolidPaint sp && sp.Color != null)
				handler.PlatformView.Background = ToBrush(sp.Color);
		}

		public static void MapStrokeColor(ButtonHandler handler, IButton button)
		{
			if (button is IButtonStroke bs)
			{
				var brush = ToBrush(bs.StrokeColor);
				if (brush != null)
					handler.PlatformView.BorderBrush = brush;
			}
		}

		public static void MapStrokeThickness(ButtonHandler handler, IButton button)
		{
			if (button is IButtonStroke bs && bs.StrokeThickness >= 0)
				handler.PlatformView.BorderThickness = new System.Windows.Thickness(bs.StrokeThickness);
		}

		public static void MapCornerRadius(ButtonHandler handler, IButton button)
		{
			// WPF Button does not directly support CornerRadius without a custom ControlTemplate.
		}
	}
}
