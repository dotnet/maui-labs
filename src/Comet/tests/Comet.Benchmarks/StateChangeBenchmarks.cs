using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Time from state mutation to view update completion.
	/// Uses [IterationSetup] to isolate update cost from construction cost.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class StateChangeBenchmarks
	{
		private Microsoft.Maui.Controls.Label _label;
		private SingleStateCometView _singleView;
		private MultiStateCometView _multiView;
		private VerticalStackLayout _stack;
		private Microsoft.Maui.Controls.Label[] _labels;
		private Microsoft.Maui.Controls.Label[] _selectiveLabels;
		private MultiStateCometView _selectiveView;

		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		[Params(1, 10, 50)]
		public int UpdateCount;

		[IterationSetup]
		public void IterationSetup()
		{
			_label = new Microsoft.Maui.Controls.Label();
			_singleView = new SingleStateCometView();
			BenchmarkUI.InitializeHandlers(_singleView);

			_multiView = new MultiStateCometView(UpdateCount);
			BenchmarkUI.InitializeHandlers(_multiView);

			_stack = new VerticalStackLayout();
			_labels = new Microsoft.Maui.Controls.Label[UpdateCount];
			for (int i = 0; i < UpdateCount; i++)
			{
				_labels[i] = new Microsoft.Maui.Controls.Label { Text = $"Value {i}" };
				_stack.Children.Add(_labels[i]);
			}

			_selectiveLabels = new Microsoft.Maui.Controls.Label[100];
			var selectiveStack = new VerticalStackLayout();
			for (int i = 0; i < 100; i++)
			{
				_selectiveLabels[i] = new Microsoft.Maui.Controls.Label { Text = $"Value {i}" };
				selectiveStack.Children.Add(_selectiveLabels[i]);
			}

			_selectiveView = new MultiStateCometView(100);
			BenchmarkUI.InitializeHandlers(_selectiveView);
		}

		// --- Single property change ---

		[Benchmark(Description = "XAML: Single property update")]
		public void XamlSinglePropertyChange()
		{
			for (int i = 0; i < UpdateCount; i++)
				_label.Text = i.ToString();
		}

		[Benchmark(Description = "MVU: Single state update")]
		public void MvuSingleStateChange()
		{
			for (int i = 0; i < UpdateCount; i++)
				_singleView._counter.Value = i;
		}

		// --- Multiple independent property changes ---

		[Benchmark(Description = "XAML: N independent property changes")]
		public void XamlMultiPropertyChange()
		{
			for (int i = 0; i < UpdateCount; i++)
				_labels[i].Text = $"Updated {i}";
		}

		[Benchmark(Description = "MVU: N independent state changes")]
		public void MvuMultiStateChange()
		{
			for (int i = 0; i < UpdateCount; i++)
				_multiView.SetValue(i, $"Updated {i}");
		}

		// --- Change with no visual effect (same value) ---

		[Benchmark(Description = "XAML: No-op property change (same value)")]
		public void XamlNoOpChange()
		{
			_label.Text = "42";
			for (int i = 0; i < UpdateCount; i++)
				_label.Text = "42";
		}

		[Benchmark(Description = "MVU: No-op state change (same value)")]
		public void MvuNoOpChange()
		{
			_singleView._counter.Value = 42;
			for (int i = 0; i < UpdateCount; i++)
				_singleView._counter.Value = 42;
		}

		// --- Selective: change 1 of 100 ---

		[Benchmark(Description = "XAML: Change 1 of 100 properties")]
		public void XamlSelectiveUpdate()
		{
			for (int iter = 0; iter < UpdateCount; iter++)
				_selectiveLabels[0].Text = $"Changed {iter}";
		}

		[Benchmark(Description = "MVU: Change 1 of 100 states")]
		public void MvuSelectiveUpdate()
		{
			for (int iter = 0; iter < UpdateCount; iter++)
				_selectiveView.SetValue(0, $"Changed {iter}");
		}
	}

	// --- Helper types ---

	public class SingleStateCometView : Comet.View
	{
		public readonly State<int> _counter = new State<int>(0);

		public SingleStateCometView()
		{
			Body = () => new Text(() => $"Count: {_counter.Value}");
		}
	}

	public class MultiStateCometView : Comet.View
	{
		readonly State<string>[] _states;

		public MultiStateCometView(int count)
		{
			_states = new State<string>[count];
			for (int i = 0; i < count; i++)
				_states[i] = new State<string>($"Value {i}");

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

		public void SetValue(int index, string value)
			=> _states[index].Value = value;
	}
}
