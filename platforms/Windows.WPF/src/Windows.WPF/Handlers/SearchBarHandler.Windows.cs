using System.Windows;
using System.Windows.Input;
using WGrid = System.Windows.Controls.Grid;
using WButton = System.Windows.Controls.Button;
using WTextBox = System.Windows.Controls.TextBox;
using WColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SearchBarHandler : WPFViewHandler<ISearchBar, WGrid>
	{
		WTextBox _textBox = null!;
		WButton _searchButton = null!;
		System.Windows.Controls.TextBlock? _placeholderBlock;

		protected override WGrid CreatePlatformView()
		{
			var grid = new WGrid();
			grid.ColumnDefinitions.Add(new WColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new WColumnDefinition { Width = System.Windows.GridLength.Auto });

			_textBox = new WTextBox { MinWidth = 100 };

			// Watermark placeholder overlay
			_placeholderBlock = new System.Windows.Controls.TextBlock
			{
				IsHitTestVisible = false,
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				Margin = new System.Windows.Thickness(4, 0, 0, 0),
				Foreground = System.Windows.Media.Brushes.Gray,
			};
			WGrid.SetColumn(_placeholderBlock, 0);

			_searchButton = new WButton { Content = "🔍", Padding = new System.Windows.Thickness(8, 2, 8, 2) };

			WGrid.SetColumn(_textBox, 0);
			WGrid.SetColumn(_searchButton, 1);

			grid.Children.Add(_textBox);
			grid.Children.Add(_placeholderBlock);
			grid.Children.Add(_searchButton);

			return grid;
		}

		protected override void ConnectHandler(WGrid platformView)
		{
			base.ConnectHandler(platformView);
			_textBox.TextChanged += OnTextChanged;
			_textBox.KeyDown += OnKeyDown;
			_searchButton.Click += OnSearchClicked;
		}

		protected override void DisconnectHandler(WGrid platformView)
		{
			_textBox.TextChanged -= OnTextChanged;
			_textBox.KeyDown -= OnKeyDown;
			_searchButton.Click -= OnSearchClicked;
			base.DisconnectHandler(platformView);
		}

		void OnTextChanged(object sender, WTextChangedEventArgs e)
		{
			if (VirtualView == null) return;
			VirtualView.Text = _textBox.Text;
			UpdatePlaceholderVisibility();
		}

		void UpdatePlaceholderVisibility()
		{
			if (_placeholderBlock != null)
				_placeholderBlock.Visibility = string.IsNullOrEmpty(_textBox.Text)
					? System.Windows.Visibility.Visible
					: System.Windows.Visibility.Collapsed;
		}

		void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				VirtualView?.SearchButtonPressed();
		}

		void OnSearchClicked(object sender, RoutedEventArgs e)
		{
			VirtualView?.SearchButtonPressed();
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapText(SearchBarHandler handler, ISearchBar searchBar)
		{
			if (handler._textBox.Text != searchBar.Text)
				handler._textBox.Text = searchBar.Text;
		}

		public static void MapPlaceholder(SearchBarHandler handler, ISearchBar searchBar)
		{
			if (handler._placeholderBlock != null)
				handler._placeholderBlock.Text = searchBar.Placeholder ?? string.Empty;
			handler.UpdatePlaceholderVisibility();
		}

		public static void MapTextColor(SearchBarHandler handler, ISearchBar searchBar)
		{
			var brush = ToBrush(searchBar.TextColor);
			if (brush != null)
				handler._textBox.Foreground = brush;
		}

		public static void MapPlaceholderColor(SearchBarHandler handler, ISearchBar searchBar)
		{
			var brush = ToBrush(searchBar.PlaceholderColor);
			if (handler._placeholderBlock != null && brush != null)
				handler._placeholderBlock.Foreground = brush;
		}

		public static void MapFont(SearchBarHandler handler, ISearchBar searchBar)
		{
			var font = searchBar.Font;

			if (font.Size > 0)
				handler._textBox.FontSize = font.Size;

			handler._textBox.FontWeight = font.Weight >= Microsoft.Maui.FontWeight.Bold
				? System.Windows.FontWeights.Bold
				: System.Windows.FontWeights.Normal;

			handler._textBox.FontStyle =
				(font.Slant == FontSlant.Italic || font.Slant == FontSlant.Oblique)
					? System.Windows.FontStyles.Italic
					: System.Windows.FontStyles.Normal;

			if (!string.IsNullOrEmpty(font.Family))
				handler._textBox.FontFamily = new System.Windows.Media.FontFamily(font.Family);
		}

		public static void MapCancelButtonColor(SearchBarHandler handler, ISearchBar searchBar)
		{
			// WPF search bar does not have a dedicated cancel button.
		}

		public static void MapIsTextPredictionEnabled(SearchBarHandler handler, ISearchBar searchBar)
		{
			handler._textBox.AutoWordSelection = searchBar.IsTextPredictionEnabled;
		}

		public static void MapMaxLength(SearchBarHandler handler, ISearchBar searchBar)
		{
			handler._textBox.MaxLength = searchBar.MaxLength;
		}

		public static void MapCharacterSpacing(SearchBarHandler handler, ISearchBar searchBar)
		{
			// WPF TextBox doesn't have a direct CharacterSpacing property.
		}

		public static void MapHorizontalTextAlignment(SearchBarHandler handler, ISearchBar searchBar)
		{
			handler._textBox.TextAlignment = searchBar.HorizontalTextAlignment switch
			{
				Microsoft.Maui.TextAlignment.Center => System.Windows.TextAlignment.Center,
				Microsoft.Maui.TextAlignment.End => System.Windows.TextAlignment.Right,
				_ => System.Windows.TextAlignment.Left,
			};
		}
	}
}
