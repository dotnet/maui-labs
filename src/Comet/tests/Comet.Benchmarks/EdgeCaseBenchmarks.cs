using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Edge cases that could expose performance cliffs.
	/// Deep nesting, wide trees, many-state views, conditional toggling,
	/// and collection mutations.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class EdgeCaseBenchmarks
	{
		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		[Params(10, 50, 200)]
		public int Size;

		// --- Very wide tree: one parent with N children ---

		[Benchmark(Description = "XAML: Wide tree (1 parent, N children)")]
		public object XamlWideTree()
		{
			var stack = new VerticalStackLayout();
			for (int i = 0; i < Size; i++)
			{
				var row = new HorizontalStackLayout();
				row.Children.Add(new Microsoft.Maui.Controls.Label { Text = $"#{i}" });
				row.Children.Add(new Microsoft.Maui.Controls.Entry { Placeholder = $"Input {i}" });
				row.Children.Add(new Microsoft.Maui.Controls.Button { Text = "Go" });
				stack.Children.Add(row);
			}
			return stack;
		}

		[Benchmark(Description = "MVU: Wide tree (1 parent, N children)")]
		public object MvuWideTree()
		{
			var view = new WideCometView(Size);
			BenchmarkUI.InitializeHandlers(view);
			return view;
		}

		// --- Many independent state fields ---

		[Benchmark(Description = "MVU: View with N independent State<T> fields")]
		public void MvuManyStates()
		{
			var view = new ManyStatesCometView(Size);
			BenchmarkUI.InitializeHandlers(view);

			// Update each state once
			for (int i = 0; i < Size; i++)
				view.SetState(i, $"Updated {i}");
		}

		// --- Alternating add/remove (list churn) ---

		[Benchmark(Description = "XAML: Collection add/remove churn")]
		public void XamlCollectionChurn()
		{
			var items = new ObservableCollection<string>();
			var stack = new VerticalStackLayout();

			// Initial fill
			for (int i = 0; i < Size; i++)
				items.Add($"Item {i}");

			// Build labels
			foreach (var item in items)
				stack.Children.Add(new Microsoft.Maui.Controls.Label { Text = item });

			// Churn: remove first, add new at end
			for (int i = 0; i < Size; i++)
			{
				if (stack.Children.Count > 0)
					stack.Children.RemoveAt(0);
				stack.Children.Add(new Microsoft.Maui.Controls.Label { Text = $"New {i}" });
			}
		}

		[Benchmark(Description = "MVU: List state churn (add/remove)")]
		public void MvuListChurn()
		{
			var view = new ListChurnCometView(Size);
			BenchmarkUI.InitializeHandlers(view);

			for (int i = 0; i < Size; i++)
				view.ChurnOne(i);
		}

		// --- Deeply nested conditional rendering ---

		[Benchmark(Description = "MVU: Deep conditional toggle (show/hide layers)")]
		public void MvuDeepConditional()
		{
			var view = new DeepConditionalView(Size);
			BenchmarkUI.InitializeHandlers(view);

			// Toggle 10 times
			for (int i = 0; i < 10; i++)
				view.Toggle();
		}

		// --- State change that doesn't affect visible portion ---

		[Benchmark(Description = "MVU: State change affecting hidden subtree")]
		public void MvuHiddenStateChange()
		{
			var view = new HiddenStateCometView(Size);
			BenchmarkUI.InitializeHandlers(view);

			// Change state that's in the hidden branch
			for (int i = 0; i < Size; i++)
				view._hiddenCounter.Value = i;
		}

		// --- Rebuild with type changes (different view types on rebuild) ---

		[Benchmark(Description = "MVU: View type changes on rebuild")]
		public void MvuTypeChanges()
		{
			var view = new TypeChangingView(Size);
			BenchmarkUI.InitializeHandlers(view);

			for (int i = 0; i < Size; i++)
				view.NextType();
		}
	}

	// --- Helper views ---

	public class WideCometView : Comet.View
	{
		readonly int _count;
		public WideCometView(int count)
		{
			_count = count;
			Body = () =>
			{
				var children = new Comet.View[_count];
				for (int i = 0; i < _count; i++)
				{
					children[i] = new HStack
					{
						new Text($"#{i}"),
						new TextField("", $"Input {i}"),
						new Comet.Button("Go", null)
					};
				}
				return LayoutHelper.ToVStack(children);
			};
		}
	}

	public class ManyStatesCometView : Comet.View
	{
		readonly State<string>[] _states;

		public ManyStatesCometView(int count)
		{
			_states = new State<string>[count];
			for (int i = 0; i < count; i++)
				_states[i] = new State<string>($"Initial {i}");

			Body = () =>
			{
				var children = new Comet.View[_states.Length];
				for (int i = 0; i < _states.Length; i++)
				{
					var idx = i;
					children[i] = new Text(() => _states[idx].Value);
				}
				return LayoutHelper.ToVStack(children);
			};
		}

		public void SetState(int index, string value)
			=> _states[index].Value = value;
	}

	public class ListChurnCometView : Comet.View
	{
		readonly State<int> _generation = new State<int>(0);
		readonly int _size;
		readonly List<string> _items;

		public ListChurnCometView(int size)
		{
			_size = size;
			_items = new List<string>();
			for (int i = 0; i < size; i++)
				_items.Add($"Item {i}");

			Body = () =>
			{
				// Read generation to track changes
				var _ = _generation.Value;
				var children = new Comet.View[_items.Count];
				for (int i = 0; i < _items.Count; i++)
					children[i] = new Text(_items[i]);
				return LayoutHelper.ToVStack(children);
			};
		}

		public void ChurnOne(int newIndex)
		{
			if (_items.Count > 0)
				_items.RemoveAt(0);
			_items.Add($"New {newIndex}");
			_generation.Value++;
		}
	}

	public class DeepConditionalView : Comet.View
	{
		readonly State<bool> _show = new State<bool>(false);
		readonly int _depth;

		public DeepConditionalView(int depth)
		{
			_depth = depth;
			Body = () =>
			{
				if (_show.Value)
				{
					return BuildDeep(0);
				}
				return new Text("Hidden");
			};
		}

		Comet.View BuildDeep(int level)
		{
			if (level >= _depth)
				return new Text($"Leaf at depth {level}");
			return new VStack
			{
				new Text($"Level {level}"),
				BuildDeep(level + 1)
			};
		}

		public void Toggle() => _show.Value = !_show.Value;
	}

	public class HiddenStateCometView : Comet.View
	{
		readonly State<bool> _showMain = new State<bool>(true);
		public readonly State<int> _hiddenCounter = new State<int>(0);

		public HiddenStateCometView(int size)
		{
			Body = () =>
			{
				if (_showMain.Value)
				{
					return new VStack
					{
						new Text("Main content visible"),
						new Text(() => $"Hidden counter: {_hiddenCounter.Value}")
					};
				}
				return new Text("Alternate");
			};
		}
	}

	public class TypeChangingView : Comet.View
	{
		readonly State<int> _viewType = new State<int>(0);

		public TypeChangingView(int unused)
		{
			Body = () =>
			{
				return (_viewType.Value % 3) switch
				{
					0 => (Comet.View)new VStack
					{
						new Text("Type A"),
						new Comet.Button("Action", null)
					},
					1 => new HStack
					{
						new Text("Type B"),
						new Toggle(false)
					},
					_ => new VStack
					{
						new Text("Type C"),
						new Comet.Slider(new Func<double>(() => 0.5), 0.0, 1.0)
					}
				};
			};
		}

		public void NextType() => _viewType.Value++;
	}
}
