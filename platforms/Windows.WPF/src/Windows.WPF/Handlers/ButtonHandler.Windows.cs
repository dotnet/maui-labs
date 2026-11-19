using WButton = System.Windows.Controls.Button;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ButtonHandler : WPFViewHandler<IButton, WButton>
	{
		System.Windows.Controls.StackPanel? _fontImagePanel;
		System.Windows.Controls.Image? _fontImage;
		System.Windows.Controls.TextBlock? _fontImageLabel;

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
			if (handler._fontImageLabel != null &&
				ReferenceEquals(handler.PlatformView.Content, handler._fontImagePanel) &&
				button is Microsoft.Maui.Controls.Button mauiButton && mauiButton.ImageSource is IFontImageSource)
			{
				handler._fontImageLabel.Text = mauiButton.Text ?? string.Empty;
				handler.UpdateFontImageSpacing(mauiButton);
				return;
			}
			MapImageSource(handler, button);
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

			Microsoft.Maui.Platforms.Windows.WPF.WPFFontManager.ApplyFontFamily(handler.PlatformView, textStyle.Font, handler.MauiContext);
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
			var text = (button as IText)?.Text ?? string.Empty;
			if (button is not Microsoft.Maui.Controls.Button mauiButton ||
				mauiButton.ImageSource is not IFontImageSource source)
			{
				handler._fontImagePanel = null;
				handler._fontImage = null;
				handler._fontImageLabel = null;
				handler.PlatformView.Content = text;
				return;
			}

			var image = new System.Windows.Controls.Image
			{
				Source = Microsoft.Maui.Platforms.Windows.WPF.FontImageSourceHelper.RenderGlyph(source, handler.MauiContext),
				Stretch = System.Windows.Media.Stretch.None,
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			};
			var label = new System.Windows.Controls.TextBlock
			{
				Text = text,
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			};
			var layout = mauiButton.ContentLayout;
			var position = layout.Position;
			var vertical = position is Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top
				or Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Bottom;
			var imageFirst = position is Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Left
				or Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top;
			var panel = new System.Windows.Controls.StackPanel
			{
				Orientation = vertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal,
			};
			panel.Children.Add(imageFirst ? image : label);
			panel.Children.Add(imageFirst ? label : image);
			handler._fontImagePanel = panel;
			handler._fontImage = image;
			handler._fontImageLabel = label;
			handler.UpdateFontImageSpacing(mauiButton);
			handler.PlatformView.Content = panel;
		}

		void UpdateFontImageSpacing(Microsoft.Maui.Controls.Button button)
		{
			if (_fontImage == null)
				return;
			var layout = button.ContentLayout;
			var position = layout.Position;
			var vertical = position is Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top
				or Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Bottom;
			var imageFirst = position is Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Left
				or Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top;
			var spacing = string.IsNullOrEmpty(button.Text) || _fontImage.Source == null ? 0 : layout.Spacing;
			_fontImage.Margin = vertical
				? new System.Windows.Thickness(0, imageFirst ? 0 : spacing, 0, imageFirst ? spacing : 0)
				: new System.Windows.Thickness(imageFirst ? 0 : spacing, 0, imageFirst ? spacing : 0, 0);
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
