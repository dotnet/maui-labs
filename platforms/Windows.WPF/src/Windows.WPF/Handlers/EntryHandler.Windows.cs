#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WTextBox = System.Windows.Controls.TextBox;
using WPasswordBox = System.Windows.Controls.PasswordBox;
using WTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using WColor = System.Windows.Media.Color;
using WBrush = System.Windows.Media.SolidColorBrush;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class EntryHandler : WPFViewHandler<IEntry, WTextBox>
	{
		WPasswordBox? _passwordBox;
		TextBlock? _placeholderBlock;

		protected override WTextBox CreatePlatformView() => new WTextBox();

		protected override void ConnectHandler(WTextBox platformView)
		{
			base.ConnectHandler(platformView);
			platformView.TextChanged += OnTextChanged;
			platformView.KeyUp += OnKeyUp;
			platformView.SelectionChanged += OnSelectionChanged;
			platformView.GotFocus += OnFocusChanged;
			platformView.LostFocus += OnFocusChanged;
		}

		protected override void DisconnectHandler(WTextBox platformView)
		{
			platformView.TextChanged -= OnTextChanged;
			platformView.KeyUp -= OnKeyUp;
			platformView.SelectionChanged -= OnSelectionChanged;
			platformView.GotFocus -= OnFocusChanged;
			platformView.LostFocus -= OnFocusChanged;

			if (_passwordBox != null)
			{
				_passwordBox.PasswordChanged -= OnPasswordChanged;
				_passwordBox.KeyUp -= OnKeyUp;
			}

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
			if (e.Key == Key.Enter && VirtualView is Microsoft.Maui.Controls.Entry entry)
			{
				try
				{
					entry.SendCompleted();
				}
				catch { }
			}
		}

		void OnSelectionChanged(object sender, RoutedEventArgs e)
		{
			if (VirtualView is ITextInput textInput)
			{
				textInput.CursorPosition = PlatformView.CaretIndex;
				textInput.SelectionLength = PlatformView.SelectionLength;
			}
		}

		void OnFocusChanged(object sender, RoutedEventArgs e)
		{
			UpdatePlaceholderVisibility();
		}

		void OnPasswordChanged(object sender, RoutedEventArgs e)
		{
			if (VirtualView == null || _passwordBox == null) return;
			VirtualView.Text = _passwordBox.Password;
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
				Foreground = new WBrush(WColor.FromRgb(160, 160, 160)),
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				Margin = new System.Windows.Thickness(4, 0, 0, 0),
			};

			var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(PlatformView);
			if (adornerLayer != null)
			{
				adornerLayer.Add(new PlaceholderAdorner(PlatformView, _placeholderBlock));
			}
			else
			{
				// Adorner layer not available yet; defer until Loaded
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

		static WBrush? ToBrush(Graphics.Color? color)
		{
			if (color == null) return null;
			return new WBrush(WColor.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapText(EntryHandler handler, IEntry entry)
		{
			if (entry.IsPassword && handler._passwordBox != null)
			{
				if (handler._passwordBox.Password != entry.Text)
					handler._passwordBox.Password = entry.Text;
			}
			else
			{
				if (handler.PlatformView.Text != entry.Text)
					handler.PlatformView.Text = entry.Text;
			}
		}

		public static void MapTextColor(EntryHandler handler, IEntry entry)
		{
			var brush = ToBrush(entry.TextColor);
			if (brush != null)
			{
				handler.PlatformView.Foreground = brush;
				if (handler._passwordBox != null)
					handler._passwordBox.Foreground = brush;
			}
		}

		public static void MapFont(EntryHandler handler, IEntry entry)
		{
			var font = entry.Font;

			if (font.Size > 0)
			{
				handler.PlatformView.FontSize = font.Size;
				if (handler._passwordBox != null)
					handler._passwordBox.FontSize = font.Size;
			}

			var weight = font.Weight >= FontWeight.Bold
				? System.Windows.FontWeights.Bold
				: System.Windows.FontWeights.Normal;
			handler.PlatformView.FontWeight = weight;
			if (handler._passwordBox != null)
				handler._passwordBox.FontWeight = weight;

			var style = (font.Slant == FontSlant.Italic || font.Slant == FontSlant.Oblique)
					? FontStyles.Italic
					: FontStyles.Normal;
			handler.PlatformView.FontStyle = style;
			if (handler._passwordBox != null)
				handler._passwordBox.FontStyle = style;

			if (!string.IsNullOrEmpty(font.Family))
			{
				var family = new FontFamily(font.Family);
				handler.PlatformView.FontFamily = family;
				if (handler._passwordBox != null)
					handler._passwordBox.FontFamily = family;
			}
		}

		public static void MapPlaceholder(EntryHandler handler, IEntry entry)
		{
			handler.EnsurePlaceholderBlock();
			if (handler._placeholderBlock != null)
			{
				handler._placeholderBlock.Text = entry.Placeholder ?? string.Empty;
				handler.UpdatePlaceholderVisibility();
			}
		}

		public static void MapPlaceholderColor(EntryHandler handler, IEntry entry)
		{
			if (handler._placeholderBlock != null && entry.PlaceholderColor != null)
			{
				var brush = ToBrush(entry.PlaceholderColor);
				if (brush != null)
					handler._placeholderBlock.Foreground = brush;
			}
		}

		public static void MapIsPassword(EntryHandler handler, IEntry entry)
		{
			if (entry.IsPassword)
			{
				if (handler._passwordBox == null)
				{
					handler._passwordBox = new WPasswordBox();
					handler._passwordBox.PasswordChanged += handler.OnPasswordChanged;
					handler._passwordBox.KeyUp += handler.OnKeyUp;
				}

				handler._passwordBox.Password = entry.Text ?? string.Empty;
				handler.PlatformView.Visibility = System.Windows.Visibility.Collapsed;

				// Insert PasswordBox as sibling
				if (handler.PlatformView.Parent is Panel parentPanel &&
					!parentPanel.Children.Contains(handler._passwordBox))
				{
					parentPanel.Children.Add(handler._passwordBox);
				}
				handler._passwordBox.Visibility = System.Windows.Visibility.Visible;
			}
			else
			{
				handler.PlatformView.Visibility = System.Windows.Visibility.Visible;
				if (handler._passwordBox != null)
					handler._passwordBox.Visibility = System.Windows.Visibility.Collapsed;
			}
		}

		public static void MapIsReadOnly(EntryHandler handler, IEntry entry)
		{
			handler.PlatformView.IsReadOnly = entry.IsReadOnly;
		}

		public static void MapMaxLength(EntryHandler handler, IEntry entry)
		{
			handler.PlatformView.MaxLength = entry.MaxLength;
			if (handler._passwordBox != null)
				handler._passwordBox.MaxLength = entry.MaxLength;
		}

		public static void MapHorizontalTextAlignment(EntryHandler handler, IEntry entry)
		{
			handler.PlatformView.TextAlignment = entry.HorizontalTextAlignment switch
			{
				TextAlignment.Center => System.Windows.TextAlignment.Center,
				TextAlignment.End => System.Windows.TextAlignment.Right,
				_ => System.Windows.TextAlignment.Left,
			};
		}

		public static void MapCursorPosition(EntryHandler handler, IEntry entry)
		{
			if (entry is ITextInput textInput && handler.PlatformView.CaretIndex != textInput.CursorPosition)
				handler.PlatformView.CaretIndex = textInput.CursorPosition;
		}

		public static void MapSelectionLength(EntryHandler handler, IEntry entry)
		{
			if (entry is ITextInput textInput && handler.PlatformView.SelectionLength != textInput.SelectionLength)
				handler.PlatformView.Select(handler.PlatformView.CaretIndex, textInput.SelectionLength);
		}

		public static void MapReturnType(EntryHandler handler, IEntry entry)
		{
			// WPF TextBox doesn't have a ReturnType concept.
		}

		public static void MapClearButtonVisibility(EntryHandler handler, IEntry entry)
		{
			// WPF TextBox doesn't have a built-in clear button.
		}

		public static void MapIsTextPredictionEnabled(EntryHandler handler, IEntry entry)
		{
			handler.PlatformView.AutoWordSelection = entry.IsTextPredictionEnabled;
		}

		public static void MapKeyboard(EntryHandler handler, IEntry entry)
		{
			// WPF desktop doesn't use software keyboards.
		}

		public static void MapCharacterSpacing(EntryHandler handler, IEntry entry)
		{
			// WPF TextBox doesn't have a direct CharacterSpacing property.
		}

		public static void MapBackground(EntryHandler handler, IEntry entry)
		{
			if (entry.Background is SolidPaint solidPaint)
			{
				var brush = ToBrush(solidPaint.Color);
				if (brush != null)
					handler.PlatformView.Background = brush;
			}
		}
	}

	/// <summary>
	/// WPF Adorner that overlays a placeholder TextBlock on a TextBox.
	/// </summary>
	public class PlaceholderAdorner : System.Windows.Documents.Adorner
	{
		readonly TextBlock _placeholder;

		public PlaceholderAdorner(UIElement adornedElement, TextBlock placeholder)
			: base(adornedElement)
		{
			_placeholder = placeholder;
			IsHitTestVisible = false;
			AddVisualChild(_placeholder);
		}

		protected override int VisualChildrenCount => 1;
		protected override System.Windows.Media.Visual GetVisualChild(int index) => _placeholder;

		protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
		{
			_placeholder.Arrange(new System.Windows.Rect(new System.Windows.Point(4, 0), finalSize));
			return finalSize;
		}
	}
}
