using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Rapid-fire state changes simulating animation-like workloads.
	/// Uses [IterationSetup] to isolate update cost from construction cost.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class RapidUpdateBenchmarks
	{
		private Microsoft.Maui.Controls.Label _label;
		private RapidCounterCometView _counterView;
		private AnimationCometView _animView;
		private StringHeavyCometView _stringView;

		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		[Params(100, 1000, 5000)]
		public int Iterations;

		[IterationSetup]
		public void IterationSetup()
		{
			_label = new Microsoft.Maui.Controls.Label();
			_counterView = new RapidCounterCometView();
			BenchmarkUI.InitializeHandlers(_counterView);
			_animView = new AnimationCometView();
			BenchmarkUI.InitializeHandlers(_animView);
			_stringView = new StringHeavyCometView();
			BenchmarkUI.InitializeHandlers(_stringView);
		}

		// --- Counter thrashing ---

		[Benchmark(Description = "XAML: Rapid counter")]
		public void XamlRapidCounter()
		{
			for (int i = 0; i < Iterations; i++)
				_label.Text = i.ToString();
		}

		[Benchmark(Description = "MVU: Rapid counter")]
		public void MvuRapidCounter()
		{
			for (int i = 0; i < Iterations; i++)
				_counterView._value.Value = i;
		}

		// --- Multi-property animation ---

		[Benchmark(Description = "XAML: Multi-prop animation (4 props)")]
		public void XamlMultiPropAnimation()
		{
			for (int i = 0; i < Iterations; i++)
			{
				_label.TranslationX = i * 0.1;
				_label.TranslationY = i * 0.2;
				_label.Opacity = (i % 100) / 100.0;
				_label.Scale = 1.0 + (i % 50) * 0.01;
			}
		}

		[Benchmark(Description = "MVU: Multi-prop animation (unbatched)")]
		public void MvuMultiPropAnimation()
		{
			for (int i = 0; i < Iterations; i++)
			{
				_animView._x.Value = (float)(i * 0.1);
				_animView._y.Value = (float)(i * 0.2);
				_animView._opacity.Value = (float)((i % 100) / 100.0);
				_animView._scale.Value = (float)(1.0 + (i % 50) * 0.01);
			}
		}

		[Benchmark(Description = "MVU: Multi-prop animation (batched)")]
		public void MvuMultiPropAnimationBatched()
		{
			for (int i = 0; i < Iterations; i++)
			{
				StateManager.BeginBatch();
				_animView._x.Value = (float)(i * 0.1);
				_animView._y.Value = (float)(i * 0.2);
				_animView._opacity.Value = (float)((i % 100) / 100.0);
				_animView._scale.Value = (float)(1.0 + (i % 50) * 0.01);
				StateManager.EndBatch();
			}
		}

		// --- String-heavy updates ---

		[Benchmark(Description = "XAML: String-heavy updates")]
		public void XamlStringUpdates()
		{
			for (int i = 0; i < Iterations; i++)
				_label.Text = $"The quick brown fox jumps over the lazy dog — iteration {i} of {Iterations} with timestamp {DateTime.UtcNow.Ticks}";
		}

		[Benchmark(Description = "MVU: String-heavy updates")]
		public void MvuStringUpdates()
		{
			for (int i = 0; i < Iterations; i++)
				_stringView._content.Value = $"The quick brown fox jumps over the lazy dog — iteration {i} of {Iterations} with timestamp {DateTime.UtcNow.Ticks}";
		}
	}

	// --- Helper types ---

	public class RapidCounterCometView : Comet.View
	{
		public readonly State<int> _value = new State<int>(0);
		public RapidCounterCometView()
		{
			Body = () => new Text(() => $"Value: {_value.Value}");
		}
	}

	public class AnimationCometView : Comet.View
	{
		public readonly State<float> _x = new State<float>(0);
		public readonly State<float> _y = new State<float>(0);
		public readonly State<float> _opacity = new State<float>(1);
		public readonly State<float> _scale = new State<float>(1);

		public AnimationCometView()
		{
			Body = () => new Text(() => $"X:{_x.Value:F1} Y:{_y.Value:F1} O:{_opacity.Value:F2} S:{_scale.Value:F2}");
		}
	}

	public class StringHeavyCometView : Comet.View
	{
		public readonly State<string> _content = new State<string>("");
		public StringHeavyCometView()
		{
			Body = () => new Text(() => _content.Value);
		}
	}
}
