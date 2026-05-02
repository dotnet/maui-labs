using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Benchmarks: Memory allocation patterns and GC pressure.
	/// Measures bytes allocated and GC collections during view operations.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class MemoryBenchmarks
	{
		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		// --- Allocation per state change ---

		[Params(10, 100, 1000)]
		public int Iterations;

		[Benchmark(Description = "XAML: Alloc per property change")]
		public void XamlAllocPerChange()
		{
			var label = new Microsoft.Maui.Controls.Label();

			for (int i = 0; i < Iterations; i++)
				label.Text = $"Value {i}";
		}

		[Benchmark(Description = "MVU: Alloc per state change (body rebuild)")]
		public void MvuAllocPerChange()
		{
			var view = new MemCounterCometView();
			BenchmarkUI.InitializeHandlers(view);

			for (int i = 0; i < Iterations; i++)
				view._text.Value = $"Value {i}";
		}

		// --- View tree rebuild allocation (large tree) ---

		[Benchmark(Description = "MVU: Alloc for 100-node tree rebuild")]
		public void MvuTreeRebuildAlloc()
		{
			var view = new LargeTreeCometView(100);
			BenchmarkUI.InitializeHandlers(view);

			for (int i = 0; i < Iterations; i++)
				view._trigger.Value = i;
		}

		// --- Startup allocation: fresh view construction ---

		[Benchmark(Description = "XAML: Startup alloc (50-control page)")]
		public void XamlStartupAlloc()
		{
			for (int iter = 0; iter < Iterations; iter++)
			{
				var stack = new VerticalStackLayout();
				for (int i = 0; i < 50; i++)
				{
					var label = new Microsoft.Maui.Controls.Label { Text = $"Label {i}" };
					stack.Children.Add(label);
				}
			}
		}

		[Benchmark(Description = "MVU: Startup alloc (50-control view)")]
		public void MvuStartupAlloc()
		{
			for (int iter = 0; iter < Iterations; iter++)
			{
				var view = new FlatCometView(50);
				BenchmarkUI.InitializeHandlers(view);
			}
		}

		// --- Cascading state: one change triggers multiple derived updates ---

		[Benchmark(Description = "XAML: Cascading computed properties")]
		public void XamlCascadingUpdate()
		{
			var l1 = new Microsoft.Maui.Controls.Label();
			var l2 = new Microsoft.Maui.Controls.Label();
			var l3 = new Microsoft.Maui.Controls.Label();

			for (int i = 0; i < Iterations; i++)
			{
				var first = $"John{i}";
				var last = $"Doe{i}";
				var fullName = $"{first} {last}";
				l1.Text = fullName;
				l2.Text = $"Hello, {fullName}!";
				l3.Text = $"Hello, {fullName}! ({fullName.Length} chars)";
			}
		}

		[Benchmark(Description = "MVU: Cascading derived state")]
		public void MvuCascadingUpdate()
		{
			var view = new CascadeCometView();
			BenchmarkUI.InitializeHandlers(view);

			for (int i = 0; i < Iterations; i++)
			{
				view._firstName.Value = $"John{i}";
				view._lastName.Value = $"Doe{i}";
			}
		}
	}

	// --- Helper types ---

	public class MemCounterVM : INotifyPropertyChanged
	{
		string _text = "";
		public string Text
		{
			get => _text;
			set { _text = value; OnPropertyChanged(); }
		}
		public event PropertyChangedEventHandler? PropertyChanged;
		void OnPropertyChanged([CallerMemberName] string? n = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}

	public class MemCounterCometView : Comet.View
	{
		public readonly State<string> _text = new State<string>("");
		public MemCounterCometView()
		{
			Body = () => new Text(() => _text.Value);
		}
	}

	public class LargeTreeCometView : Comet.View
	{
		readonly int _size;
		public readonly State<int> _trigger = new State<int>(0);

		public LargeTreeCometView(int size)
		{
			_size = size;
			Body = () =>
			{
				var children = new Comet.View[_size];
				for (int i = 0; i < _size; i++)
					children[i] = new Text($"Item {i} gen {_trigger.Value}");
				return LayoutHelper.ToVStack(children);
			};
		}
	}

	public class CascadeVM : INotifyPropertyChanged
	{
		string _first = "", _last = "";
		public string FirstName
		{
			get => _first;
			set
			{
				_first = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(FullName));
				OnPropertyChanged(nameof(Greeting));
				OnPropertyChanged(nameof(DisplayInfo));
			}
		}
		public string LastName
		{
			get => _last;
			set
			{
				_last = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(FullName));
				OnPropertyChanged(nameof(Greeting));
				OnPropertyChanged(nameof(DisplayInfo));
			}
		}
		public string FullName => $"{_first} {_last}";
		public string Greeting => $"Hello, {FullName}!";
		public string DisplayInfo => $"{Greeting} ({FullName.Length} chars)";

		public event PropertyChangedEventHandler? PropertyChanged;
		void OnPropertyChanged([CallerMemberName] string? n = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}

	public class CascadeCometView : Comet.View
	{
		public readonly State<string> _firstName = new State<string>("");
		public readonly State<string> _lastName = new State<string>("");

		public CascadeCometView()
		{
			Body = () =>
			{
				var fullName = $"{_firstName.Value} {_lastName.Value}";
				var greeting = $"Hello, {fullName}!";
				var info = $"{greeting} ({fullName.Length} chars)";
				return new VStack
				{
					new Text(fullName),
					new Text(greeting),
					new Text(info)
				};
			};
		}
	}
}
