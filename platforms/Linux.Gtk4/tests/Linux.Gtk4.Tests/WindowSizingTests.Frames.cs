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
		public Gtk.Window Native { get; set; } = null!;
		public IElementHandler WindowHandler { get; set; } = null!;
		public SizedPage Page { get; } = new();
		public SizedGrid Grid { get; } = new();
		public Label Label { get; } = new() { Text = "Native allocated frames" };
		public CollectionView Collection { get; }
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
			Collection = new CollectionView
			{
				ItemsSource = new[] { "First", "Second", "Third" },
				ItemTemplate = new DataTemplate(() =>
				{
					var card = new SizedGrid();
					card.Add(new Label
					{
						Text = string.Join(" ", Enumerable.Repeat("Card content wraps across the available width.", 12)),
						LineBreakMode = LineBreakMode.WordWrap
					});
					Cards.Add(card);
					return card;
				})
			};
			Grid.Add(Collection, 0, 1);
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
			foreach (var (view, events) in SizeEvents)
			{
				var native = (Gtk.Widget)view.Handler!.PlatformView!;
				output.WriteLine($"{Name} {step}: {view.GetType().Name} frame {view.Frame}; native {native.GetAllocatedWidth()}x{native.GetAllocatedHeight()}; events {events}; allocations {((IAllocationProbe)view).Allocations.Count}");
			}
			Assert.True(content.GetAllocatedWidth() > 0 && content.GetAllocatedHeight() > 0);
			Assert.Equal(content.GetAllocatedWidth(), Window.Width);
			Assert.Equal(content.GetAllocatedHeight(), Window.Height);
			Assert.True(WindowEvents > 0);
			Assert.Equal(Window.Width < 560, Narrow);
			foreach (var (view, events) in SizeEvents)
			{
				var native = (Gtk.Widget)view.Handler!.PlatformView!;
				Assert.True(view.Width > 0 && view.Height > 0);
				Assert.Equal(native.GetAllocatedWidth(), view.Width);
				Assert.Equal(native.GetAllocatedHeight(), view.Height);
				Assert.True(events > 0);
				Assert.Equal(view.Frame.Size, ((IAllocationProbe)view).Allocations.Last());
			}
			Assert.True(Page.Height < Window.Page!.Height, "Native navigation chrome must not be counted as page content.");
			Assert.Equal(Grid.Width, Label.Width);
			Assert.NotEmpty(Cards);
			var mappedCards = Cards.Where(c => c.Handler?.PlatformView is Gtk.Widget w && w.GetMapped()).ToArray();
			Assert.NotEmpty(mappedCards);
			foreach (var card in mappedCards)
			{
				var native = (Gtk.Widget)card.Handler!.PlatformView!;
				output.WriteLine($"{Name} {step}: card {card.Frame}; native {native.GetAllocatedWidth()}x{native.GetAllocatedHeight()}; child {card.Children[0].Frame}");
				Assert.True(card.Width > 0);
				Assert.Equal(native.GetAllocatedWidth(), card.Width);
				Assert.Equal(native.GetAllocatedHeight(), card.Height);
				Assert.Equal(card.Width, card.Children[0].Frame.Width);
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
		int[][] _allocations = [];
		Size[] _windowSizes = [];
		double[] _cardHeights = [];
		readonly Stopwatch _clock = new();

		public void Start()
		{
			foreach (var scenario in _scenarios)
			{
				app.OpenWindow(scenario.Window);
				scenario.WindowHandler = scenario.Window.Handler!;
				scenario.Native = (Gtk.Window)scenario.WindowHandler.PlatformView!;
			}
			Apply();
			GLib.Functions.TimeoutAdd(0, 50, Tick);
		}

		void Apply()
		{
			_windowEvents = _scenarios.Select(s => s.WindowEvents).ToArray();
			_viewEvents = _scenarios.Select(s => s.SizeEvents.Values.ToArray()).ToArray();
			_allocations = _scenarios.Select(s => s.SizeEvents.Keys.Select(v => ((IAllocationProbe)v).Allocations.Count).ToArray()).ToArray();
			_windowSizes = _scenarios.Select(s => new Size(s.Window.Width, s.Window.Height)).ToArray();
			_cardHeights = _scenarios.Select(s => s.Cards.FirstOrDefault(c =>
				c.Handler?.PlatformView is Gtk.Widget widget && widget.GetMapped())?.Height ?? -1).ToArray();
			if (_step < _sizes.Length)
				for (var i = 0; i < _scenarios.Length; i++)
				{
					// Different widths prove callbacks don't accidentally report the active window.
					var (width, height) = _sizes[_step];
					_scenarios[i].Native.SetDefaultSize(width + i * 10, height);
				}
			if (_step == 5)
			{
				foreach (var scenario in _scenarios)
				{
					var titlebar = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
					titlebar.SetSizeRequest(-1, 40);
					scenario.Native.Hide();
					scenario.Native.Unrealize();
					scenario.Native.SetTitlebar(titlebar);
					scenario.Native.Show();
				}
			}
			if (_step == 6)
				foreach (var scenario in _scenarios)
				{
					scenario.WindowHandler.DisconnectHandler();
					scenario.Native.SetDefaultSize(680, 600);
				}
			if (_step == 7)
				foreach (var scenario in _scenarios)
				{
					scenario.WindowHandler.SetVirtualView(scenario.Window);
					scenario.Native.SetDefaultSize(680, 600);
				}
			if (_step == 8)
				foreach (var scenario in _scenarios)
					scenario.Collection.ItemsSource = new[] { "Replacement first", "Replacement second" };
			_clock.Restart();
		}

		bool Tick()
		{
			try
			{
				var size = _sizes[Math.Min(_step, _sizes.Length - 1)];
				var ready = _scenarios.Select((s, i) => s.Native.GetAllocatedWidth() == (_step >= 6 ? 680 : size.Width + i * 10) &&
					s.Label.Width == ((Gtk.Widget)s.Grid.Handler!.PlatformView!).GetAllocatedWidth()).All(x => x);
				if ((!ready || _clock.ElapsedMilliseconds < 500) && _clock.Elapsed < TimeSpan.FromSeconds(5))
					return true;
				Assert.True(ready, "Frame scenarios did not reach their requested native allocations.");
				for (var i = 0; i < _scenarios.Length; i++)
				{
					var scenario = _scenarios[i];
					try
					{
						if (_step == 6)
						{
							Assert.Equal(_windowEvents[i], scenario.WindowEvents);
							Assert.Equal(_windowSizes[i], new Size(scenario.Window.Width, scenario.Window.Height));
							continue;
						}
						scenario.AssertFrames(output, $"step {_step}");
						var cardHeight = scenario.Cards.First(c => c.Handler?.PlatformView is Gtk.Widget widget && widget.GetMapped()).Height;
						if (_step is 1 or 3)
							Assert.True(cardHeight > _cardHeights[i], "Wrapping rows must grow when narrowed.");
						if (_step == 2)
							Assert.True(cardHeight < _cardHeights[i], "Wrapping rows must shrink when widened.");
						if (_step >= 5)
							Assert.True(scenario.Window.Height < scenario.Native.GetAllocatedHeight(),
								"Client-side titlebar must not be counted in Window.Height.");
						if (_step == _sizes.Length || _step == 8)
						{
							Assert.Equal(_windowEvents[i], scenario.WindowEvents);
							Assert.Equal(_viewEvents[i], scenario.SizeEvents.Values.ToArray());
							Assert.Equal(_allocations[i], scenario.SizeEvents.Keys.Select(v => ((IAllocationProbe)v).Allocations.Count).ToArray());
						}
						else if (_step != 5)
						{
							Assert.True(scenario.WindowEvents > _windowEvents[i]);
							if (_step < _sizes.Length)
							{
								Assert.All(scenario.SizeEvents.Values.Zip(_viewEvents[i]), pair => Assert.True(pair.First > pair.Second));
								Assert.All(scenario.SizeEvents.Keys.Select(v => ((IAllocationProbe)v).Allocations.Count).Zip(_allocations[i]),
									pair => Assert.True(pair.First > pair.Second));
							}
						}
					}
					catch (Exception ex)
					{
						output.WriteLine($"{scenario.Name} step {_step} FAILED: {ex}");
						_failures.Add(ex);
					}
				}
				if (++_step <= 8)
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
			if (_failures.Count == 0)
				new AllocationLifecycleChecks(app, output, finish).Start();
			else
				finish(new AggregateException(_failures));
			return false;
		}
	}
}
