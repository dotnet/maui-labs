using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class StateManagementTests : TestBase
	{
		// Test that Reactive<T> properly notifies on value change
		[Fact]
		public void ReactiveNotifiesOnValueChange()
		{
			var state = new Reactive<int>(0);
			int notifyCount = 0;

			state.ValueChanged = (val) => notifyCount++;
			state.Value = 1;

			Assert.Equal(1, state.Value);
			Assert.True(notifyCount > 0);
		}

		// Test that Reactive<T> implicit conversions work
		[Fact]
		public void ReactiveImplicitConversions()
		{
			Reactive<int> state = 42;
			int value = state;

			Assert.Equal(42, value);
		}

		// Test that view rebuilds when state changes
		[Fact]
		public void ViewRebuildsOnStateChange()
		{
			int buildCount = 0;
			var view = new TestStateView();

			view.Body = () =>
			{
				buildCount++;
				return new Text($"Count: {view.count.Value}");
			};

			view.SetViewHandlerToGeneric();
			var initialCount = buildCount;

			view.count.Value++;
			Assert.True(buildCount > initialCount);
		}

		// Test nested state binding with model
		[Fact]
		public void NestedStateBindingWorks()
		{
			var view = new TestNestedView();
			Text nameText = null;
			Text ageText = null;

			view.Body = () => new VStack
			{
				(nameText = new Text(() => $"Name: {view.person.Value.Name}")),
				(ageText = new Text(() => $"Age: {view.person.Value.Age}"))
			};

			view.SetViewHandlerToGeneric();
			Assert.NotNull(view.BuiltView);
		}

		// Test multiple state properties in one view
		[Fact]
		public void MultipleStatesInOneView()
		{
			var view = new TestMultiStateView();
			Text firstText = null;
			Text lastText = null;
			Text fullText = null;

			view.Body = () => new VStack
			{
				(firstText = new Text(() => view.firstName.Value)),
				(lastText = new Text(() => view.lastName.Value)),
				(fullText = new Text(() => $"{view.firstName.Value} {view.lastName.Value}"))
			};

			view.SetViewHandlerToGeneric();

			view.firstName.Value = "John";
			view.lastName.Value = "Doe";

			Assert.Equal("John", view.firstName.Value);
			Assert.Equal("Doe", view.lastName.Value);
		}

		// Test that disposing a view does not throw on subsequent state access
		[Fact]
		public void DisposingViewDoesNotThrowOnStateAccess()
		{
			var view = new TestStateView();
			view.Body = () => new Text($"Count: {view.count.Value}");
			view.SetViewHandlerToGeneric();

			view.Dispose();
			// Should not throw after disposal
			var ex = Record.Exception(() => view.count.Value = 99);
			Assert.Null(ex);
		}

		// Test conditional view rendering based on state
		[Fact]
		public void ConditionalViewRendering()
		{
			var view = new TestConditionalView();
			view.Body = () =>
			{
				if (view.showDetails.Value)
					return new VStack
					{
						new Text("Details visible"),
						new Text(() => view.detail.Value)
					};
				return new Text("Hidden");
			};

			view.SetViewHandlerToGeneric();
			Assert.NotNull(view.BuiltView);

			// Initially showDetails is false, so we get a Text
			Assert.IsType<Text>(view.BuiltView);

			view.showDetails.Value = true;
			// After changing, we get a VStack
			Assert.IsType<VStack>(view.BuiltView);
		}

		// Test Reactive<T> PropertyRead fires on read
		[Fact]
		public void ReactiveNotifiesPropertyRead()
		{
			var reactive = new Reactive<string>("test");
			bool notified = false;

			reactive.PropertyRead += (sender, args) =>
			{
				notified = true;
			};

			var _ = reactive.Value;
			Assert.True(notified);
		}

		// Test Reactive<T> with null value
		[Fact]
		public void ReactiveHandlesNullValue()
		{
			var state = new Reactive<string>(null);
			Assert.Null(state.Value);

			state.Value = "hello";
			Assert.Equal("hello", state.Value);

			state.Value = null;
			Assert.Null(state.Value);
		}

		// Test Reactive<T> with collection types
		[Fact]
		public void ReactiveWithCollectionType()
		{
			var state = new Reactive<List<string>>(new List<string> { "a", "b", "c" });
			Assert.Equal(3, state.Value.Count);

			state.Value.Add("d");
			Assert.Equal(4, state.Value.Count);
		}

		// Test that formatted text binding updates value without full rebuild
		[Fact]
		public void FormattedTextBindingUpdatesWithoutRebuild()
		{
			Text text = null;
			var view = new TestStateView();
			int buildCount = 0;
			int textBuildCount = 0;

			view.Body = () =>
			{
				buildCount++;
				text = new Text(() =>
				{
					textBuildCount++;
					return $"Count: {view.count.Value}";
				});
				return text;
			};

			view.SetViewHandlerToGeneric();
			text.SetViewHandlerToGeneric();

			Assert.Equal(1, buildCount);
			Assert.Equal(1, textBuildCount);

			view.count.Value = 5;

			// Formatted binding should update text without full body rebuild
			Assert.Equal(1, buildCount);
			Assert.Equal(2, textBuildCount);
		}

		// Test Reactive<T> ToString returns value string
		[Fact]
		public void ReactiveToStringReturnsValueString()
		{
			var state = new Reactive<int>(42);
			Assert.Equal("42", state.ToString());

			var strState = new Reactive<string>("hello");
			Assert.Equal("hello", strState.ToString());
		}

		// Test Reactive<T> PropertyChanged fires on set
		[Fact]
		public void ReactivePropertySetTriggersPropertyChanged()
		{
			var reactive = new Reactive<string>();
			string changedProp = null;

			reactive.PropertyChanged += (sender, args) =>
			{
				changedProp = args.PropertyName;
			};

			reactive.Value = "NewName";
			Assert.Equal("Value", changedProp);
		}

		// Helper classes
		class TestStateView : View
		{
			public readonly Reactive<int> count = new Reactive<int>(0);
		}

		class TestNestedView : View
		{
			public readonly Reactive<PersonModel> person = new Reactive<PersonModel>(new PersonModel { Name = "Test", Age = 25 });
		}

		class TestMultiStateView : View
		{
			public readonly Reactive<string> firstName = new Reactive<string>("Jane");
			public readonly Reactive<string> lastName = new Reactive<string>("Smith");
		}

		class TestConditionalView : View
		{
			public readonly Reactive<bool> showDetails = new Reactive<bool>(false);
			public readonly Reactive<string> detail = new Reactive<string>("Detail text");
		}

		class PersonModel
		{
			public string Name { get; set; }
			public int Age { get; set; }
		}

		// Threading tests for Reactive<T>

		[Fact]
		public void ConcurrentReactiveChangesDoNotThrow()
		{
			var reactive = new Reactive<string>();
			var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

			var tasks = new List<System.Threading.Tasks.Task>();
			for (int t = 0; t < 10; t++)
			{
				int threadId = t;
				tasks.Add(System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						for (int i = 0; i < 100; i++)
						{
							reactive.Value = $"Thread{threadId}_Iteration{i}";
						}
					}
					catch (Exception ex)
					{
						exceptions.Add(ex);
					}
				}));
			}

			System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
			Assert.Empty(exceptions);
		}

		[Fact]
		public void ReactiveValueCanBeReadFromMultipleThreads()
		{
			var state = new Reactive<int>(0);
			var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
			var values = new System.Collections.Concurrent.ConcurrentBag<int>();

			var tasks = new List<System.Threading.Tasks.Task>();
			for (int t = 0; t < 10; t++)
			{
				tasks.Add(System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						for (int i = 0; i < 100; i++)
						{
							state.Value = i;
							values.Add(state.Value);
						}
					}
					catch (Exception ex)
					{
						exceptions.Add(ex);
					}
				}));
			}

			System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
			Assert.Empty(exceptions);
			Assert.Equal(1000, values.Count);
		}

		[Fact]
		public void DisposeWhileChangingDoesNotThrow()
		{
			var reactive = new Reactive<string>();
			var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

			var tasks = new List<System.Threading.Tasks.Task>();

			// Thread 1: rapidly change value
			tasks.Add(System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					for (int i = 0; i < 200; i++)
						reactive.Value = $"Value{i}";
				}
				catch (Exception ex)
				{
					exceptions.Add(ex);
				}
			}));

			// Thread 2: create views that read the reactive, then dispose
			tasks.Add(System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					for (int i = 0; i < 50; i++)
					{
						var view = new View();
						view.Body = () => new Text(() => $"Name: {reactive.Value}");
						var _ = view.Body?.Invoke();
						view.Dispose();
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(ex);
				}
			}));

			System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
			Assert.Empty(exceptions);
		}

		class CounterView : View
		{
			[State] readonly Reactive<int> count = new Reactive<int>(0);

			[Body]
			View body() => new Text(() => $"Count: {count.Value}");
		}
	}
}
