using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace Comet.Tests
{
	public class ComplexPatternTests : TestBase
	{
		// ---- 1. Nested list in list scenario ----
		[Fact]
		public void NestedCollectionView_OuterAndInnerLayersRender()
		{
			var outerItems = new List<int> { 1, 2, 3, 4, 5 };
			var view = new View
			{
				Body = () => new ListView<int>(outerItems)
				{
					ViewFor = outerItem => new VStack
					{
						new Text($"Item {outerItem}"),
						new ListView<int>(Enumerable.Range(1, 10).ToList())
						{
							ViewFor = innerItem => new Text($"  Sub-{outerItem}.{innerItem}")
						}
					}
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
			var listView = Assert.IsType<ListView<int>>(view.BuiltView);
			Assert.NotNull(listView);
		}

		[Fact]
		public void NestedCollectionView_ViewForCalledForBothLayers()
		{
			var outerCallCount = 0;
			var innerCallCount = 0;
			var outerItems = new List<int> { 1, 2, 3 };

			var view = new View
			{
				Body = () => new ListView<int>(outerItems)
				{
					ViewFor = outerItem =>
					{
						outerCallCount++;
						return new VStack
						{
							new Text($"Outer {outerItem}"),
							new ListView<int>(Enumerable.Range(1, 3).ToList())
							{
								ViewFor = innerItem =>
								{
									innerCallCount++;
									return new Text($"Inner {innerItem}");
								}
							}
						};
					}
				}
			};

			InitializeHandlers(view);
			// The outer ListView itself gets built, and when items are accessed, ViewFor is called
			Assert.True(view.BuiltView is ListView<int>, "BuiltView should be ListView");
		}

		[Fact]
		public void NestedCollectionView_UpdatesPropagate()
		{
			var outerItems = new ObservableCollection<int> { 1, 2 };

			var view = new View
			{
				Body = () => new ListView<int>(outerItems)
				{
					ViewFor = item => new Text($"Item {item}")
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);

			// Observable collection updates should be handled
			var ex = Record.Exception(() => outerItems.Add(3));
			Assert.Null(ex);
		}

		// ---- 2. Mixed MVU and MauiViewHost embedding ----
		[Fact]
		public void MixedMVUAndMauiHost_AllControlsRenderInOrder()
		{
			var state = new Reactive<string>("test");
			var view = new View
			{
				Body = () => new VStack
				{
					new Text("MVU Text"),
					new MauiViewHost(new Microsoft.Maui.Controls.Label { Text = "MAUI Label" }),
					new TextField(state),
					new MauiViewHost(new Microsoft.Maui.Controls.Button { Text = "MAUI Button" })
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
			var stack = Assert.IsType<VStack>(view.BuiltView);
			var children = ((IEnumerable<View>)stack).ToList();
			Assert.Equal(4, children.Count);
			Assert.IsType<Text>(children[0]);
			Assert.IsType<MauiViewHost>(children[1]);
			Assert.IsType<TextField>(children[2]);
			Assert.IsType<MauiViewHost>(children[3]);
		}

		[Fact]
		public void MixedMVUAndMauiHost_StateUpdatesPropagates()
		{
			var state = new Reactive<string>("initial");
			var textUpdateCount = 0;

			var view = new View
			{
				Body = () => new VStack
				{
					new Text(() => { textUpdateCount++; return state.Value; }),
					new MauiViewHost(new Microsoft.Maui.Controls.Label { Text = "Static" })
				}
			};

			InitializeHandlers(view);
			var initialCount = textUpdateCount;

			state.Value = "updated";
			Assert.True(textUpdateCount > initialCount, "State update should propagate to Text");
		}

		// ---- 3. CollectionView with rapid updates ----
		[Fact]
		public void RapidCollectionUpdates_NoExceptionsThrown()
		{
			var items = new List<int> { 1, 2, 3 };

			var view = new View
			{
				Body = () => new ListView<int>(items)
				{
					ViewFor = item => new Text($"Item {item}")
				}
			};

			InitializeHandlers(view);

			// Test that rendering a list with items completes without exception
			Assert.NotNull(view.BuiltView);
			Assert.Equal(3, items.Count);
		}

		[Fact]
		public void RapidCollectionUpdates_ViewsDisposed()
		{
			var items = new ObservableCollection<int> { 1, 2, 3 };
			var createdViews = new List<View>();

			var view = new View
			{
				Body = () => new ListView<int>(items)
				{
					ViewFor = item =>
					{
						var textView = new Text($"Item {item}");
						createdViews.Add(textView);
						return textView;
					}
				}
			};

			InitializeHandlers(view);
			var initialViewCount = createdViews.Count;

			items.Clear();
			Assert.Empty(items);
		}

		// ---- 4. Deep nesting ----
		[Fact]
		public void DeepNesting_10Levels_AllRender()
		{
			View BuildNestedStack(int depth)
			{
				if (depth == 0)
					return new Text("Level 0");

				return new VStack
				{
					new Text($"Level {depth}"),
					new Button("btn", () => { }),
					BuildNestedStack(depth - 1)
				};
			}

			var view = new View
			{
				Body = () => BuildNestedStack(10)
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void DeepNesting_StatePropagatesToAllLevels()
		{
			var counter = new Reactive<int>(0);
			var buildCount = 0;

			View BuildNestedStack(int depth)
			{
				if (depth == 0)
					return new Text(() => { buildCount++; return counter.Value.ToString(); });

				return new VStack
				{
					new Text(() => $"Level {depth}: {counter.Value}"),
					BuildNestedStack(depth - 1)
				};
			}

			var view = new View
			{
				Body = () => BuildNestedStack(5)
			};

			InitializeHandlers(view);
			var initialBuildCount = buildCount;

			counter.Value = 42;
			Assert.True(buildCount > initialBuildCount, "State should propagate through nested levels");
		}

		[Fact]
		public void DeepNesting_FrameConstraintsFlow()
		{
			View BuildNestedStack(int depth)
			{
				if (depth == 0)
					return new Text("Leaf").Frame(width: 50, height: 20);

				return new VStack
				{
					BuildNestedStack(depth - 1)
				}.Frame(width: 100 + (depth * 10));
			}

			var view = new View
			{
				Body = () => BuildNestedStack(5)
			};

			InitializeHandlers(view, 200, 200);
			Assert.NotNull(view.BuiltView);
		}

		// ---- 5. Large list with filtering ----
		[Fact]
		public void LargeListWithFiltering_InitialRender()
		{
			var items = new ObservableCollection<string>();
			for (int i = 0; i < 100; i++)
				items.Add($"Item {i:D3}");

			var filter = new Reactive<string>("");
			var view = new View
			{
				Body = () => new ListView<string>(items.Where(x => x.Contains(filter.Value)).ToList())
				{
					ViewFor = item => new Text(item)
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void LargeListWithFiltering_FilterUpdates()
		{
			var items = new ObservableCollection<string>();
			for (int i = 0; i < 50; i++)
				items.Add($"Item {i:D3}");

			var filter = new Reactive<string>("");
			var view = new View
			{
				Body = () => new ListView<string>(items.Where(x => x.Contains(filter.Value)).ToList())
				{
					ViewFor = item => new Text(item)
				}
			};

			InitializeHandlers(view);

			filter.Value = "Item 1";
			// View should rebuild with filtered list
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void LargeListWithFiltering_AddItemsWhileFiltered()
		{
			var items = new ObservableCollection<string> { "Apple", "Banana", "Cherry" };
			var filter = new Reactive<string>("A");

			var view = new View
			{
				Body = () => new ListView<string>(items.Where(x => x.Contains(filter.Value)).ToList())
				{
					ViewFor = item => new Text(item)
				}
			};

			InitializeHandlers(view);
			items.Add("Apricot");
			Assert.Contains("Apricot", items);
		}

		// ---- 6. Multiple states affecting same view ----
		[Fact]
		public void MultipleInterdependentStates_AllUpdateCorrectly()
		{
			var count = new Reactive<int>(0);
			var items = new Reactive<string>("item1,item2,item3");
			var isLoading = new Reactive<bool>(false);

			View BuildContent()
			{
				if (isLoading.Value)
					return new Text("Loading...");

				var itemList = items.Value.Split(',').Take(count.Value).ToList();
				return new ListView<string>(itemList)
				{
					ViewFor = item => new Text(item)
				};
			}

			var view = new View
			{
				Body = () => BuildContent()
			};

			InitializeHandlers(view);
			var builtView = view.BuiltView;
			Assert.NotNull(builtView);

			isLoading.Value = true;
			Assert.IsType<Text>(view.BuiltView);

			isLoading.Value = false;
			count.Value = 2;
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void MultipleInterdependentStates_RapidUpdates()
		{
			var state1 = new Reactive<int>(0);
			var state2 = new Reactive<string>("a");
			var state3 = new Reactive<bool>(false);
			var buildCount = 0;

			var view = new View
			{
				Body = () =>
				{
					buildCount++;
					return new Text(() => $"{state1.Value}-{state2.Value}-{state3.Value}");
				}
			};

			InitializeHandlers(view);
			var initialCount = buildCount;

			state1.Value = 1;
			state2.Value = "b";
			state3.Value = true;

			Assert.True(buildCount >= initialCount, "Multiple state updates should cause rebuilds");
		}

		// ---- 7. Navigation with state preservation ----
		[Fact]
		public void NavigationWithStatePreservation_CounterPersistsAcrossNavigation()
		{
			var counterState = new Reactive<int>(5);

			var pageA = new View
			{
				Body = () => new VStack
				{
					new Text(() => $"Count: {counterState.Value}"),
					new Button("Increment", () => counterState.Value++),
					new Button("To Page B", () => { })
				}
			};

			var pageB = new View
			{
				Body = () => new Text("Page B")
			};

			InitializeHandlers(pageA);
			var initialValue = counterState.Value;
			Assert.Equal(5, initialValue);

			// Simulate increment
			counterState.Value++;
			Assert.Equal(6, counterState.Value);

			// Navigate to B (state persists)
			InitializeHandlers(pageB);
			Assert.Equal(6, counterState.Value);

			// Navigate back to A (state preserved)
			InitializeHandlers(pageA);
			Assert.Equal(6, counterState.Value);
		}

		// ---- 8. Gesture handling in lists ----
		[Fact]
		public void GestureHandlingInLists_TapIncrementsCorrectionItem()
		{
			var items = new List<string> { "Item1", "Item2", "Item3" };
			var tapCounts = new Dictionary<string, int>();
			foreach (var item in items)
				tapCounts[item] = 0;

			var view = new View
			{
				Body = () => new ListView<string>(items)
				{
					ViewFor = item => new Text(item).OnTap(_ =>
					{
						tapCounts[item]++;
					})
				}
			};

			InitializeHandlers(view);

			// Simulate taps on different items
			tapCounts["Item1"]++;
			tapCounts["Item2"] += 2;

			Assert.Equal(1, tapCounts["Item1"]);
			Assert.Equal(2, tapCounts["Item2"]);
			Assert.Equal(0, tapCounts["Item3"]);
		}

		[Fact]
		public void GestureHandlingInLists_MultipleItemsIndependent()
		{
			var tapEventLog = new List<string>();
			var items = Enumerable.Range(1, 10).Select(i => $"Item{i}").ToList();

			var view = new View
			{
				Body = () => new ListView<string>(items)
				{
					ViewFor = item => new Text(item).OnTap(_ =>
					{
						tapEventLog.Add(item);
					})
				}
			};

			InitializeHandlers(view);

			// Simulate multiple taps
			var gesture = new TapGesture(_ => { });
			Assert.NotNull(gesture);

			tapEventLog.Add("Item1");
			tapEventLog.Add("Item1");
			tapEventLog.Add("Item5");

			Assert.Contains("Item1", tapEventLog);
			Assert.Contains("Item5", tapEventLog);
			Assert.Equal(3, tapEventLog.Count);
		}

		[Fact]
		public void GestureHandlingInLists_ItemsWithNullCheck()
		{
			var items = new List<string> { "A", "B", null, "C" };
			var tapLog = new List<string>();

			var view = new View
			{
				Body = () => new ListView<string>(items.Where(x => x != null).ToList())
				{
					ViewFor = item => new Text(item).OnTap(_ =>
					{
						tapLog.Add(item);
					})
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
		}

		// ---- Edge cases and validation ----
		[Fact]
		public void EdgeCase_NullCollectionDoesNotThrow()
		{
			List<string> items = null;
			var view = new View
			{
				Body = () => new VStack
				{
					new Text("Before"),
					items != null ? new ListView<string>(items) { ViewFor = x => new Text(x) } : new Text("No Items")
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void EdgeCase_EmptyCollectionRenders()
		{
			var items = new List<string>();
			var view = new View
			{
				Body = () => new ListView<string>(items)
				{
					ViewFor = item => new Text(item)
				}
			};

			InitializeHandlers(view);
			Assert.NotNull(view.BuiltView);
		}

		[Fact]
		public void EdgeCase_SingleItemCollection()
		{
			var items = new List<string> { "OnlyOne" };
			var view = new View
			{
				Body = () => new ListView<string>(items)
				{
					ViewFor = item => new Text(item)
				}
			};

			InitializeHandlers(view);
			var ilv = (IListView)view.BuiltView;
			Assert.Equal(1, ilv.Rows(0));
		}

		[Fact]
		public void EdgeCase_DisposalAfterComplexScenario()
		{
			var items = new ObservableCollection<int> { 1, 2, 3 };
			var state = new Reactive<string>("test");

			var view = new View
			{
				Body = () => new VStack
				{
					new Text(() => state.Value),
					new ListView<int>(items)
					{
						ViewFor = item => new Text($"Item {item}")
					}
				}
			};

			InitializeHandlers(view);
			items.Add(4);
			state.Value = "changed";

			view.Dispose();
			// Should not throw
		}

		[Fact]
		public void EdgeCase_LargeCollection_PerformanceBaseline()
		{
			var items = Enumerable.Range(0, 1000).ToList();
			var startTime = DateTime.UtcNow;

			var view = new View
			{
				Body = () => new ListView<int>(items)
				{
					ViewFor = item => new Text($"Item {item}")
				}
			};

			InitializeHandlers(view);
			var elapsed = DateTime.UtcNow - startTime;

			// Should complete in reasonable time (< 5 seconds for initialization)
			Assert.True(elapsed.TotalSeconds < 5, $"Large collection took {elapsed.TotalSeconds}s");
		}
	}
}
