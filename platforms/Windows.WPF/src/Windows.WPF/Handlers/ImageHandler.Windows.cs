using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WImage = System.Windows.Controls.Image;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ImageHandler : WPFViewHandler<IImage, WImage>
	{
		protected override WImage CreatePlatformView() => new WImage();

		/// <summary>
		/// Resolves a MAUI image file name to a full path on disk.
		/// Searches the processed application output and Resources/Images subdirectories.
		/// </summary>
		internal static string? ResolveImagePath(string fileName)
		{
			if (string.IsNullOrEmpty(fileName)) return null;

			var appDir = AppDomain.CurrentDomain.BaseDirectory;
			var candidates = new[]
			{
				Path.Combine(appDir, fileName),
				Path.Combine(appDir, "Resources", "Images", fileName),
			};

			foreach (var path in candidates)
			{
				if (File.Exists(path)) return path;
			}

			// Search subdirectories of Resources/Images
			var imagesDir = Path.Combine(appDir, "Resources", "Images");
			if (Directory.Exists(imagesDir))
			{
				var found = Directory.EnumerateFiles(imagesDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
				if (found != null) return found;
			}

			return null;
		}

		public static void MapSource(ImageHandler handler, IImage image)
		{
			if (image.Source == null)
			{
				handler.PlatformView.Source = null;
				return;
			}

			if (image.Source is IFontImageSource fontSource)
			{
				handler.PlatformView.Source = Microsoft.Maui.Platforms.Windows.WPF.FontImageSourceHelper
					.RenderGlyph(fontSource, handler.MauiContext);
			}
			else if (image.Source is IFileImageSource fileImageSource)
			{
				var fileName = fileImageSource.File;
				if (!string.IsNullOrEmpty(fileName))
				{
					if (Path.GetExtension(fileName).Equals(".svg", StringComparison.OrdinalIgnoreCase))
						throw new NotSupportedException(
							"Runtime SVG loading is not supported by the WPF backend. Add the SVG as a MauiImage and use its processed bitmap name.");
					try
					{
						var resolvedPath = ResolveImagePath(fileName);
						if (resolvedPath != null)
						{
							handler.PlatformView.Source = new BitmapImage(new Uri(resolvedPath, UriKind.Absolute));
						}
						else
						{
							handler.PlatformView.Source = new BitmapImage(new Uri(fileName, UriKind.RelativeOrAbsolute));
						}
					}
					catch { }
				}
			}
			else if (image.Source is IUriImageSource uriImageSource)
			{
				try
				{
					var bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.UriSource = uriImageSource.Uri;
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.EndInit();
					handler.PlatformView.Source = bmp;
				}
				catch { }
			}
			else if (image.Source is IStreamImageSource streamImageSource)
			{
				_ = LoadStreamImageAsync(handler, streamImageSource);
			}
		}

		static async Task LoadStreamImageAsync(ImageHandler handler, IStreamImageSource streamSource)
		{
			try
			{
				var stream = await streamSource.GetStreamAsync(System.Threading.CancellationToken.None);
				if (stream == null) return;

				await handler.PlatformView.Dispatcher.InvokeAsync(() =>
				{
					try
					{
						var bmp = new BitmapImage();
						bmp.BeginInit();
						bmp.StreamSource = stream;
						bmp.CacheOption = BitmapCacheOption.OnLoad;
						bmp.EndInit();
						bmp.Freeze();
						handler.PlatformView.Source = bmp;
					}
					catch { }
				});
			}
			catch { }
		}

		public static void MapAspect(ImageHandler handler, IImage image)
		{
			handler.PlatformView.Stretch = image.Aspect switch
			{
				Aspect.AspectFit => System.Windows.Media.Stretch.Uniform,
				Aspect.AspectFill => System.Windows.Media.Stretch.UniformToFill,
				Aspect.Fill => System.Windows.Media.Stretch.Fill,
				_ => System.Windows.Media.Stretch.Uniform,
			};
		}
	}
}
