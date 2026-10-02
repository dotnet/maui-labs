using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public partial class WindowSizingTests
{
	interface IAllocationProbe
	{
		List<Size> Allocations { get; }
	}

	sealed class SizedPage : ContentPage, IAllocationProbe
	{
		public List<Size> Allocations { get; } = [];
		protected override void OnSizeAllocated(double width, double height)
		{
			Allocations.Add(new(width, height));
			base.OnSizeAllocated(width, height);
		}
	}

	sealed class SizedGrid : Grid, IAllocationProbe
	{
		public List<Size> Allocations { get; } = [];
		protected override void OnSizeAllocated(double width, double height)
		{
			Allocations.Add(new(width, height));
			base.OnSizeAllocated(width, height);
		}
	}

	sealed class SizedNavigationPage(Page page) : NavigationPage(page), IAllocationProbe
	{
		public List<Size> Allocations { get; } = [];
		protected override void OnSizeAllocated(double width, double height)
		{
			Allocations.Add(new(width, height));
			base.OnSizeAllocated(width, height);
		}
	}

	sealed class SizedShell : Shell, IAllocationProbe
	{
		public List<Size> Allocations { get; } = [];
		protected override void OnSizeAllocated(double width, double height)
		{
			Allocations.Add(new(width, height));
			base.OnSizeAllocated(width, height);
		}
	}

	sealed class FrameScenario
	{
		public string Name { get; }
		public Window Window { get; }
		public Gtk.Window Native => (Gtk.Window)Window.Handler!.PlatformView!;
		public SizedPage Page { get; } = new();
		public SizedGrid Grid { get; } = new();
		public Label Label { get; } = new() { Text = "Native allocated frames" };
		public List<SizedGrid> Cards { get; } = [];
		public Dictionary<VisualElement, int> SizeEvents { get; } = [];
		public int WindowEvents { get; private set; }
		public bool Narrow { get; private set; }

		public FrameScenario(bool shell)
		{
			Name = shell ? "Shell" : "NavigationPage";
			Grid.RowDefinitions.Add(new RowDefinition { Height = 40 });
			Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
			Grid.Add(Label);
			var collection = new CollectionView
			{
				ItemsSource = new[] { "First", "Second", "Third" },
				ItemTemplate = new DataTemplate(() =>
				{
					var card = new SizedGrid { HeightRequest = 50 };
					card.Add(new Label { Text = "Card content" });
					Cards.Add(card);
					return card;
				})
			};
			Grid.Add(collection, 0, 1);
			Page.Content = Grid;
			Page root = shell
				? new SizedShell
				{
					FlyoutBehavior = FlyoutBehavior.Disabled,
					Items = { new ShellContent { Title = "Home", ContentTemplate = new DataTemplate(() => Page) } }
				}
				: new SizedNavigationPage(Page);
			Window = new Window(root);
			Window.SizeChanged += (_, _) =>
			{
				WindowEvents++;
				Narrow = Window.Width < 560;
				// Exercise invalidation from an app's responsive SizeChanged callback.
				Label.Text = Narrow ? "Narrow" : "Wide";
			};
			foreach (var view in new VisualElement[] { root, Page, Grid })
			{
				SizeEvents.Add(view, 0);
				view.SizeChanged += (_, _) => SizeEvents[view]++;
			}
		}

		public void AssertFrames(ITestOutputHelper output, string step)
		{
			var content = Native.GetChild()!;
			output.WriteLine($"{Name} {step}: native outer {Native.GetAllocatedWidth()}x{Native.GetAllocatedHeight()}, content {content.GetAllocatedWidth()}x{content.GetAllocatedHeight()}, window {Window.Width}x{Window.Height}, events {WindowEvents}");
			Assert.True(content.GetAllocatedWidth() > 0 && content.GetAllocatedHeight() > 0);
			Assert.Equal(content.GetAllocatedWidth(), Window.Width);
			Assert.Equal(content.GetAllocatedHeight(), Window.Height);
			Assert.True(WindowEvents > 0);
			Assert.Equal(Window.Width < 560, Narrow);
			foreach (var (view, events) in SizeEvents)
			{
				var native = (Gtk.Widget)view.Handler!.PlatformView!;
				output.WriteLine($"{Name} {step}: {view.GetType().Name} frame {view.Frame}; native {native.GetAllocatedWidth()}x{native.GetAllocatedHeight()}; events {events}; allocations {((IAllocationProbe)view).Allocations.Count}");
				Assert.True(view.Width > 0 && view.Height > 0);
				Assert.Equal(native.GetAllocatedWidth(), view.Width);
				Assert.Equal(native.GetAllocatedHeight(), view.Height);
				Assert.True(events > 0);
				Assert.Equal(view.Frame.Size, ((IAllocationProbe)view).Allocations.Last());
			}
			Assert.True(Page.Height < Window.Page!.Height, "Native navigation chrome must not be counted as page content.");
			Assert.Equal(Grid.Width, Label.Width);
			Assert.NotEmpty(Cards);
			foreach (var card in Cards.Where(c => c.Handler?.PlatformView is Gtk.Widget w && w.GetMapped()))
			{
				var native = (Gtk.Widget)card.Handler!.PlatformView!;
				Assert.True(card.Width > 0);
				Assert.Equal(native.GetAllocatedWidth(), card.Width);
				Assert.Equal(card.Width, card.Children[0].Width);
				Assert.InRange(card.Width, Grid.Width - 40, Grid.Width);
			}
		}
	}

	// Extend the existing #594 host: all scenarios use its GTK main loop and real handlers.
	sealed class FrameChecks(Application app, ITestOutputHelper output, Action<Exception?> finish)
	{
		readonly List<Exception> _failures = [];
		readonly FrameScenario[] _scenarios = [new(false), new(true)];
		readonly (int Width, int Height)[] _sizes = [(1024, 768), (480, 500), (900, 600), (480, 500)];
		int _step;
		int[] _windowEvents = [];
		int[][] _viewEvents = [];
		readonly Stopwatch _clock = new();

		public void Start()
		{
			foreach (var scenario in _scenarios)
				app.OpenWindow(scenario.Window);
			Apply();
			GLib.Functions.TimeoutAdd(0, 50, Tick);
		}

		void Apply()
		{
			_windowEvents = _scenarios.Select(s => s.WindowEvents).ToArray();
			_viewEvents = _scenarios.Select(s => s.SizeEvents.Values.ToArray()).ToArray();
			if (_step < _sizes.Length)
				for (var i = 0; i < _scenarios.Length; i++)
				{
					// Different widths prove callbacks don't accidentally report the active window.
					var (width, height) = _sizes[_step];
					_scenarios[i].Native.SetDefaultSize(width + i * 10, height);
				}
			_clock.Restart();
		}

		bool Tick()
		{
			try
			{
				var size = _sizes[Math.Min(_step, _sizes.Length - 1)];
				var ready = _scenarios.Select((s, i) => s.Native.GetAllocatedWidth() == size.Width + i * 10 &&
					s.Label.Width == ((Gtk.Widget)s.Grid.Handler!.PlatformView!).GetAllocatedWidth()).All(x => x);
				if ((!ready || _clock.ElapsedMilliseconds < 500) && _clock.Elapsed < TimeSpan.FromSeconds(5))
					return true;
				Assert.True(ready, "Frame scenarios did not reach their requested native allocations.");
				for (var i = 0; i < _scenarios.Length; i++)
				{
					var scenario = _scenarios[i];
					try
					{
						scenario.AssertFrames(output, $"step {_step}");
						if (_step == _sizes.Length)
						{
							Assert.Equal(_windowEvents[i], scenario.WindowEvents);
							Assert.Equal(_viewEvents[i], scenario.SizeEvents.Values.ToArray());
						}
						else
						{
							Assert.True(scenario.WindowEvents > _windowEvents[i]);
							Assert.All(scenario.SizeEvents.Values.Zip(_viewEvents[i]), pair => Assert.True(pair.First > pair.Second));
						}
					}
					catch (Exception ex)
					{
						output.WriteLine($"{scenario.Name} step {_step} FAILED: {ex}");
						_failures.Add(ex);
					}
				}
				if (++_step <= _sizes.Length)
				{
					Apply();
					return true;
				}
			}
			catch (Exception ex)
			{
				_failures.Add(ex);
			}
			foreach (var scenario in _scenarios)
				scenario.Native.Close();
			finish(_failures.Count == 0 ? null : new AggregateException(_failures));
			return false;
		}
	}
}
