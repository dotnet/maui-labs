using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using Font = Microsoft.Maui.Font;

namespace HandlerTests;

[CollectionDefinition("Font application", DisableParallelization = true)]
public class FontApplicationCollection;

[Collection("Font application")]
public class FontTests(Xunit.Abstractions.ITestOutputHelper output)
{
	[Fact]
	public void FontFormatFailure_LogsAndDoesNotRenderFallbackGlyph()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar(new InvalidFontLoader());
			registrar.Register("Embedded.ttf", "Broken", typeof(FontTests).Assembly);
			var logger = new RecordingLogger();
			var manager = new WPFFontManager(registrar, logger);
			Assert.Same(manager.DefaultFontFamily, manager.GetFontFamily(Font.OfSize("Broken", 24)));
			Assert.Null(FontImageSourceHelper.RenderGlyph("A", "Broken", 24, null, manager, logger));
			var warning = Assert.Single(logger.Messages);
			Assert.Contains("Unable to resolve font Broken", warning);
			Assert.Contains("Invalid font data", warning);
		});
	}

	[Fact]
	public void PasswordEntry_CreatingNativeControl_AppliesRegisteredFont()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			foreach (var initiallyPassword in new[] { false, true })
			{
				var entry = new Entry
				{
					FontFamily = "OpenSansRegular",
					FontSize = 32,
					FontAttributes = FontAttributes.Bold | FontAttributes.Italic,
					IsPassword = initiallyPassword,
				};
				var handler = new EntryHandler();
				handler.SetMauiContext(new WPFMauiContext(app.Services));
				try
				{
					handler.SetVirtualView(entry);
					entry.IsPassword = true;
					handler.UpdateValue(nameof(IEntry.IsPassword));
					var native = Assert.IsType<System.Windows.Controls.PasswordBox>(
						typeof(EntryHandler).GetField("_passwordBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler));
					AssertFont(native.FontFamily, "Open Sans", "OpenSans-Regular.ttf");
					Assert.Equal(32, native.FontSize);
					Assert.Equal(System.Windows.FontWeights.Bold, native.FontWeight);
					Assert.Equal(System.Windows.FontStyles.Italic, native.FontStyle);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			}
		});
	}

	[Fact]
	public void GraphicsCanvas_RegisteredFont_MeasurementMatchesNativeDrawing()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var manager = (WPFFontManager)app.Services.GetRequiredService<IFontManager>();
			var canvasType = typeof(GraphicsViewHandler).Assembly.GetType("Microsoft.Maui.Handlers.WPF.WpfCanvas")!;
			foreach (var boldItalic in new[] { false, true })
			{
				var font = new Microsoft.Maui.Graphics.Font("OpenSansRegular", boldItalic ? 800 : 400,
					boldItalic ? Microsoft.Maui.Graphics.FontStyleType.Italic : Microsoft.Maui.Graphics.FontStyleType.Normal);
				var visual = new DrawingVisual();
				Microsoft.Maui.Graphics.SizeF measured;
				const string text = "WWW iii mmm";
				using (var drawing = visual.RenderOpen())
				{
					var canvas = Assert.IsAssignableFrom<Microsoft.Maui.Graphics.ICanvas>(
						Activator.CreateInstance(canvasType, drawing, 500, 100, manager));
					canvas.Font = font;
					canvas.FontSize = 32;
					measured = canvas.GetStringSize(text, font, 32);
					Assert.Equal(measured, canvas.GetStringSize(text, font, 32,
						Microsoft.Maui.Graphics.HorizontalAlignment.Center, Microsoft.Maui.Graphics.VerticalAlignment.Center));
					canvas.DrawString(text, 0, 0, Microsoft.Maui.Graphics.HorizontalAlignment.Left);
				}
				var glyph = Assert.Single(GlyphRuns(visual.Drawing));
				Assert.Equal("OpenSans-Regular.ttf", Path.GetFileName(glyph.GlyphTypeface.FontUri.LocalPath), ignoreCase: true);
				output.WriteLine("Canvas native font URI: " + glyph.GlyphTypeface.FontUri);
				var expected = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
					System.Windows.FlowDirection.LeftToRight,
					new Typeface(manager.GetFontFamily(Font.OfSize("OpenSansRegular", 32)),
						boldItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
						boldItalic ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal),
					32, Brushes.Black, 96);
				Assert.Equal((float)expected.Width, measured.Width, 3);
				Assert.Equal((float)expected.Height, measured.Height, 3);
				var bitmap = new RenderTargetBitmap(500, 100, 96, 96, PixelFormats.Pbgra32);
				bitmap.Render(visual);
				bitmap.Freeze();
				AssertRendered(bitmap);
			}
		});
	}

	[Fact]
	public void GlyphSize_ExceedsLimit_ReturnsNoImageAndLogs()
	{
		StaHelper.RunOnSta(() =>
		{
			foreach (var size in new[] { float.MaxValue, 100000f, 4097f, float.PositiveInfinity, float.NaN, 0, -1 })
			{
				var registrar = new RejectingRegistrar();
				var logger = new RecordingLogger();
				var manager = new WPFFontManager(registrar, logger);
				Assert.Null(FontImageSourceHelper.RenderGlyph("W", "Guarded", size, null, manager, logger));
				Assert.Equal(0, registrar.Lookups);
				Assert.Contains("size", Assert.Single(logger.Messages));
			}
		});
	}

	[Fact]
	public void GlyphBitmapBounds_AreLimitedBeforeAllocation()
	{
		var method = typeof(FontImageSourceHelper).GetMethod("TryGetBitmapDimensions", BindingFlags.Static | BindingFlags.NonPublic)!;
		(System.Windows.Rect Bounds, int Width, int Height)[] cases =
		[
			(new(0, 0, 30.25, 19.25), 33, 22),
			(new(-10, -10, 4094, 1022), 4096, 1024),
			(new(0, 0, 1022, 4094), 1024, 4096),
			(new(0, 0, 4094.01, 10), 0, 0),
			(new(0, 0, 10, 4094.01), 0, 0),
			(new(0, 0, 4094, 1022.01), 0, 0),
			(new(0, 0, double.MaxValue, 10), 0, 0),
			(new(0, 0, double.PositiveInfinity, 10), 0, 0),
			(new(0, 0, double.NaN, 10), 0, 0),
			(new(double.NaN, 0, 10, 10), 0, 0),
			(new(0, 0, 0, 10), 0, 0),
			(System.Windows.Rect.Empty, 0, 0),
		];
		foreach (var (bounds, width, height) in cases)
		{
			object[] arguments = [bounds, 0, 0];
			Assert.Equal(width != 0, Assert.IsType<bool>(method.Invoke(null, arguments)));
			Assert.Equal(width, arguments[1]);
			Assert.Equal(height, arguments[2]);
		}
	}

	[Fact]
	public void MissingFontCache_RemainsFailureForGlyphsAndLogsOnce()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register("missing.ttf", "Missing");
			var logger = new RecordingLogger();
			var manager = new WPFFontManager(registrar, logger);
			Assert.Same(manager.DefaultFontFamily, manager.GetFontFamily(Font.OfSize("Missing", 24)));
			Assert.Null(FontImageSourceHelper.RenderGlyph("A", "Missing", 24, null, manager, logger));
			Assert.Null(FontImageSourceHelper.RenderGlyph("A", "Missing", 24, null, manager, logger));
			Assert.Single(logger.Messages);
		});
	}

	[Fact]
	public void EmbeddedFont_TransientExtractionFailure_CanRetry()
	{
		StaHelper.RunOnSta(() =>
		{
			var loader = new RetryLoader();
			var registrar = new WPFFontRegistrar(loader);
			registrar.Register("Embedded.ttf", "Retry", typeof(FontTests).Assembly);
			var manager = new WPFFontManager(registrar);
			Assert.Same(manager.DefaultFontFamily, manager.GetFontFamily(Font.OfSize("Retry", 20)));
			AssertFont(manager.GetFontFamily(Font.OfSize("Retry", 20)), "Open Sans", null);
			Assert.Equal(2, loader.Attempts);
		});
	}

	[Fact]
	public void EmbeddedFont_AmbiguousShortName_RequiresFullResourceName()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register("Ambiguous.ttf", "Ambiguous", typeof(FontTests).Assembly);
			var error = Assert.Throws<FileLoadException>(() => registrar.GetFont("Ambiguous"));
			Assert.Contains("HandlerTests.Ambiguous.ttf", error.Message);
			Assert.Contains("Other.Ambiguous.ttf", error.Message);
			registrar.Register("HandlerTests.Ambiguous.ttf", "Exact", typeof(FontTests).Assembly);
			AssertFont(new WPFFontManager(registrar).GetFontFamily(Font.OfSize("Exact", 20)), "Open Sans", null);
		});
	}

	[Fact]
	public void NativeToolbar_WithoutMauiContext_PreservesTextFallback()
	{
		StaHelper.RunOnSta(() =>
		{
			var toolbar = new NavigationContainerView();
			toolbar.SetToolbarItems([new ToolbarItem { Text = "Home", IconImageSource = new FontImageSource { FontFamily = "Icons", Glyph = "\uf015" } }]);
			Assert.Contains(Descendants(toolbar).OfType<System.Windows.Controls.Button>(), button => Equals(button.Content, "Home"));
		});
	}

	[Fact]
	public void TextControlHandlers_ResolveAliases()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var context = new WPFMauiContext(app.Services);
			(View View, IElementHandler Handler)[] controls =
			[
				(new Button { FontFamily = "OpenSansRegular" }, new ButtonHandler()),
				(new Entry { FontFamily = "OpenSansRegular" }, new EntryHandler()),
				(new Editor { FontFamily = "OpenSansRegular" }, new EditorHandler()),
				(new Picker { FontFamily = "OpenSansRegular" }, new PickerHandler()),
				(new DatePicker { FontFamily = "OpenSansRegular" }, new DatePickerHandler()),
				(new TimePicker { FontFamily = "OpenSansRegular" }, new TimePickerHandler()),
			];
			try
			{
				foreach (var (view, handler) in controls)
				{
					handler.SetMauiContext(context);
					handler.SetVirtualView(view);
					var native = Assert.IsAssignableFrom<System.Windows.Controls.Control>(handler.PlatformView);
					AssertFont(native.FontFamily, "Open Sans", "OpenSans-Regular.ttf");
				}
			}
			finally
			{
				foreach (var (_, handler) in controls)
					handler.DisconnectHandler();
			}
		});
	}

	[Fact]
	public void RegisteredAbsolutePath_WithSpacesAndHash_UsesDeclaredFamily()
	{
		StaHelper.RunOnSta(() =>
		{
			var directory = Path.Combine(Path.GetTempPath(), "Wpf Font Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "not the family #1.ttf");
			try
			{
				File.Copy(Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", "OpenSans-Regular.ttf"), path);
				var registrar = new WPFFontRegistrar();
				registrar.Register(path, "UnrelatedAlias");
				var logger = new RecordingLogger();
				var family = new WPFFontManager(registrar, logger).GetFontFamily(Font.OfSize("UnrelatedAlias", 20));
				Assert.Empty(logger.Messages);
				AssertFont(family,
					"Open Sans", Path.GetFileName(path));
			}
			finally
			{
				Directory.Delete(directory, recursive: true);
			}
		});
	}

	[Fact]
	public void SystemFontAndDefault_AreStillNativeFonts()
	{
		StaHelper.RunOnSta(() =>
		{
			var manager = new WPFFontManager(new WPFFontRegistrar());
			AssertFont(manager.GetFontFamily(Font.OfSize("Segoe UI", 20)), "Segoe UI", null);
			Assert.Equal(System.Windows.SystemFonts.MessageFontFamily, manager.GetFontFamily(Font.Default));
		});
	}

	[Fact]
	public void IconFont_RendersRegisteredGlyphInsteadOfFallbackBox()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var manager = (WPFFontManager)app.Services.GetRequiredService<IFontManager>();
			var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", "fa_solid.ttf");
			var fileFace = new GlyphTypeface(new Uri(path));
			output.WriteLine("Family names: " + string.Join(", ", fileFace.FamilyNames.Values));
			output.WriteLine("Win32 family names: " + string.Join(", ", fileFace.Win32FamilyNames.Values));
			var family = manager.GetFontFamily(Font.OfSize("Icons", 32));
			output.WriteLine("Resolved WPF family: " + family.Source);
			var typeface = new Typeface(family, System.Windows.FontStyles.Normal,
				System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
			Assert.True(typeface.TryGetGlyphTypeface(out var native));
			Assert.Equal("fa_solid.ttf", Path.GetFileName(native.FontUri.LocalPath), ignoreCase: true);
			output.WriteLine("Native font URI: " + native.FontUri);
			Assert.True(native.CharacterToGlyphMap.TryGetValue(0xf015, out var index));
			Assert.NotEqual(0, index);

			var bitmap = FontImageSourceHelper.RenderGlyph("\uf015", "Icons", 32,
				Microsoft.Maui.Graphics.Colors.Red, manager);
			AssertRendered(bitmap);
			SaveEvidence(bitmap!, "registered-icon.png");
			var direct = FontImageSourceHelper.RenderGlyph("\uf015", family.Source, 32,
				Microsoft.Maui.Graphics.Colors.Red,
				new WPFFontManager(new FixedRegistrar(native.FontUri.LocalPath)));
			Assert.Equal(Pixels(direct!), Pixels(bitmap!));
		});
	}

	[Fact]
	public void FontImageSources_UseSameNativeRendererAcrossSurfaces()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var context = new WPFMauiContext(app.Services);
			var source = new FontImageSource { FontFamily = "Icons", Glyph = "\uf015", Size = 32, Color = Microsoft.Maui.Graphics.Colors.Red };
			var expected = FontImageSourceHelper.RenderGlyph(source.Glyph, source.FontFamily, 32, source.Color,
				(WPFFontManager)app.Services.GetRequiredService<IFontManager>());
			AssertRendered(expected);
			var image = new Image { Source = source };
			var imageButton = new ImageButton { Source = source };
			var button = new Button { ImageSource = source, Text = "Home" };
			var imageHandler = new ImageHandler();
			var imageButtonHandler = new ImageButtonHandler();
			var buttonHandler = new ButtonHandler();
			var navigationHandler = new NavigationViewHandler();
			IElementHandler[] handlers = [imageHandler, imageButtonHandler, buttonHandler, navigationHandler];
			try
			{
				foreach (var handler in handlers)
					handler.SetMauiContext(context);
				imageHandler.SetVirtualView(image);
				imageButtonHandler.SetVirtualView(imageButton);
				buttonHandler.SetVirtualView(button);
				Assert.Equal(Pixels(expected!), Pixels(Assert.IsAssignableFrom<BitmapSource>(imageHandler.PlatformView.Source)));
				Assert.Equal(Pixels(expected!), Pixels(Assert.IsAssignableFrom<BitmapSource>(
					Assert.IsType<System.Windows.Controls.Image>(imageButtonHandler.PlatformView.Content).Source)));
				var panel = Assert.IsType<System.Windows.Controls.StackPanel>(buttonHandler.PlatformView.Content);
				var originalPanel = panel;
				var originalImage = Assert.IsType<System.Windows.Controls.Image>(panel.Children[0]).Source;
				Assert.Equal(Pixels(expected!), Pixels(Assert.IsAssignableFrom<BitmapSource>(
					Assert.IsType<System.Windows.Controls.Image>(panel.Children[0]).Source)));
				button.Text = "Changed";
				buttonHandler.UpdateValue(nameof(IText.Text));
				panel = Assert.IsType<System.Windows.Controls.StackPanel>(buttonHandler.PlatformView.Content);
				Assert.Same(originalPanel, panel);
				Assert.Same(originalImage, Assert.IsType<System.Windows.Controls.Image>(panel.Children[0]).Source);
				Assert.Equal("Changed", Assert.IsType<System.Windows.Controls.TextBlock>(panel.Children[1]).Text);

				// Shell calls this before attaching the Image to its parent grid.
				var shellImage = new System.Windows.Controls.Image();
				typeof(ShellContainerView).GetMethod("SetIconSource", BindingFlags.Static | BindingFlags.NonPublic)!
					.Invoke(null, [shellImage, source, context]);
				Assert.Null(shellImage.Parent);
				Assert.Equal(Pixels(expected!), Pixels(Assert.IsAssignableFrom<BitmapSource>(shellImage.Source)));

				navigationHandler.SetVirtualView(new NavigationPage(new ContentPage()));
				navigationHandler.PlatformView.SetToolbarItems([new ToolbarItem { Text = "Home", IconImageSource = source }]);
				var toolbarImage = Assert.Single(Descendants(navigationHandler.PlatformView).OfType<System.Windows.Controls.Image>());
				Assert.Equal(Pixels(expected!), Pixels(Assert.IsAssignableFrom<BitmapSource>(toolbarImage.Source)));
				navigationHandler.PlatformView.SetToolbarItems([new ToolbarItem
				{
					Text = "Missing icon",
					IconImageSource = new FontImageSource { FontFamily = "MissingAlias", Glyph = "A" },
				}]);
				Assert.Contains(Descendants(navigationHandler.PlatformView).OfType<System.Windows.Controls.Button>(),
					nativeButton => Equals(nativeButton.Content, "Missing icon"));

				image.Source = null;
				imageHandler.UpdateValue(nameof(Microsoft.Maui.IImage.Source));
				Assert.Null(imageHandler.PlatformView.Source);
				button.ImageSource = null;
				buttonHandler.UpdateValue(nameof(Button.ImageSource));
				Assert.Equal("Changed", buttonHandler.PlatformView.Content);
			}
			finally
			{
				foreach (var handler in handlers)
					handler.DisconnectHandler();
			}
		});
	}

	[Theory]
	[InlineData("missing.ttf", "\uf015")]
	[InlineData("OpenSans-Regular.ttf", "\uf015")]
	public void UnavailableFontOrGlyph_ReturnsNoImageAndLogs(string filename, string glyph)
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register(filename, "TestFont");
			var logger = new RecordingLogger();
			var result = FontImageSourceHelper.RenderGlyph(glyph, "TestFont", 24, null,
				new WPFFontManager(registrar, logger), logger);
			Assert.Null(result);
			Assert.Contains(logger.Messages, message => message.Contains("Cannot render glyph") || message.Contains("Unable to resolve font"));
		});
	}

	[Fact]
	public void MissingRegisteredFont_LogsAndUsesExplicitDefaultForText()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register("not-present.ttf", "MissingAlias");
			var logger = new RecordingLogger();
			var manager = new WPFFontManager(registrar, logger);
			Assert.Same(manager.DefaultFontFamily, manager.GetFontFamily(Font.OfSize("MissingAlias", 20)));
			Assert.Contains(logger.Messages, message => message.Contains("Unable to resolve font MissingAlias"));
		});
	}

	[Fact]
	public void RegisteredFont_LoadsDeclaredNativeFamily()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register("OpenSans-Regular.ttf", "OpenSansRegular");
			var path = registrar.GetFont("OpenSansRegular");
			Assert.True(Path.IsPathFullyQualified(path!), $"Not an absolute font path: {path}");
			Assert.True(File.Exists(path));
			var manager = new WPFFontManager(registrar);
			AssertFont(manager.GetFontFamily(Font.OfSize("OpenSansRegular", 20)), "Open Sans", "OpenSans-Regular.ttf");
		});
	}

	[Fact]
	public void EmbeddedFont_LoadsFromSpecifiedAssembly()
	{
		StaHelper.RunOnSta(() =>
		{
			var registrar = new WPFFontRegistrar();
			registrar.Register("Embedded.ttf", "EmbeddedAlias", typeof(FontTests).Assembly);
			AssertFont(new WPFFontManager(registrar).GetFontFamily(Font.OfSize("EmbeddedAlias", 20)), "Open Sans", null);
		});
	}

	[Fact]
	public void Label_ConfigureFontsAlias_UsesNativeFont()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var label = new Label { Text = "Hello, World!", FontFamily = "OpenSansRegular" };
			var handler = new LabelHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(label);
				AssertFont(handler.PlatformView.FontFamily, "Open Sans", "OpenSans-Regular.ttf");
				var nativeLabel = handler.PlatformView;
				nativeLabel.FontSize = 32;
				nativeLabel.Foreground = System.Windows.Media.Brushes.Black;
				nativeLabel.Background = System.Windows.Media.Brushes.White;
				nativeLabel.Measure(new System.Windows.Size(500, 100));
				nativeLabel.Arrange(new System.Windows.Rect(0, 0, 500, 100));
				var labelBitmap = new RenderTargetBitmap(500, 100, 96, 96, PixelFormats.Pbgra32);
				labelBitmap.Render(nativeLabel);
				labelBitmap.Freeze();
				AssertRendered(labelBitmap);
				var labelPixels = Pixels(labelBitmap);
				Assert.True(Enumerable.Range(0, 500 * 100).Count(i => labelPixels[i * 4 + 2] < 128) > 30);
				SaveEvidence(labelBitmap, "registered-label.png");
				label.FormattedText = new FormattedString
				{
					Spans = { new Span { Text = "Registered span", FontFamily = "OpenSansRegular" } },
				};
				handler.UpdateValue(nameof(Label.FormattedText));
				var run = Assert.IsType<System.Windows.Documents.Run>(handler.PlatformView.Inlines.FirstInline);
				AssertFont(run.FontFamily, "Open Sans", "OpenSans-Regular.ttf");
				handler.PlatformView.Style = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock))
				{
					Setters = { new System.Windows.Setter(System.Windows.Controls.TextBlock.FontFamilyProperty, new FontFamily("Consolas")) },
				};
				label.FontFamily = null;
				handler.UpdateValue(nameof(ITextStyle.Font));
				AssertFont(handler.PlatformView.FontFamily, "Consolas", null);
				handler.PlatformView.Style = null;
				Assert.Equal(System.Windows.SystemFonts.MessageFontFamily, handler.PlatformView.FontFamily);
			}

			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
	{
		foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>())
		{
			yield return child;
			foreach (var descendant in Descendants(child))
				yield return descendant;
		}
	}

	static IEnumerable<GlyphRun> GlyphRuns(System.Windows.Media.Drawing drawing)
	{
		if (drawing is GlyphRunDrawing glyph)
			yield return glyph.GlyphRun;
		else if (drawing is DrawingGroup group)
			foreach (var child in group.Children)
				foreach (var run in GlyphRuns(child))
					yield return run;
	}

	static void AssertRendered(BitmapSource? bitmap)
	{
		Assert.NotNull(bitmap);
		Assert.True(bitmap.IsFrozen);
		Assert.True(bitmap.PixelWidth > 4 && bitmap.PixelHeight > 4);
		var pixels = Pixels(bitmap);
		Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0) > 30);
	}

	static void SaveEvidence(BitmapSource bitmap, string name)
	{
		var directory = Environment.GetEnvironmentVariable("MAUI_WPF_FONT_EVIDENCE_DIR");
		if (string.IsNullOrEmpty(directory))
			return;
		Directory.CreateDirectory(directory);
		using var stream = File.Create(Path.Combine(directory, name));
		var encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		encoder.Save(stream);
	}

	static byte[] Pixels(BitmapSource bitmap)
	{
		var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
		bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
		return pixels;
	}

	sealed class RetryLoader : IEmbeddedFontLoader
	{
		public int Attempts { get; private set; }
		public string? LoadFont(EmbeddedFont font)
		{
			if (++Attempts == 1)
				throw new IOException("Transient extraction failure");
			return new WPFEmbeddedFontLoader().LoadFont(font);
		}
	}

	sealed class InvalidFontLoader : IEmbeddedFontLoader
	{
		public string? LoadFont(EmbeddedFont font) => throw new FileFormatException("Invalid font data.");
	}

	sealed class FixedRegistrar(string path) : IFontRegistrar
	{
		public string GetFont(string font) => path;
		public void Register(string filename, string? alias) => throw new NotSupportedException();
		public void Register(string filename, string? alias, Assembly assembly) => throw new NotSupportedException();
	}

	sealed class RejectingRegistrar : IFontRegistrar
	{
		public int Lookups { get; private set; }
		public string GetFont(string font)
		{
			Lookups++;
			throw new InvalidOperationException("Invalid sizes must be rejected before font lookup or rendering.");
		}
		public void Register(string filename, string? alias) => throw new NotSupportedException();
		public void Register(string filename, string? alias, Assembly assembly) => throw new NotSupportedException();
	}

	sealed class RecordingLogger : ILogger<WPFFontManager>
	{
		public List<string> Messages { get; } = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel level) => true;
		public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			=> Messages.Add(formatter(state, exception) + (exception == null ? "" : " " + exception.Message));
	}

	internal static MauiApp CreateApp()
	{
		_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
		DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
		return MauiApp.CreateBuilder().UseMauiAppWPF<Application>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("fa_solid.ttf", "Icons");
			}).Build();
	}

	internal static GlyphTypeface AssertFont(FontFamily family, string expectedFamily, string? filename)
	{
		Assert.True(new Typeface(family, System.Windows.FontStyles.Normal,
			System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal).TryGetGlyphTypeface(out var glyph),
			$"No native typeface for {family.Source}");
		Assert.Contains(expectedFamily, glyph.FamilyNames.Values);
		if (filename != null)
			Assert.Equal(filename, Path.GetFileName(glyph.FontUri.LocalPath), ignoreCase: true);
		return glyph;
	}
}
