using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using System.Windows.Automation.Peers;
using System.Collections.ObjectModel;
using Xunit.Abstractions;
using WGrid = System.Windows.Controls.Grid;

namespace HandlerTests;

[Collection("CollectionView accessibility")]
public class CollectionViewAccessibilityTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void ItemPeer_UsesTemplateTextOrSemanticDescription(bool grouped, bool semantic)
	{
		var item = new ItemSummary("1", "First");
		var list = new CollectionView
		{
			IsGrouped = grouped,
			ItemsSource = grouped ? new[] { new[] { item } } : new[] { item },
			GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Group heading" }),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(ItemSummary.DisplayName));
				var root = new VerticalStackLayout { Children = { label } };
				if (semantic)
					SemanticProperties.SetDescription(root, "Semantic first");
				return root;
			}),
		};
		Run(list, native =>
		{
			var peers = GetItemPeers(native);
			if (grouped)
				AssertName(peers[0], "Group heading");
			AssertName(peers[grouped ? 1 : 0], semantic ? "Semantic first" : "First");
			Assert.NotNull(peers[grouped ? 1 : 0].GetPattern(PatternInterface.SelectionItem));
			var container = (System.Windows.UIElement)native.ItemContainerGenerator.ContainerFromIndex(grouped ? 1 : 0);
			AssertName(UIElementAutomationPeer.CreatePeerForElement(container)!, semantic ? "Semantic first" : "First");
		});
	}

	[Fact]
	public void ItemPeer_ReflectsTextAndSemanticChanges()
	{
		Label? label = null;
		VerticalStackLayout? root = null;
		var list = new CollectionView
		{
			ItemsSource = new[] { new ItemSummary("1", "First") },
			ItemTemplate = new DataTemplate(() =>
			{
				label = new Label { Text = "First" };
				root = new VerticalStackLayout { Children = { label } };
				return root;
			}),
		};
		Run(list, native =>
		{
			var peer = Assert.Single(GetItemPeers(native));
			AssertName(peer, "First");
			label!.Text = "Updated";
			AssertName(peer, "Updated");
			SemanticProperties.SetDescription(root!, "Explicit");
			AssertName(peer, "Explicit");
			SemanticProperties.SetDescription(root!, null);
			AssertName(peer, "Updated");
		});
	}

	[Fact]
	public void ItemPeer_NotifiesAutomationClientsWhenNameChanges()
	{
		Label? label = null;
		VerticalStackLayout? root = null;
		var list = new CollectionView
		{
			ItemsSource = new[] { new ItemSummary("1", "First") },
			ItemTemplate = new DataTemplate(() =>
			{
				label = new Label { Text = "First" };
				root = new VerticalStackLayout { Children = { label } };
				return root;
			}),
		};
		Run(list, native =>
		{
			var container = (System.Windows.UIElement)native.ItemContainerGenerator.ContainerFromIndex(0);
			System.Windows.Automation.AutomationProperties.SetAutomationId(container, "NameChangeItem");
			var hwnd = new System.Windows.Interop.WindowInteropHelper(System.Windows.Window.GetWindow(native)).Handle;
			var changes = new System.Collections.Concurrent.ConcurrentQueue<(string OldName, string NewName)>();
			using var ready = new ManualResetEventSlim();
			using var stop = new ManualResetEventSlim();
			var client = Task.Run(() =>
			{
				var window = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
				var item = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
					new System.Windows.Automation.PropertyCondition(
						System.Windows.Automation.AutomationElement.AutomationIdProperty, "NameChangeItem"));
				Assert.NotNull(item);
				Assert.Equal("First", item.Current.Name);
				System.Windows.Automation.AutomationPropertyChangedEventHandler onChanged = (_, e) =>
					changes.Enqueue(((string)e.OldValue, (string)e.NewValue));
				System.Windows.Automation.Automation.AddAutomationPropertyChangedEventHandler(
					item, System.Windows.Automation.TreeScope.Element, onChanged,
					System.Windows.Automation.AutomationElement.NameProperty);
				try
				{
					ready.Set();
					Assert.True(stop.Wait(TimeSpan.FromSeconds(30)), "UIA listener was not stopped.");
				}
				finally
				{
					System.Windows.Automation.Automation.RemoveAutomationPropertyChangedEventHandler(item, onChanged);
				}
			});
			try
			{
				PumpUntil(() => ready.IsSet || client.IsCompleted);
				if (client.IsCompleted)
					client.GetAwaiter().GetResult();
				label!.Text = "Updated";
				PumpUntil(() => changes.Any(change => change.NewName == "Updated"));
				SemanticProperties.SetDescription(root!, "Explicit");
				PumpUntil(() => changes.Any(change => change.NewName == "Explicit"));
				label.Text = "After clearing";
				DrainDispatcher();
				SemanticProperties.SetDescription(root!, null);
				PumpUntil(() => changes.Any(change => change.NewName == "After clearing"));
				var detail = new Label { Text = "Tail" };
				root!.Children.Add(detail);
				PumpUntil(() => changes.Any(change => change.NewName == "After clearing Tail"));
				SemanticProperties.SetDescription(detail, "Detail");
				PumpUntil(() => changes.Any(change => change.NewName == "After clearing Detail"));
				root.Children.Remove(detail);
				PumpUntil(() => changes.Any(change => change.OldName == "After clearing Detail" && change.NewName == "After clearing"));
			}
			finally
			{
				output.WriteLine($"Native UIA NameProperty events: {string.Join(", ", changes)}");
				stop.Set();
				PumpUntil(() => client.IsCompleted);
				client.GetAwaiter().GetResult();
			}
		});
	}

	[Fact]
	public void ItemPeer_CombinesVisibleTextAndHonorsChildSemantics()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { new ItemSummary("1", "First") },
			ItemTemplate = new DataTemplate(() =>
			{
				var described = new Label { Text = "Do not repeat" };
				SemanticProperties.SetDescription(described, "Accessible detail");
				return new ContentView
				{
					Content = new VerticalStackLayout
					{
						Children =
						{
							new Label { Text = "First" },
							described,
							new Label { Text = "Hidden", IsVisible = false },
						},
					},
				};
			}),
		};
		Run(list, native => AssertName(Assert.Single(GetItemPeers(native)), "First Accessible detail"));
	}

	[Fact]
	public void ItemPeer_EmptyTemplateDoesNotExposeRecord()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { new ItemSummary("secret-id", "Not displayed") },
			ItemTemplate = new DataTemplate(() => new VerticalStackLayout()),
		};
		Run(list, native => AssertName(Assert.Single(GetItemPeers(native)), ""));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ItemPeer_WithoutTemplateUsesDisplayedText(bool grouped)
	{
		var list = new CollectionView
		{
			IsGrouped = grouped,
			ItemsSource = grouped ? new[] { new[] { "First" } } : new[] { "First" },
			GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "Heading" }),
		};
		Run(list, native => AssertName(GetItemPeers(native)[grouped ? 1 : 0], "First"));
	}

	[Fact]
	public void ItemPeer_UsesSelectedTemplateAndFormattedText()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { new ItemSummary("1", "First") },
			ItemTemplate = new ItemTemplateSelector(),
		};
		Run(list, native => AssertName(Assert.Single(GetItemPeers(native)), "Formatted first"));
	}

	[Fact]
	public void ItemPeer_RespectsNativeContainerNameAndLabeledBy()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "First" },
			ItemTemplate = new DataTemplate(() => new Label { Text = "Template text" }),
		};
		Run(list, native =>
		{
			var peer = Assert.Single(GetItemPeers(native));
			var container = (System.Windows.UIElement)native.ItemContainerGenerator.ContainerFromIndex(0);
			System.Windows.Automation.AutomationProperties.SetLabeledBy(container,
				new System.Windows.Controls.TextBlock { Text = "Native label" });
			AssertName(peer, "Native label");
			System.Windows.Automation.AutomationProperties.SetName(container, "Native name");
			AssertName(peer, "Native name");
		});
	}

	[Fact]
	public void ItemPeer_AfterReplacementUsesNewTemplateContent()
	{
		var items = new ObservableCollection<ItemSummary> { new("1", "First") };
		var list = new CollectionView
		{
			ItemsSource = items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(ItemSummary.DisplayName));
				return new VerticalStackLayout { Children = { label } };
			}),
		};
		Run(list, native =>
		{
			AssertName(Assert.Single(GetItemPeers(native)), "First");
			items[0] = new("2", "Second");
			native.UpdateLayout();
			DrainDispatcher();
			AssertName(UIElementAutomationPeer.CreatePeerForElement((System.Windows.UIElement)native.ItemContainerGenerator.ContainerFromIndex(0))!, "Second");
			AssertName(Assert.Single(GetItemPeers(native)), "Second");
		});
	}

	[Fact]
	public void ItemPeer_AfterVirtualizationUsesCurrentTemplateContent()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 200).Select(i => new ItemSummary(i.ToString(), $"Item {i}")).ToArray(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { HeightRequest = 40 };
				label.SetBinding(Label.TextProperty, nameof(ItemSummary.DisplayName));
				return label;
			}),
		};
		Run(list, native =>
		{
			var originalPeer = GetItemPeers(native)[0];
			AssertName(originalPeer, "Item 0");
			native.ScrollIntoView(native.Items[199]);
			native.UpdateLayout();
			DrainDispatcher();
			Assert.Null(native.ItemContainerGenerator.ContainerFromIndex(100));
			var container = (System.Windows.UIElement)native.ItemContainerGenerator.ContainerFromIndex(199);
			AssertName(UIElementAutomationPeer.CreatePeerForElement(container)!, "Item 199");
		});
	}

	void AssertName(AutomationPeer peer, string expected)
	{
		var actual = peer.GetName();
		output.WriteLine($"{peer.GetType().Name}: expected='{expected}', actual='{actual}'");
		Assert.Equal(expected, actual);
	}

	static List<AutomationPeer> GetItemPeers(MauiCollectionListBox native)
	{
		var peer = UIElementAutomationPeer.CreatePeerForElement(native)!;
		peer.ResetChildrenCache();
		return peer.GetChildren();
	}

	static void DrainDispatcher()
		=> System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
			() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

	static void PumpUntil(Func<bool> condition)
	{
		if (condition())
			return;
		var frame = new System.Windows.Threading.DispatcherFrame();
		var deadline = DateTime.UtcNow.AddSeconds(10);
		var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
		timer.Tick += (_, _) =>
		{
			if (condition() || DateTime.UtcNow >= deadline)
				frame.Continue = false;
		};
		timer.Start();
		try
		{
			System.Windows.Threading.Dispatcher.PushFrame(frame);
		}
		finally
		{
			timer.Stop();
		}
		Assert.True(condition(), "Timed out waiting for the native UIA client.");
	}

	static void Run(CollectionView list, Action<MauiCollectionListBox> assertion)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
				DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
				using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
				var handler = new CollectionViewHandler();
				handler.SetMauiContext(new WPFMauiContext(app.Services));
				var window = new System.Windows.Window
				{
					Width = 400, Height = 300, Left = -10000, Top = -10000,
					ShowActivated = false, ShowInTaskbar = false,
				};
				try
				{
					handler.SetVirtualView(list);
					window.Content = handler.PlatformView;
					window.Show();
					window.UpdateLayout();
					var native = Assert.IsType<MauiCollectionListBox>(((WGrid)handler.PlatformView).Children[0]);
					assertion(native);
				}
				finally
				{
					window.Close();
					((IElementHandler)handler).DisconnectHandler();
				}
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The native STA assertion did not finish.");
		if (failure != null)
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}

	sealed class ItemTemplateSelector : DataTemplateSelector
	{
		protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
		{
			Assert.IsType<ItemSummary>(item);
			return new DataTemplate(() => new Label
			{
				FormattedText = new FormattedString { Spans = { new Span { Text = "Formatted " }, new Span { Text = "first" } } },
			});
		}
	}

	public record ItemSummary(string Id, string DisplayName);
}

// UseMauiAppWPF updates shared property mappers; it cannot race other handler tests.
[CollectionDefinition("CollectionView accessibility", DisableParallelization = true)]
public class CollectionViewAccessibilityCollection;
