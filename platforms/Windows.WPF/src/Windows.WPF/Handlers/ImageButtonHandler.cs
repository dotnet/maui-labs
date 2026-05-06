using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.Handlers;
using WButton = System.Windows.Controls.Button;
using WImage = System.Windows.Controls.Image;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ImageButtonHandler : WPFViewHandler<Microsoft.Maui.Controls.ImageButton, WButton>
	{
		public static readonly PropertyMapper<Microsoft.Maui.Controls.ImageButton, ImageButtonHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.ImageButton.Source)] = MapSource,
				[nameof(Microsoft.Maui.Controls.ImageButton.Padding)] = MapPadding,
				[nameof(Microsoft.Maui.Controls.ImageButton.CornerRadius)] = MapCornerRadius,
				[nameof(Microsoft.Maui.Controls.ImageButton.BorderWidth)] = MapBorderWidth,
				[nameof(Microsoft.Maui.Controls.ImageButton.BorderColor)] = MapBorderColor,
				[nameof(Microsoft.Maui.Controls.ImageButton.Aspect)] = MapAspect,
			};

		public ImageButtonHandler() : base(Mapper) { }

		protected override WButton CreatePlatformView()
		{
			var btn = new WButton
			{
				Padding = new System.Windows.Thickness(0),
				Background = System.Windows.Media.Brushes.Transparent,
				BorderThickness = new System.Windows.Thickness(0),
			};
			btn.Click += OnClick;
			return btn;
		}

		void OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				// Use reflection to send clicked since IImageButton.Clicked isn't directly accessible
				var method = typeof(Microsoft.Maui.Controls.ImageButton).GetMethod("SendClicked",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
				method?.Invoke(VirtualView, null);
			}
			catch { }

			VirtualView?.Command?.Execute(VirtualView?.CommandParameter);
		}

		static void MapSource(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			if (view.Source is Microsoft.Maui.Controls.FileImageSource fis && !string.IsNullOrEmpty(fis.File))
			{
				try
				{
					var bmp = new BitmapImage(new Uri(fis.File, UriKind.RelativeOrAbsolute));
					handler.PlatformView.Content = new WImage
					{
						Source = bmp,
						Stretch = GetStretch(view.Aspect),
					};
				}
				catch { }
			}
			else if (view.Source is Microsoft.Maui.Controls.UriImageSource uis && uis.Uri != null)
			{
				try
				{
					var bmp = new BitmapImage(uis.Uri);
					handler.PlatformView.Content = new WImage
					{
						Source = bmp,
						Stretch = GetStretch(view.Aspect),
					};
				}
				catch { }
			}
		}

		static System.Windows.Media.Stretch GetStretch(Aspect aspect) => aspect switch
		{
			Aspect.Fill => System.Windows.Media.Stretch.Fill,
			Aspect.AspectFit => System.Windows.Media.Stretch.Uniform,
			Aspect.AspectFill => System.Windows.Media.Stretch.UniformToFill,
			_ => System.Windows.Media.Stretch.Uniform,
		};

		static void MapPadding(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			var p = view.Padding;
			handler.PlatformView.Padding = new System.Windows.Thickness(p.Left, p.Top, p.Right, p.Bottom);
		}

		static void MapCornerRadius(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			// WPF default Button doesn't support CornerRadius directly
		}

		static void MapBorderWidth(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			handler.PlatformView.BorderThickness = new System.Windows.Thickness(view.BorderWidth);
		}

		static void MapBorderColor(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			if (view.BorderColor != null)
			{
				var c = view.BorderColor;
				handler.PlatformView.BorderBrush = new System.Windows.Media.SolidColorBrush(
					System.Windows.Media.Color.FromArgb((byte)(c.Alpha * 255), (byte)(c.Red * 255),
						(byte)(c.Green * 255), (byte)(c.Blue * 255)));
			}
		}

		static void MapAspect(ImageButtonHandler handler, Microsoft.Maui.Controls.ImageButton view)
		{
			if (handler.PlatformView.Content is WImage img)
				img.Stretch = GetStretch(view.Aspect);
		}

		protected override void DisconnectHandler(WButton platformView)
		{
			platformView.Click -= OnClick;
			base.DisconnectHandler(platformView);
		}
	}
}
