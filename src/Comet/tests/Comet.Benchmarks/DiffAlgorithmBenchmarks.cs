using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Cost of Comet's tree diff/reconciliation algorithm.
	/// MVU-only — XAML has no equivalent (uses targeted binding updates).
	/// Measures DiffUpdate performance for various tree shapes and change patterns.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class DiffAlgorithmBenchmarks
	{
		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		[Params(10, 50, 200, 1000)]
		public int TreeSize;

		// --- Identical trees: best-case diff (nothing changed) ---

		[Benchmark(Description = "Diff: Identical trees (no change)")]
		public void DiffIdenticalTrees()
		{
			var view = new DiffTestView(TreeSize, changeIndex: -1);
			BenchmarkUI.InitializeHandlers(view);
			// Force rebuild — Body returns same structure
			view.TriggerRebuild();
		}

		// --- Single node changed: find 1 difference in N nodes ---

		[Benchmark(Description = "Diff: Single node changed in N")]
		public void DiffSingleChange()
		{
			var view = new DiffTestView(TreeSize, changeIndex: TreeSize / 2);
			BenchmarkUI.InitializeHandlers(view);
			view.TriggerRebuild();
		}

		// --- All nodes changed: worst-case diff ---

		[Benchmark(Description = "Diff: All nodes changed")]
		public void DiffAllChanged()
		{
			var view = new DiffTestView(TreeSize, changeIndex: -2); // -2 = all change
			BenchmarkUI.InitializeHandlers(view);
			view.TriggerRebuild();
		}

		// --- Append node to end ---

		[Benchmark(Description = "Diff: Append node to list")]
		public void DiffAppendNode()
		{
			var view = new GrowingListView(TreeSize);
			BenchmarkUI.InitializeHandlers(view);
			view.AddItem();
		}

		// --- Remove node from middle ---

		[Benchmark(Description = "Diff: Remove node from middle")]
		public void DiffRemoveNode()
		{
			var view = new ShrinkingListView(TreeSize);
			BenchmarkUI.InitializeHandlers(view);
			view.RemoveMiddle();
		}

		// --- Conditional rendering: toggle subtree visibility ---

		[Benchmark(Description = "Diff: Toggle subtree (show/hide)")]
		public void DiffToggleSubtree()
		{
			var view = new ConditionalView(TreeSize);
			BenchmarkUI.InitializeHandlers(view);
			// Toggle on
			view.Toggle();
			// Toggle off
			view.Toggle();
		}
	}

	// --- Helper views ---

	public class DiffTestView : Comet.View
	{
		readonly int _size;
		readonly int _changeIndex;
		int _generation;

		public DiffTestView(int size, int changeIndex)
		{
			_size = size;
			_changeIndex = changeIndex;
			Body = BuildBody;
		}

		Comet.View BuildBody()
		{
			var children = new Comet.View[_size];
			for (int i = 0; i < _size; i++)
			{
				bool shouldChange = _changeIndex == -2 || (_changeIndex >= 0 && i == _changeIndex);
				string text = shouldChange ? $"Item {i} gen {_generation}" : $"Item {i}";
				children[i] = new Text(text);
			}
			return LayoutHelper.ToVStack(children);
		}

		public void TriggerRebuild()
		{
			_generation++;
			this.Reload();
		}
	}

	public class GrowingListView : Comet.View
	{
		readonly State<int> _count;

		public GrowingListView(int initial)
		{
			_count = new State<int>(initial);
			Body = () =>
			{
				var children = new Comet.View[_count.Value];
				for (int i = 0; i < _count.Value; i++)
					children[i] = new Text($"Item {i}");
				return LayoutHelper.ToVStack(children);
			};
		}

		public void AddItem() => _count.Value++;
	}

	public class ShrinkingListView : Comet.View
	{
		readonly State<int> _count;
		int _removeIndex;

		public ShrinkingListView(int initial)
		{
			_count = new State<int>(initial);
			_removeIndex = initial / 2;
			Body = () =>
			{
				var children = new List<Comet.View>();
				for (int i = 0; i < _count.Value + 1; i++)
				{
					if (i == _removeIndex && _count.Value < _count.Value + 1) continue;
					children.Add(new Text($"Item {i}"));
				}
				return LayoutHelper.ToVStack(children.ToArray());
			};
		}

		public void RemoveMiddle() => _count.Value--;
	}

	public class ConditionalView : Comet.View
	{
		readonly State<bool> _showSubtree = new State<bool>(false);
		readonly int _subtreeSize;

		public ConditionalView(int subtreeSize)
		{
			_subtreeSize = subtreeSize;
			Body = () =>
			{
				if (_showSubtree.Value)
				{
					var children = new Comet.View[_subtreeSize + 1];
					children[0] = new Text("Header");
					for (int i = 0; i < _subtreeSize; i++)
						children[i + 1] = new Text($"Detail {i}");
					return LayoutHelper.ToVStack(children);
				}
				return new VStack { new Text("Collapsed") };
			};
		}

		public void Toggle() => _showSubtree.Value = !_showSubtree.Value;
	}
}
