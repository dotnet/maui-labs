using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class GalleryLayoutRuntimeTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void Gallery_AllocatesNestedCards_FiltersTemplates_AndRestartsPageLifecycle()
	{
		var previousCulture = CultureInfo.CurrentCulture;
		try
		{
			var commaCulture = (CultureInfo)CultureInfo.GetCultureInfo("en-ZA").Clone();
			commaCulture.NumberFormat.NumberDecimalSeparator = ",";
			CultureInfo.CurrentCulture = commaCulture;
			Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
			var host = new GalleryHost(output);
			host.Run([]);
			Assert.Null(host.Failure);
			Assert.True(host.Completed);
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
		}
	}

	public sealed class GalleryApplication : Application
	{
		public GalleryPage Gallery { get; } = new();
		public LifecyclePage Drawing { get; } = new();
		public Shell Shell { get; } = new();

		protected override Window CreateWindow(IActivationState? activationState)
		{
			Shell.FlyoutBehavior = FlyoutBehavior.Disabled;
			Shell.Items.Add(new ShellContent { Route = "gallery", ContentTemplate = new DataTemplate(() => Gallery) });
			Shell.Items.Add(new ShellContent { Route = "drawing", ContentTemplate = new DataTemplate(() => Drawing) });
			return new Window(Shell) { Width = 1024, Height = 768 };
		}
	}

	public sealed class GalleryPage : ContentPage
	{
		public Entry Search { get; } = new() { AutomationId = "gallery-search", Placeholder = "Search", CharacterSpacing = 0.25 };
		public Button Sort { get; } = new() { Text = "Sort", FontSize = 14.5, BorderWidth = 1.25, BorderColor = Colors.Gray };
		public Grid Root { get; } = new()
		{
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
			ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
			Padding = 12, RowSpacing = 8
		};
		public List<Border> Cards { get; } = [];
		public GalleryCardsView Collection { get; private set; }
		public Entry CompactSearch { get; } = new() { IsVisible = false };
		readonly Grid _collectionRoot = new();
		string _search = "";
		bool _animationOnly;
		event EventHandler? FiltersChanged;
		public List<string> SearchUpdates { get; } = [];

		public GalleryPage()
		{
			Root.Add(Search);
			Root.Add(Sort, 1);
			Root.Add(CompactSearch);
			Collection = new GalleryCardsView
			{
				ItemsSource = AllItems(),
				EmptyView = "No matching samples",
				ItemTemplate = new DataTemplate(() =>
				{
					var title = new Label { FontFamily = "RegressionFont", FontSize = 18.5, TextColor = new Color(1f, 0.5f, 0f, 0.75f) };
					title.SetBinding(Label.TextProperty, ".");
					var grid = new Grid
					{
						Padding = 12,
						RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
						RowSpacing = 8
					};
					grid.Add(title);
					grid.Add(new Label { Text = "Nested card detail which wraps when the viewport shrinks.", LineBreakMode = LineBreakMode.WordWrap }, 0, 1);
					var border = new Border { Content = grid, StrokeThickness = 1, BackgroundColor = new Color(0.2f, 0.3f, 0.4f, 0.5f), Margin = 4 };
					Cards.Add(border);
					return new ContentView { Content = border };
				})
			};
			Search.TextChanged += (_, args) => SetSearch(args.NewTextValue);
			CompactSearch.TextChanged += (_, args) => SetSearch(args.NewTextValue);
			FiltersChanged += (_, _) =>
			{
				if (Search.Text != _search)
					Search.Text = _search;
				if (CompactSearch.Text != _search)
					CompactSearch.Text = _search;
			};
			FiltersChanged += (_, _) => RefreshResults();
			_collectionRoot.Add(Collection);
			var scroll = new ScrollView { Content = _collectionRoot };
			Root.Add(scroll, 0, 1);
			Grid.SetColumnSpan(scroll, 2);
			var footer = new Label { Text = "Gallery footer", BackgroundColor = Colors.LightGray };
			Root.Add(footer, 0, 2);
			Grid.SetColumnSpan(footer, 2);
			Content = Root;
		}

		static string[] AllItems() => Enumerable.Range(0, 12).Select(i => i == 0 ? "Lottie Player" : $"Card {i}").ToArray();

		void SetSearch(string text)
		{
			if (_search == text)
				return;
			_search = text;
			SearchUpdates.Add(text);
			FiltersChanged?.Invoke(this, EventArgs.Empty);
		}

		public void ToggleAnimationFacet()
		{
			_animationOnly = !_animationOnly;
			FiltersChanged?.Invoke(this, EventArgs.Empty);
		}

		public GalleryCardsView ReplaceCollection()
		{
			var retired = Collection;
			Collection = new GalleryCardsView
			{
				ItemsSource = retired.ItemsSource,
				ItemTemplate = retired.ItemTemplate,
				EmptyView = retired.EmptyView
			};
			_collectionRoot.Remove(retired);
			retired.DisconnectHandlers();
			_collectionRoot.Add(Collection);
			return retired;
		}

		void RefreshResults() => Collection.ItemsSource = AllItems().Where((item, index) =>
			item.Contains(_search, StringComparison.OrdinalIgnoreCase) && (!_animationOnly || index % 2 == 0)).ToArray();
	}

	public sealed class GalleryCardsView : CollectionView { }

	public sealed class LifecyclePage : ContentPage
	{
		public int AppearingCount { get; private set; }
		public int DisappearingCount { get; private set; }
		public int NavigatedToCount { get; private set; }
		public int NavigatedFromCount { get; private set; }
		public int Ticks { get; private set; }
		public Label Status { get; } = new() { Text = "FPS --" };
		IDispatcherTimer? _timer;

		public LifecyclePage() => Content = new Grid { Children = { Status } };

		protected override void OnAppearing() { base.OnAppearing(); AppearingCount++; }
		protected override void OnDisappearing() { base.OnDisappearing(); DisappearingCount++; }
		protected override void OnNavigatedTo(NavigatedToEventArgs args)
		{
			base.OnNavigatedTo(args);
			NavigatedToCount++;
			_timer ??= Dispatcher.CreateTimer();
			_timer.Interval = TimeSpan.FromMilliseconds(20);
			if (NavigatedToCount == 1)
				_timer.Tick += (_, _) => Status.Text = $"FPS {++Ticks}";
			_timer.Start();
		}
		protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
		{
			base.OnNavigatedFrom(args);
			NavigatedFromCount++;
			_timer?.Stop();
		}
	}

	sealed class GalleryHost(ITestOutputHelper output) : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override bool CreateDesktopEntry => false;
		protected override string ApplicationId => $"org.maui.tests.gallery.p{Environment.ProcessId}";
		protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<GalleryApplication>()
			.ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "RegressionFont"))
			.Build();

		protected override void OnStarted()
		{
			GLib.Functions.IdleAdd(0, () => { RunAssertions(); return false; });
		}

		async void RunAssertions()
		{
			var app = (GalleryApplication)Application;
			var window = (Gtk.Window)app.Windows[0].Handler!.PlatformView!;
			try
			{
				await Until(() => app.Gallery.Cards.Count > 0 && app.Gallery.Cards[0].Height > 0, "card allocation");
				Assert.Equal(12, app.Gallery.Cards.Count);
				foreach (var width in new[] { 1024, 440, 1100 })
				{
					GtkTestWindow.Resize(window, width, 700);
					await Until(() => window.GetAllocatedWidth() == width && app.Gallery.Root.Width > 0 &&
						window.GetAllocatedHeight() == 700 &&
						app.Gallery.Root.Height <= window.GetAllocatedHeight() &&
						Math.Abs(app.Gallery.Root.Width - ((Gtk.Widget)app.Gallery.Root.Handler!.PlatformView!).GetAllocatedWidth()) < 1 &&
						Math.Abs(app.Gallery.Root.Height - ((Gtk.Widget)app.Gallery.Root.Handler!.PlatformView!).GetAllocatedHeight()) < 1 &&
						app.Gallery.Height == ((Gtk.Widget)app.Gallery.Handler!.PlatformView!).GetAllocatedHeight(),
						$"root resize {width}");
					Assert.True(app.Gallery.Sort.Height > 1);
					Assert.True(app.Gallery.Search.Height > 1);
					foreach (var card in app.Gallery.Cards.Where(c => ((Gtk.Widget)c.Handler!.PlatformView!).GetMapped()))
					{
						Assert.True(card.Width > 0 && card.Height > 0);
						Assert.True(((Gtk.Widget)card.Handler!.PlatformView!).GetAllocatedHeight() > 0);
					}
					Assert.True(app.Gallery.Cards[0].Width <= app.Gallery.Root.Width);
					output.WriteLine($"GTK-GALLERY width={width} root={app.Gallery.Root.Frame} sort={app.Gallery.Sort.Frame} cards={app.Gallery.Cards.Count}");
					await SaveScreenshot(window, $"gallery-{width}.png");
				}

				var sort = (Gtk.Widget)app.Gallery.Sort.Handler!.PlatformView!;
				app.Gallery.Root.ColumnDefinitions[1].Width = new GridLength(0);
				await Until(() => app.Gallery.Sort.Width == 0 && !sort.GetMapped(), "positive-to-zero allocation");
				app.Gallery.Root.ColumnDefinitions[1].Width = GridLength.Auto;
				await Until(() => app.Gallery.Sort.Width > 0 && sort.GetMapped(), "zero-to-positive allocation");

				var entry = Assert.IsType<Gtk.Entry>(app.Gallery.Search.Handler!.PlatformView);
				Assert.Equal(12, ((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren().Count);
				var retiredRoots = ((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren().Cast<View>().ToArray();
				var retiredCollection = app.Gallery.ReplaceCollection();
				Assert.Empty(((IVisualTreeElement)retiredCollection).GetVisualChildren());
				Assert.All(retiredRoots, root =>
				{
					Assert.Null(root.Parent);
					Assert.Null(root.Handler);
				});
				await Until(() => ((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren().Count == 12 &&
					app.Gallery.Cards.Any(c => c.Handler?.PlatformView is Gtk.Widget widget && widget.GetMapped() && c.Height > 0),
					"replacement visual children and allocation");
				Assert.All(((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren(),
					root => Assert.Same(app.Gallery.Collection, Assert.IsAssignableFrom<Element>(root).Parent));
				var initialPanelCount = GtkLayoutPanel.TrackedInstanceCount;
				for (var iteration = 0; iteration < 8; iteration++)
				{
					app.Gallery.SearchUpdates.Clear();
					var retiredCards = app.Gallery.Cards.Where(c => c.Handler != null).ToArray();
					app.Gallery.CompactSearch.Text = "Lottie Player";
					Assert.Equal("Lottie Player", app.Gallery.Search.Text);
					Assert.Equal("Lottie Player", app.Gallery.CompactSearch.Text);
					Assert.Single(app.Gallery.Collection.ItemsSource.Cast<string>());
					AssertNativeItems(app.Gallery.Collection, 1);
					await Until(() => app.Gallery.Cards.Any(c => c.BindingContext as string == "Lottie Player" &&
						c.Handler?.PlatformView is Gtk.Widget widget && widget.GetMapped() && c.Height > 0), "filtered card allocation");
					Assert.Single(((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren());
					Assert.All(retiredCards, card => Assert.Null(card.Handler));
					app.Gallery.Collection.ScrollTo(0);
					app.Gallery.ToggleAnimationFacet();
					Assert.Single(app.Gallery.Collection.ItemsSource.Cast<string>());
					app.Gallery.CompactSearch.Text = "__no_such_gallery_sample__";
					Assert.Equal(new[] { "Lottie Player", "__no_such_gallery_sample__" }, app.Gallery.SearchUpdates);
					Assert.Empty(app.Gallery.Collection.ItemsSource);
					Assert.Empty(((IVisualTreeElement)app.Gallery.Collection).GetVisualChildren());
					var scrolled = (Gtk.ScrolledWindow)app.Gallery.Collection.Handler!.PlatformView!;
					var emptyChild = scrolled.GetChild();
					if (emptyChild is Gtk.Viewport viewport)
						emptyChild = viewport.GetChild();
					Assert.Equal("No matching samples", Assert.IsType<Gtk.Label>(emptyChild).GetText());
					await Task.Delay(16);
					entry.SetText("");
					Assert.Equal(6, app.Gallery.Collection.ItemsSource.Cast<string>().Count());
					app.Gallery.ToggleAnimationFacet();
					Assert.Equal(12, app.Gallery.Collection.ItemsSource.Cast<string>().Count());
					AssertNativeItems(app.Gallery.Collection, 12);
					await Until(() => app.Gallery.Cards.Any(c => c.Handler?.PlatformView is Gtk.Widget widget &&
						widget.GetMapped() && c.Width > 0 && c.Height > 0), "repopulated card allocation");
					await Task.Run(() =>
					{
						GC.Collect();
						GC.WaitForPendingFinalizers();
					});
					Assert.Equal(initialPanelCount, GtkLayoutPanel.TrackedInstanceCount);
					output.WriteLine($"GTK-FILTER iteration={iteration} cards={app.Gallery.Cards.Count} panels={GtkLayoutPanel.TrackedInstanceCount}");
				}
				entry.SetText("__no_such_gallery_sample__");
				Assert.Equal("__no_such_gallery_sample__", app.Gallery.Search.Text);
				entry.SetText("");
				Assert.Equal("", app.Gallery.Search.Text);
				CheckEntryFeedback(app.Gallery.Handler!.MauiContext!);
				await Until(() => app.Gallery.Root.Width > 0, "input remains responsive");

				await app.Shell.GoToAsync("//drawing", false);
				await Until(() => app.Drawing.Ticks >= 2, "drawing lifecycle starts timer");
				Assert.Equal(1, app.Drawing.AppearingCount);
				Assert.Equal(1, app.Drawing.NavigatedToCount);
				Assert.Same(app.Drawing, ((IShellContentController)app.Shell.CurrentItem.CurrentItem.CurrentItem).GetOrCreateContent());
				Assert.True(((Gtk.Widget)app.Drawing.Status.Handler!.PlatformView!).GetMapped());
				await app.Shell.GoToAsync("//gallery", false);
				Assert.Equal(1, app.Drawing.DisappearingCount);
				Assert.Equal(1, app.Drawing.NavigatedFromCount);
				var stoppedTicks = app.Drawing.Ticks;
				await Task.Delay(100);
				Assert.Equal(stoppedTicks, app.Drawing.Ticks);
				await app.Shell.GoToAsync("//drawing", false);
				await Until(() => app.Drawing.Ticks > stoppedTicks, "drawing lifecycle restarts timer");
				Assert.Equal(2, app.Drawing.AppearingCount);
				Assert.Equal(2, app.Drawing.NavigatedToCount);
				output.WriteLine($"GTK-LIFECYCLE appearing={app.Drawing.AppearingCount} disappearing={app.Drawing.DisappearingCount} to={app.Drawing.NavigatedToCount} from={app.Drawing.NavigatedFromCount} ticks={app.Drawing.Ticks}");
				await SaveScreenshot(window, "lifecycle.png");
				Completed = true;
			}
			catch (Exception ex)
			{
				Failure = ex;
				output.WriteLine($"GTK-FAIL window={window.GetAllocatedWidth()}x{window.GetAllocatedHeight()} page={app.Gallery.Frame} root={app.Gallery.Root.Frame} native-root={((Gtk.Widget)app.Gallery.Root.Handler!.PlatformView!).GetAllocatedWidth()}x{((Gtk.Widget)app.Gallery.Root.Handler!.PlatformView!).GetAllocatedHeight()}");
				output.WriteLine(ex.ToString());
			}
			finally
			{
				window.Close();
			}
		}

		static async Task Until(Func<bool> predicate, string phase)
		{
			var clock = Stopwatch.StartNew();
			while (!predicate())
			{
				Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Timed out at {phase}.");
				await Task.Delay(16);
			}
		}

		static void AssertNativeItems(CollectionView collection, uint count)
		{
			var scrolled = (Gtk.ScrolledWindow)collection.Handler!.PlatformView!;
			var list = Assert.IsType<Gtk.ListView>(scrolled.GetChild());
			Assert.Equal(count, ((Gio.ListModel)list.GetModel()!).GetNItems());
		}

		static void CheckEntryFeedback(IMauiContext context)
		{
			var entry = new Entry { Text = "old" };
			using var native = (Gtk.Entry)entry.ToPlatform(context);
			var changes = new List<string>();
			entry.TextChanged += (_, args) => changes.Add(args.NewTextValue);
			try
			{
				entry.Text = "new";
				Assert.Equal(new[] { "new" }, changes);
				native.SetMaxLength(4);
				entry.Text = "longer";
				Assert.Equal("long", entry.Text);
				Assert.Equal("long", native.GetText());
				Assert.DoesNotContain("", changes);
				native.SetText("user");
				Assert.Equal("user", entry.Text);
				entry.Text = null;
				Assert.Null(entry.Text);
				Assert.Equal("", native.GetText());
				entry.Text = "";
				Assert.Equal("", entry.Text);
			}
			finally
			{
				entry.Handler?.DisconnectHandler();
			}
		}

		static async Task SaveScreenshot(Gtk.Window window, string filename)
		{
			var directory = Environment.GetEnvironmentVariable("GTK_RUNTIME_ARTIFACTS");
			if (directory == null)
				return;
			Directory.CreateDirectory(directory);
			using var paintable = Gtk.WidgetPaintable.New(window);
			Gsk.RenderNode? node = null;
			await Until(() =>
			{
				using var snapshot = Gtk.Snapshot.New();
				paintable.Snapshot(snapshot, paintable.GetIntrinsicWidth(), paintable.GetIntrinsicHeight());
				node = snapshot.ToNode();
				return node != null;
			}, $"rendered window frame for {filename}");
			Assert.NotNull(node);
			try
			{
				var renderer = window.GetNative()!.GetRenderer()!;
				using var texture = renderer.RenderTexture(node, null);
				Assert.NotNull(texture);
				Assert.True(texture.SaveToPng(Path.Combine(directory, filename)));
			}
			finally
			{
				node.Unref();
			}
		}
	}
}
