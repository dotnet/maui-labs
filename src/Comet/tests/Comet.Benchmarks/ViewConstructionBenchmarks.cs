using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Time to construct a view hierarchy from scratch.
	/// XAML (programmatic): Create MAUI controls + wire bindings
	/// MVU (Comet): Evaluate Body() + set up StateManager tracking
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class ViewConstructionBenchmarks
	{
		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		// --- Flat view: N labels in a stack ---

		[Params(10, 50, 100, 500)]
		public int N;

		[Benchmark(Description = "XAML: Flat StackLayout + Labels")]
		public object XamlFlatConstruction()
		{
			var vm = new FlatViewModel(N);
			var stack = new VerticalStackLayout();
			for (int i = 0; i < N; i++)
			{
				var label = new Microsoft.Maui.Controls.Label();
				label.SetBinding(Microsoft.Maui.Controls.Label.TextProperty,
					new Microsoft.Maui.Controls.Binding($"Items[{i}]"));
				label.BindingContext = vm;
				stack.Children.Add(label);
			}
			return stack;
		}

		[Benchmark(Description = "MVU: Flat VStack + Text")]
		public object MvuFlatConstruction()
		{
			var view = new FlatCometView(N);
			BenchmarkUI.InitializeHandlers(view);
			return view;
		}

		// --- Deep nested view ---

		[Benchmark(Description = "XAML: Deep nested layouts")]
		public object XamlDeepConstruction()
		{
			int depth = Math.Min(N, 50); // Cap depth
			Microsoft.Maui.Controls.View current = new Microsoft.Maui.Controls.Label { Text = "Leaf" };
			for (int i = 0; i < depth; i++)
			{
				var border = new Microsoft.Maui.Controls.Border { Content = current };
				var hsl = new HorizontalStackLayout();
				hsl.Children.Add(border);
				var vsl = new VerticalStackLayout();
				vsl.Children.Add(hsl);
				current = vsl;
			}
			return current;
		}

		[Benchmark(Description = "MVU: Deep nested views")]
		public object MvuDeepConstruction()
		{
			int depth = Math.Min(N, 50);
			var view = new DeepCometView(depth);
			BenchmarkUI.InitializeHandlers(view);
			return view;
		}

		// --- Mixed controls (form page) ---

		[Benchmark(Description = "XAML: Mixed form controls")]
		public object XamlMixedConstruction()
		{
			var stack = new VerticalStackLayout();
			for (int i = 0; i < N; i++)
			{
				switch (i % 5)
				{
					case 0: stack.Children.Add(new Microsoft.Maui.Controls.Label { Text = $"Label {i}" }); break;
					case 1: stack.Children.Add(new Microsoft.Maui.Controls.Entry { Placeholder = $"Entry {i}" }); break;
					case 2: stack.Children.Add(new Microsoft.Maui.Controls.Button { Text = $"Button {i}" }); break;
					case 3: stack.Children.Add(new Microsoft.Maui.Controls.Switch()); break;
					case 4: stack.Children.Add(new Microsoft.Maui.Controls.Slider { Minimum = 0, Maximum = 100 }); break;
				}
			}
			return stack;
		}

		[Benchmark(Description = "MVU: Mixed form controls")]
		public object MvuMixedConstruction()
		{
			var view = new MixedCometView(N);
			BenchmarkUI.InitializeHandlers(view);
			return view;
		}
	}

	// --- Helper types ---

	public class FlatViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public string[] Items { get; }

		public FlatViewModel(int count)
		{
			Items = new string[count];
			for (int i = 0; i < count; i++)
				Items[i] = $"Item {i}";
		}
	}

	public class FlatCometView : Comet.View
	{
		readonly int _count;
		public FlatCometView(int count)
		{
			_count = count;
			Body = BuildBody;
		}

		Comet.View BuildBody()
		{
			var children = new Comet.View[_count];
			for (int i = 0; i < _count; i++)
				children[i] = new Text($"Item {i}");
			return LayoutHelper.ToVStack(children);
		}
	}

	public class DeepCometView : Comet.View
	{
		readonly int _depth;
		public DeepCometView(int depth)
		{
			_depth = depth;
			Body = BuildBody;
		}

		Comet.View BuildBody() => BuildLevel(0);

		Comet.View BuildLevel(int level)
		{
			if (level >= _depth)
				return new Text("Leaf");
			return new VStack { new HStack { BuildLevel(level + 1) } };
		}
	}

	public class MixedCometView : Comet.View
	{
		readonly int _count;
		public MixedCometView(int count)
		{
			_count = count;
			Body = BuildBody;
		}

		Comet.View BuildBody()
		{
			var children = new Comet.View[_count];
			for (int i = 0; i < _count; i++)
			{
				children[i] = (i % 5) switch
				{
					0 => new Text($"Label {i}"),
					1 => new TextField("", $"Entry {i}"),
					2 => new Comet.Button($"Button {i}", null),
					3 => new Toggle(false),
					4 => new Comet.Slider(new System.Func<double>(() => 50.0), 0.0, 100.0),
					_ => new Text("?")
				};
			}
			return LayoutHelper.ToVStack(children);
		}
	}
}
