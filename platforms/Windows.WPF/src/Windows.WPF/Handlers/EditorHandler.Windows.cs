using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WTextBox = System.Windows.Controls.TextBox;
using WTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class EditorHandler : WPFViewHandler<IEditor, WTextBox>
	{
		TextBlock? _placeholderBlock;

		protected override WTextBox CreatePlatformView()
		{
			return new WTextBox
			{
				AcceptsReturn = true,
				TextWrapping = System.Windows.TextWrapping.Wrap,
				VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
				MinHeight = 80
			};
		}

		protected override void ConnectHandler(WTextBox platformView)
		{
			base.ConnectHandler(platformView);
			platformView.TextChanged += OnTextChanged;
			platformView.KeyUp += OnKeyUp;
			platformView.GotFocus += OnFocusChanged;
			platformView.LostFocus += OnFocusChanged;
		}

		protected override void DisconnectHandler(WTextBox platformView)
		{
			platformView.TextChanged -= OnTextChanged;
			platformView.KeyUp -= OnKeyUp;
			platformView.GotFocus -= OnFocusChanged;
			platformView.LostFocus -= OnFocusChanged;
			base.DisconnectHandler(platformView);
		}

		void OnTextChanged(object sender, WTextChangedEventArgs e)
		{
			if (VirtualView == null) return;
			VirtualView.Text = PlatformView.Text;
			UpdatePlaceholderVisibility();
		}

		void OnKeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && VirtualView is Microsoft.Maui.Controls.Editor editor)
			{
				try
				{
					editor.SendCompleted();
				}
				catch { }
			}
		}

		void OnFocusChanged(object sender, RoutedEventArgs e)
		{
			UpdatePlaceholderVisibility();
		}

		void UpdatePlaceholderVisibility()
		{
			if (_placeholderBlock == null) return;
			_placeholderBlock.Visibility = string.IsNullOrEmpty(PlatformView.Text) && !PlatformView.IsFocused
				? System.Windows.Visibility.Visible
				: System.Windows.Visibility.Collapsed;
		}

		void EnsurePlaceholderBlock()
		{
			if (_placeholderBlock != null) return;

			_placeholderBlock = new TextBlock
			{
				IsHitTestVisible = false,
				Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)),
				VerticalAlignment = System.Windows.VerticalAlignment.Top,
				Margin = new System.Windows.Thickness(4, 4, 0, 0),
			};

			var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(PlatformView);
			if (adornerLayer != null)
			{
				adornerLayer.Add(new PlaceholderAdorner(PlatformView, _placeholderBlock));
			}
			else
			{
				void OnLoaded(object s, RoutedEventArgs e)
				{
					PlatformView.Loaded -= OnLoaded;
					var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(PlatformView);
					if (layer != null && _placeholderBlock != null)
						layer.Add(new PlaceholderAdorner(PlatformView, _placeholderBlock));
				}
				PlatformView.Loaded += OnLoaded;
			}
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapText(EditorHandler handler, IEditor editor)
		{
			if (handler.PlatformView.Text != editor.Text)
				handler.PlatformView.Text = editor.Text;
		}

		public static void MapPlaceholder(EditorHandler handler, IEditor editor)
		{
			handler.EnsurePlaceholderBlock();
			if (handler._placeholderBlock != null)
			{
				handler._placeholderBlock.Text = editor.Placeholder ?? string.Empty;
				handler.UpdatePlaceholderVisibility();
			}
		}

		public static void MapPlaceholderColor(EditorHandler handler, IEditor editor)
		{
			if (handler._placeholderBlock != null && editor.PlaceholderColor != null)
			{
				var brush = ToBrush(editor.PlaceholderColor);
				if (brush != null)
					handler._placeholderBlock.Foreground = brush;
			}
		}

		public static void MapTextColor(EditorHandler handler, IEditor editor)
		{
			var brush = ToBrush(editor.TextColor);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
		}

		public static void MapFont(EditorHandler handler, IEditor editor)
		{
			if (editor is ITextStyle textStyle)
			{
				var font = textStyle.Font;

				if (font.Size > 0)
					handler.PlatformView.FontSize = font.Size;

				handler.PlatformView.FontWeight = (int)font.Weight switch
				{
					>= 700 => System.Windows.FontWeights.Bold,
					>= 600 => System.Windows.FontWeights.SemiBold,
					_ => System.Windows.FontWeights.Normal
				};

				handler.PlatformView.FontStyle = (font.Slant != FontSlant.Default)
					? System.Windows.FontStyles.Italic
					: System.Windows.FontStyles.Normal;

				if (!string.IsNullOrEmpty(font.Family))
					handler.PlatformView.FontFamily = new FontFamily(font.Family);
			}
		}

		public static void MapIsReadOnly(EditorHandler handler, IEditor editor)
		{
			handler.PlatformView.IsReadOnly = editor.IsReadOnly;
		}

		public static void MapMaxLength(EditorHandler handler, IEditor editor)
		{
			handler.PlatformView.MaxLength = editor.MaxLength;
		}

		public static void MapCharacterSpacing(EditorHandler handler, IEditor editor)
		{
			// WPF TextBox does not natively support character spacing
		}

		public static void MapHorizontalTextAlignment(EditorHandler handler, IEditor editor)
		{
			handler.PlatformView.TextAlignment = editor.HorizontalTextAlignment switch
			{
				Microsoft.Maui.TextAlignment.Center => System.Windows.TextAlignment.Center,
				Microsoft.Maui.TextAlignment.End => System.Windows.TextAlignment.Right,
				_ => System.Windows.TextAlignment.Left
			};
		}

		public static void MapIsTextPredictionEnabled(EditorHandler handler, IEditor editor)
		{
			// WPF TextBox does not have a text prediction toggle
		}
	}
}
