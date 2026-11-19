using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class StartupLayoutTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void FirstShellPage_ShowsLoadedChildren_WithoutNavigationOrResize()
	{
		var backend = typeof(GtkMauiApplication).Assembly;
		output.WriteLine($"Backend: {backend.FullName}; path={backend.Location}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backend.Location)))}");
		var host = new StartupHost(output);
		host.Run([]);
		Assert.Null(host.Failure);
		Assert.True(host.Completed);
	}

	public sealed class StartupApp : Application
	{
		public ObservableCollection<string> Items { get; } = [];
		public Label Heading { get; } = new() { Text = "Startup layout" };
		public Button First { get; } = new() { Text = "First action" };
		public Button Second { get; } = new() { Text = "Second action" };
		public CollectionView Cards { get; }
		public HorizontalStackLayout Actions { get; }
		public List<Border> CreatedCards { get; } = [];

		public StartupApp()
		{
			Actions = new HorizontalStackLayout { IsVisible = false, Children = { First, Second } };
			Cards = new CollectionView
			{
				IsVisible = false,
				ItemsSource = Items,
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label();
					label.SetBinding(Label.TextProperty, ".");
					var card = new Border { Content = label, Padding = 12 };
					CreatedCards.Add(card);
					return card;
				})
			};
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			var page = new ContentPage
			{
				Content = new ScrollView
				{
					Content = new VerticalStackLayout
					{
						Spacing = 12,
						Children =
						{
							Heading,
							Actions,
							Cards
						}
					}
				}
			};
			var shell = new Shell
			{
				FlyoutBehavior = FlyoutBehavior.Disabled,
				Items = { new ShellContent { ContentTemplate = new DataTemplate(() => page) } }
			};
			return new Window(shell) { Width = 500, Height = 700 };
		}
	}

	sealed class StartupHost(ITestOutputHelper output) : GtkMauiApplication
	{
		public Exception? Failure { get; private set; }
		public bool Completed { get; private set; }
		protected override bool CreateDesktopEntry => false;
		protected override string ApplicationId => "org.maui.tests.startuplayout";
		protected override MauiApp CreateMauiApp() =>
			MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<StartupApp>().Build();

		protected override void OnStarted()
		{
			var app = (StartupApp)Application;
			var window = (Gtk.Window)app.Windows[0].Handler!.PlatformView!;
			var clock = Stopwatch.StartNew();
			int stage = 0;
			Task? load = null;
			GLib.Functions.TimeoutAdd(0, 50, () =>
			{
				try
				{
					if (load?.IsCompleted == true)
						load.GetAwaiter().GetResult();
					var views = new View[] { app.Heading, app.First, app.Second, app.Cards };
					bool arranged = window.GetAllocatedWidth() == 500 && window.GetAllocatedHeight() == 700 &&
						(stage == 0 ? IsArranged(app.Heading) : views.All(IsArranged));
					var cards = app.CreatedCards.Where(card => card.Handler != null &&
						((Gtk.Widget)card.Handler.PlatformView!).GetMapped()).ToArray();
					bool CardArranged(Border card) =>
						IsArranged(card) && card.Content is View content && IsArranged(content);
					bool itemsArranged = stage == 0 || (stage == 1
						? cards.Any(CardArranged)
						: app.Cards.Height == 200 && cards.Length == 2 && cards.All(CardArranged));
					if ((!arranged || !itemsArranged) && clock.Elapsed < TimeSpan.FromSeconds(5))
						return true;
					foreach (var view in views)
					{
						var native = (Gtk.Widget)view.Handler!.PlatformView!;
						output.WriteLine($"stage={stage} {view.GetType().Name}: managed={view.Width}x{view.Height}; native={native.GetAllocatedWidth()}x{native.GetAllocatedHeight()}");
					}
					Assert.True(arranged, "First-page children stayed unmeasured without navigation or resize.");
					foreach (var card in cards)
						output.WriteLine($"card: managed={card.Width}x{card.Height}; label={card.Content?.Width}x{card.Content?.Height}");
					Assert.True(itemsArranged, "Loaded cards stayed unmeasured without navigation.");
					if (stage > 0)
						Assert.True(app.Cards.Y > app.Actions.Y);
					if (stage == 0)
					{
						Assert.False(app.Actions.IsVisible);
						Assert.False(app.Cards.IsVisible);
						load = Task.Run(() =>
						{
							Assert.True(app.Dispatcher.Dispatch(() =>
							{
								app.Items.Add("First loaded card");
								app.Items.Add("Second loaded card");
								app.Actions.IsVisible = true;
								app.Cards.IsVisible = true;
							}), "Could not dispatch the background load to GTK.");
						});
						stage++;
						clock.Restart();
						return true;
					}
					Assert.True(app.Actions.IsVisible);
					Assert.True(app.Cards.IsVisible);
					if (stage == 1)
					{
						// Make both virtualized rows visible before requiring both allocations.
						app.Cards.HeightRequest = 200;
						stage++;
						clock.Restart();
						return true;
					}
					Assert.Contains(cards, card => card.Content is Label { Text: "First loaded card" });
					Assert.Contains(cards, card => card.Content is Label { Text: "Second loaded card" });
					Completed = true;
				}
				catch (Exception ex)
				{
					Failure = ex;
				}
				window.Close();
				return false;
			});
		}

		static bool IsArranged(View view) =>
			view.Width > 0 && view.Height > 0 &&
			view.Handler?.PlatformView is Gtk.Widget native && native.GetMapped() &&
			native.GetAllocatedWidth() == (int)view.Width &&
			native.GetAllocatedHeight() == (int)view.Height;
	}
}
