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
	/// Benchmarks: Real-world composite scenarios — Todo app, form validation,
	/// master-detail navigation patterns.
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(warmupCount: 3, iterationCount: 10)]
	public class RealWorldBenchmarks
	{
		[GlobalSetup]
		public void Setup() => BenchmarkUI.Init();

		[Params(10, 50, 100)]
		public int ItemCount;

		// --- Todo app: build list + add/remove items ---

		[Benchmark(Description = "XAML: Todo list build + mutations")]
		public void XamlTodoApp()
		{
			var stack = new VerticalStackLayout();
			var checkboxes = new Microsoft.Maui.Controls.CheckBox[ItemCount];
			var labels = new Microsoft.Maui.Controls.Label[ItemCount];

			for (int i = 0; i < ItemCount; i++)
			{
				var row = new HorizontalStackLayout();
				checkboxes[i] = new Microsoft.Maui.Controls.CheckBox { IsChecked = i % 3 == 0 };
				labels[i] = new Microsoft.Maui.Controls.Label { Text = $"Task {i}" };
				row.Children.Add(checkboxes[i]);
				row.Children.Add(labels[i]);
				stack.Children.Add(row);
			}

			// Toggle half the items
			for (int i = 0; i < ItemCount / 2; i++)
				checkboxes[i].IsChecked = !checkboxes[i].IsChecked;
		}

		[Benchmark(Description = "MVU: Todo list build + mutations")]
		public void MvuTodoApp()
		{
			var view = new TodoCometView(ItemCount);
			BenchmarkUI.InitializeHandlers(view);

			// Toggle half the items
			for (int i = 0; i < ItemCount / 2; i++)
				view.ToggleItem(i);
		}

		// --- Form with validation: build form + validate all fields ---

		[Benchmark(Description = "XAML: Form build + validation")]
		public void XamlFormValidation()
		{
			var stack = new VerticalStackLayout();
			var entries = new Microsoft.Maui.Controls.Entry[ItemCount];
			var errorLabels = new Microsoft.Maui.Controls.Label[ItemCount];

			for (int i = 0; i < ItemCount; i++)
			{
				entries[i] = new Microsoft.Maui.Controls.Entry { Placeholder = $"Field {i}" };
				errorLabels[i] = new Microsoft.Maui.Controls.Label { IsVisible = false };
				stack.Children.Add(entries[i]);
				stack.Children.Add(errorLabels[i]);
			}

			// Validate: set values and show/hide errors
			for (int i = 0; i < ItemCount; i++)
			{
				var value = i % 2 == 0 ? "" : $"Valid value {i}";
				entries[i].Text = value;
				bool hasError = string.IsNullOrWhiteSpace(value);
				errorLabels[i].Text = hasError ? "Required" : "";
				errorLabels[i].IsVisible = hasError;
			}
		}

		[Benchmark(Description = "MVU: Form build + validation")]
		public void MvuFormValidation()
		{
			var view = new FormCometView(ItemCount);
			BenchmarkUI.InitializeHandlers(view);

			// Validate
			for (int i = 0; i < ItemCount; i++)
				view.SetField(i, i % 2 == 0 ? "" : $"Valid value {i}");
		}

		// --- Dashboard: multiple data sections with mixed controls ---

		[Benchmark(Description = "XAML: Dashboard build")]
		public void XamlDashboard()
		{
			var stack = new VerticalStackLayout();

			// Stats section
			var statsSection = new VerticalStackLayout();
			for (int i = 0; i < ItemCount; i++)
			{
				var row = new HorizontalStackLayout();
				row.Children.Add(new Microsoft.Maui.Controls.Label { Text = $"Metric {i}" });
				row.Children.Add(new Microsoft.Maui.Controls.Label { Text = $"{i * 42.5:F1}" });
				var progress = new Microsoft.Maui.Controls.ProgressBar { Progress = i / (double)ItemCount };
				row.Children.Add(progress);
				statsSection.Children.Add(row);
			}
			stack.Children.Add(statsSection);

			// Action buttons
			var buttonSection = new HorizontalStackLayout();
			for (int i = 0; i < Math.Min(ItemCount, 10); i++)
				buttonSection.Children.Add(new Microsoft.Maui.Controls.Button { Text = $"Action {i}" });
			stack.Children.Add(buttonSection);
		}

		[Benchmark(Description = "MVU: Dashboard build")]
		public void MvuDashboard()
		{
			var view = new DashboardCometView(ItemCount);
			BenchmarkUI.InitializeHandlers(view);
		}
	}

	// --- Todo helpers ---

	public class TodoVM
	{
		public ObservableCollection<TodoItemVM> Items { get; } = new();
	}

	public class TodoItemVM : INotifyPropertyChanged
	{
		string _title = "";
		bool _isDone;
		public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
		public bool IsDone { get => _isDone; set { _isDone = value; OnPropertyChanged(); } }
		public event PropertyChangedEventHandler? PropertyChanged;
		void OnPropertyChanged([CallerMemberName] string? n = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}

	public class TodoCometView : Comet.View
	{
		readonly State<bool>[] _done;
		readonly string[] _titles;

		public TodoCometView(int count)
		{
			_done = new State<bool>[count];
			_titles = new string[count];
			for (int i = 0; i < count; i++)
			{
				_done[i] = new State<bool>(i % 3 == 0);
				_titles[i] = $"Task {i}";
			}

			Body = () =>
			{
				var children = new Comet.View[_done.Length];
				for (int i = 0; i < _done.Length; i++)
				{
					var idx = i;
					children[i] = new HStack
					{
						new Toggle(() => _done[idx].Value),
						new Text(() => _titles[idx])
					};
				}
				return LayoutHelper.ToVStack(children);
			};
		}

		public void ToggleItem(int index)
			=> _done[index].Value = !_done[index].Value;
	}

	// --- Form helpers ---

	public class FormField : INotifyPropertyChanged
	{
		string _value = "", _error = "";
		public string Value { get => _value; set { _value = value; OnPropertyChanged(); Validate(); } }
		public string Error { get => _error; set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
		public bool HasError => !string.IsNullOrEmpty(_error);

		public void Validate() => Error = string.IsNullOrWhiteSpace(Value) ? "Required" : "";

		public event PropertyChangedEventHandler? PropertyChanged;
		void OnPropertyChanged([CallerMemberName] string? n = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
	}

	public class FormVM
	{
		public FormField[] Fields { get; }
		public FormVM(int count)
		{
			Fields = new FormField[count];
			for (int i = 0; i < count; i++)
				Fields[i] = new FormField();
		}
	}

	public class FormCometView : Comet.View
	{
		readonly State<string>[] _values;
		readonly State<string>[] _errors;

		public FormCometView(int count)
		{
			_values = new State<string>[count];
			_errors = new State<string>[count];
			for (int i = 0; i < count; i++)
			{
				_values[i] = new State<string>("");
				_errors[i] = new State<string>("");
			}

			Body = () =>
			{
				var children = new List<Comet.View>();
				for (int i = 0; i < _values.Length; i++)
				{
					var idx = i;
					children.Add(new TextField((Func<string>)(() => _values[idx].Value), $"Field {idx}"));
					if (!string.IsNullOrEmpty(_errors[idx].Value))
						children.Add(new Text(() => _errors[idx].Value));
				}
				return LayoutHelper.ToVStack(children.ToArray());
			};
		}

		public void SetField(int index, string value)
		{
			_values[index].Value = value;
			_errors[index].Value = string.IsNullOrWhiteSpace(value) ? "Required" : "";
		}
	}

	// --- Dashboard helpers ---

	public class DashboardCometView : Comet.View
	{
		readonly int _count;
		public DashboardCometView(int count)
		{
			_count = count;
			Body = BuildBody;
		}

		Comet.View BuildBody()
		{
			var sections = new List<Comet.View>();

			// Stats section
			for (int i = 0; i < _count; i++)
			{
				sections.Add(new HStack
				{
					new Text($"Metric {i}"),
					new Text($"{i * 42.5:F1}"),
					new ProgressBar((double)(i / (float)_count))
				});
			}

			// Action buttons
			var buttons = new List<Comet.View>();
			for (int i = 0; i < Math.Min(_count, 10); i++)
				buttons.Add(new Comet.Button($"Action {i}", null));

			sections.Add(LayoutHelper.ToHStack(buttons.ToArray()));

			return LayoutHelper.ToVStack(sections.ToArray());
		}
	}
}
