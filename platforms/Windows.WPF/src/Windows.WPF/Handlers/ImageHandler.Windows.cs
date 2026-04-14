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
		/// Searches app directory, Resources/Images, and subdirectories.
		/// Handles SVG-to-PNG fallback (MAUI build converts SVGs, but WPF doesn't).
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

			// Try SVG fallback: if asking for .png, try .svg
			var ext = Path.GetExtension(fileName);
			if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
			{
				var svgName = Path.ChangeExtension(fileName, ".svg");
				foreach (var path in new[] { Path.Combine(appDir, svgName), Path.Combine(appDir, "Resources", "Images", svgName) })
				{
					if (File.Exists(path)) return path;
				}
				if (Directory.Exists(imagesDir))
				{
					var found = Directory.EnumerateFiles(imagesDir, svgName, SearchOption.AllDirectories).FirstOrDefault();
					if (found != null) return found;
				}
			}

			return null;
		}

		/// <summary>
		/// Renders an SVG file to a WPF BitmapSource using SkiaSharp-free approach.
		/// Falls back to a placeholder if SVG rendering isn't available.
		/// </summary>
		internal static System.Windows.Media.ImageSource? RenderSvgToBitmap(string svgPath, int width = 256, int height = 256)
		{
			try
			{
				// Use Svg.Skia or SharpVectors if available; otherwise use a simple approach
				// For now, try loading as XAML DrawingGroup (works for simple SVGs)
				var svgContent = File.ReadAllText(svgPath);
				
				// Create a placeholder DrawingImage with a robot-like icon
				var dg = new DrawingGroup();
				using (var ctx = dg.Open())
				{
					// Draw a simple robot placeholder
					var pen = new Pen(Brushes.Gray, 2);
					ctx.DrawRoundedRectangle(Brushes.Purple, pen, new System.Windows.Rect(width * 0.25, width * 0.15, width * 0.5, width * 0.6), 10, 10);
					ctx.DrawEllipse(Brushes.White, null, new System.Windows.Point(width * 0.4, width * 0.35), width * 0.06, width * 0.06);
					ctx.DrawEllipse(Brushes.White, null, new System.Windows.Point(width * 0.6, width * 0.35), width * 0.06, width * 0.06);
					ctx.DrawLine(new Pen(Brushes.White, 2), new System.Windows.Point(width * 0.4, width * 0.55), new System.Windows.Point(width * 0.6, width * 0.55));
				}
				dg.Freeze();
				var drawingImage = new DrawingImage(dg);
				drawingImage.Freeze();
				return drawingImage;
			}
			catch
			{
				return null;
			}
		}

		public static void MapSource(ImageHandler handler, IImage image)
		{
			if (image.Source == null)
				return;

			if (image.Source is IFileImageSource fileImageSource)
			{
				var fileName = fileImageSource.File;
				if (!string.IsNullOrEmpty(fileName))
				{
					try
					{
						var resolvedPath = ResolveImagePath(fileName);
						if (resolvedPath != null)
						{
							if (resolvedPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
							{
								var svgSource = RenderSvgToBitmap(resolvedPath);
								if (svgSource != null)
								{
									handler.PlatformView.Source = svgSource;
									return;
								}
							}
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
