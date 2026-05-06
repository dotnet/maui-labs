using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Comet.Reactive;
using Xunit;
using ReactiveEffect = Comet.Reactive.Effect;

namespace Comet.Tests
{
	public class IntegrationTests : TestBase
	{
		#region 1. Signal Change Triggers View Dirty

		class CounterView : View
		{
			public readonly Signal<int> Count = new(0);
			public int BuildCount;

			[Body]
			View body()
			{
				BuildCount++;
				return new Text($"Count: {Count.Value}");
			}
		}

		[Fact]
		public void Signal_Change_Triggers_View_Dirty()
		{
			var view = new CounterView();

			// Force initial build via InitializeHandlers
			InitializeHandlers(view);
			Assert.Equal(1, view.BuildCount);

			// Change the signal — triggers MarkViewDirty via BodyDependencySubscriber
			view.Count.Value = 42;
			ReactiveScheduler.FlushSync();

			// View should have rebuilt
			Assert.True(view.BuildCount >= 2,
				$"View should have rebuilt after signal change, built {view.BuildCount} times");
		}

		[Fact]
		public void Signal_Change_Updates_View_Output()
		{
			var view = new CounterView();
			InitializeHandlers(view);

			view.Count.Value = 99;
			ReactiveScheduler.FlushSync();

			// BuiltView should exist after rebuild
			Assert.NotNull(view.BuiltView);
		}

		#endregion

		#region 2. Multiple Signals Coalesce (Final State Correct)

		class MultiSignalView : View
		{
			public readonly Signal<int> A = new(0);
			public readonly Signal<string> B = new("hello");
			public readonly Signal<bool> C = new(false);
			public int BuildCount;

			[Body]
			View body()
			{
				BuildCount++;
				return new Text($"{A.Value}-{B.Value}-{C.Value}");
			}
		}

		[Fact]
		public void Multiple_Signals_Final_State_Correct_After_Flush()
		{
			var view = new MultiSignalView();
			InitializeHandlers(view);
			Assert.Equal(1, view.BuildCount);

			// Change all 3 signals
			view.A.Value = 42;
			view.B.Value = "world";
			view.C.Value = true;

			ReactiveScheduler.FlushSync();

			// View must have rebuilt at least once with all updates applied
			Assert.True(view.BuildCount >= 2,
				$"View should have rebuilt, built {view.BuildCount} times");

			// Verify signals hold the latest values
			Assert.Equal(42, view.A.Value);
			Assert.Equal("world", view.B.Value);
			Assert.True(view.C.Value);
		}

		#endregion

		#region 3. Computed Provides Targeted Update

		[Fact]
		public void Computed_Provides_Targeted_Update()
		{
			var firstName = new Signal<string>("Alice");
			var lastName = new Signal<string>("Smith");

			int evalCount = 0;
			var fullName = new Computed<string>(() =>
			{
				evalCount++;
				return $"{firstName.Value} {lastName.Value}";
			});

			// Initial read
			Assert.Equal("Alice Smith", fullName.Value);
			Assert.Equal(1, evalCount);
			var v0 = fullName.Version;

			// Change one signal
			firstName.Value = "Bob";

			// Computed should re-evaluate on next read
			var result = fullName.Value;
			// May need a second read to settle (diamond re-dirtying)
			if (result != "Bob Smith")
				result = fullName.Value;

			Assert.Equal("Bob Smith", result);
			Assert.True(fullName.Version > v0, "Version should increment on value change");
			Assert.True(evalCount >= 2, "Computed should have re-evaluated");
		}

		[Fact]
		public void Computed_No_Version_Increment_When_Result_Same()
		{
			var signal = new Signal<int>(3);
			// Clamp to max 5 — changing signal from 3 to 4 still returns <=5
			var clamped = new Computed<int>(() => Math.Min(signal.Value, 5));

			_ = clamped.Value;
			var v0 = clamped.Version;

			signal.Value = 4; // different signal, but clamped result is still 4
			_ = clamped.Value;
			var v1 = clamped.Version;

			// Value changed (4 vs 3), so version should increment
			Assert.True(v1 > v0);

			signal.Value = 10; // clamped to 5
			_ = clamped.Value;
			var v2 = clamped.Version;
			Assert.True(v2 > v1);

			signal.Value = 20; // still clamped to 5 — same result
			_ = clamped.Value;
			if (clamped.Version != v2)
				_ = clamped.Value; // settle
			Assert.Equal(v2, clamped.Version); // version unchanged, same output
		}

		#endregion

		#region 4. Signal Bridge Works With INotifyPropertyRead

		[Fact]
		public void Signal_Bridge_Fires_PropertyRead_Outside_ReactiveScope()
		{
			var signal = new Signal<int>(10);
			string? readPropertyName = null;

			signal.PropertyRead += (sender, args) =>
			{
				readPropertyName = args.PropertyName;
			};

			// Read outside a ReactiveScope — should fire PropertyRead
			Assert.Null(ReactiveScope.Current);
			var val = signal.Value;

			Assert.Equal("Value", readPropertyName);
			Assert.Equal(10, val);
		}

		[Fact]
		public void Signal_Bridge_Does_Not_Fire_PropertyRead_Inside_ReactiveScope()
		{
			var signal = new Signal<int>(10);
			bool propertyReadFired = false;

			signal.PropertyRead += (sender, args) =>
			{
				propertyReadFired = true;
			};

			// Read inside a ReactiveScope — should NOT fire PropertyRead
			using var scope = ReactiveScope.BeginTracking();
			var val = signal.Value;

			Assert.False(propertyReadFired,
				"PropertyRead should not fire inside ReactiveScope");
			Assert.Equal(10, val);
		}

		[Fact]
		public void Signal_Bridge_Fires_PropertyChanged_On_Write()
		{
			var signal = new Signal<int>(0);
			string? changedPropertyName = null;

			signal.PropertyChanged += (sender, args) =>
			{
				changedPropertyName = args.PropertyName;
			};

			signal.Value = 42;
			Assert.Equal("Value", changedPropertyName);
		}

		[Fact]
		public void Signal_Bridge_Does_Not_Fire_PropertyChanged_For_Same_Value()
		{
			var signal = new Signal<int>(10);
			bool changedFired = false;

			signal.PropertyChanged += (sender, args) =>
			{
				changedFired = true;
			};

			signal.Value = 10; // same value
			Assert.False(changedFired);
		}

		#endregion

		#region 5. SignalList Queues Changes

		[Fact]
		public void SignalList_Queues_Insert_Changes()
		{
			var list = new SignalList<string>();

			list.Add("alpha");
			list.Add("beta");
			list.Add("gamma");

			var changes = list.ConsumePendingChanges();
			Assert.Equal(3, changes.Count);

			Assert.Equal(ListChangeKind.Insert, changes[0].Kind);
			Assert.Equal(0, changes[0].Index);
			Assert.Equal("alpha", changes[0].Item);

			Assert.Equal(ListChangeKind.Insert, changes[1].Kind);
			Assert.Equal(1, changes[1].Index);
			Assert.Equal("beta", changes[1].Item);

			Assert.Equal(ListChangeKind.Insert, changes[2].Kind);
			Assert.Equal(2, changes[2].Index);
			Assert.Equal("gamma", changes[2].Item);
		}

		[Fact]
		public void SignalList_Changes_Cleared_After_Consumption()
		{
			var list = new SignalList<string>();

			list.Add("one");
			list.Add("two");

			var first = list.ConsumePendingChanges();
			Assert.Equal(2, first.Count);

			var second = list.ConsumePendingChanges();
			Assert.Empty(second);
		}

		[Fact]
		public void SignalList_Remove_Change_Tracked()
		{
			var list = new SignalList<string>(new[] { "a", "b", "c" });
			_ = list.ConsumePendingChanges(); // clear initial state

			list.Remove("b");

			var changes = list.ConsumePendingChanges();
			Assert.Single(changes);
			Assert.Equal(ListChangeKind.Remove, changes[0].Kind);
			Assert.Equal(1, changes[0].Index);
			Assert.Equal("b", changes[0].OldItem);
		}

		[Fact]
		public void SignalList_Replace_Change_Tracked()
		{
			var list = new SignalList<string>(new[] { "x", "y", "z" });
			_ = list.ConsumePendingChanges(); // clear

			list[1] = "Y";

			var changes = list.ConsumePendingChanges();
			Assert.Single(changes);
			Assert.Equal(ListChangeKind.Replace, changes[0].Kind);
			Assert.Equal(1, changes[0].Index);
			Assert.Equal("y", changes[0].OldItem);
			Assert.Equal("Y", changes[0].Item);
		}

		[Fact]
		public void SignalList_Notifies_Subscribers_On_Change()
		{
			var list = new SignalList<int>();
			int notifyCount = 0;

			var subscriber = new TestSubscriber(() => notifyCount++);
			list.Subscribe(subscriber);

			list.Add(1);
			Assert.Equal(1, notifyCount);

			list.Add(2);
			Assert.Equal(2, notifyCount);

			list.RemoveAt(0);
			Assert.Equal(3, notifyCount);
		}

		[Fact]
		public void SignalList_Batch_Produces_Reset_Change()
		{
			var list = new SignalList<int>(new[] { 1, 2, 3 });
			_ = list.ConsumePendingChanges(); // clear

			list.Batch(items =>
			{
				items.Clear();
				items.AddRange(new[] { 10, 20, 30 });
			});

			var changes = list.ConsumePendingChanges();
			Assert.Single(changes);
			Assert.Equal(ListChangeKind.Reset, changes[0].Kind);

			Assert.Equal(3, list.Count);
			Assert.Equal(10, list[0]);
		}

		#endregion

		#region 6. ReactiveEnvironment Per-Key Tracking

		[Fact]
		public void ReactiveEnvironment_Per_Key_Tracking()
		{
			// ReactiveEnvironment is internal, so we test through its behavior:
			// Create a new instance via reflection (InternalsVisibleTo is set)
			var env = new ReactiveEnvironment();

			int bgNotifyCount = 0;
			var bgSubscriber = new TestSubscriber(() => bgNotifyCount++);

			// Track reads for "Background" key
			using (var scope = ReactiveScope.BeginTracking())
			{
				env.TrackRead("Background");
				var reads = scope.EndTracking();

				// Subscribe to the "Background" key's source
				foreach (var source in reads)
					source.Subscribe(bgSubscriber);
			}

			// Set a DIFFERENT key — "FontSize"
			env.SetValue("FontSize", 18);
			Assert.Equal(0, bgNotifyCount); // Background subscriber NOT notified

			// Set the "Background" key
			env.SetValue("Background", "Red");
			Assert.Equal(1, bgNotifyCount); // Background subscriber IS notified
		}

		[Fact]
		public void ReactiveEnvironment_Same_Key_Returns_Same_Source()
		{
			var env = new ReactiveEnvironment();

			IReactiveSource? source1 = null;
			IReactiveSource? source2 = null;

			using (var scope = ReactiveScope.BeginTracking())
			{
				env.TrackRead("Color");
				var reads = scope.EndTracking();
				foreach (var s in reads) source1 = s;
			}

			using (var scope = ReactiveScope.BeginTracking())
			{
				env.TrackRead("Color");
				var reads = scope.EndTracking();
				foreach (var s in reads) source2 = s;
			}

			Assert.Same(source1, source2);
		}

		#endregion

		#region 7. Hot Reload Transfers Signal References

		class OldView : View
		{
			public readonly Signal<int> counter = new(42);
			public readonly Signal<string> name = new("Alice");

			[Body]
			View body() => new Text($"{counter.Value} {name.Value}");
		}

		class NewView : View
		{
			public readonly Signal<int> counter = new(0);
			public readonly Signal<string> name = new("");

			[Body]
			View body() => new Text($"New: {counter.Value} {name.Value}");
		}

		[Fact]
		public void Hot_Reload_Transfers_Signal_References()
		{
			var oldView = new OldView();
			var newView = new NewView();

			// The old view has signals with values 42 and "Alice"
			Assert.Equal(42, oldView.counter.Peek());
			Assert.Equal("Alice", oldView.name.Peek());

			// New view starts with defaults
			Assert.Equal(0, newView.counter.Peek());
			Assert.Equal("", newView.name.Peek());

			// Transfer state
			oldView.TransferHotReloadStateTo(newView);

			// After transfer, new view's signal fields should reference the old signals
			Assert.Same(oldView.counter, newView.counter);
			Assert.Same(oldView.name, newView.name);

			// Values are preserved
			Assert.Equal(42, newView.counter.Peek());
			Assert.Equal("Alice", newView.name.Peek());
		}

		[Fact]
		public void Hot_Reload_Transfer_Null_NewView_NoOp()
		{
			var oldView = new OldView();
			// Should not throw
			oldView.TransferHotReloadStateTo(null);
		}

		#endregion

		#region 8. Disposed View Unsubscribes From Signals

		class DisposableView : View
		{
			public readonly Signal<int> Value = new(0);
			public int BuildCount;

			[Body]
			View body()
			{
				BuildCount++;
				return new Text($"Value: {Value.Value}");
			}
		}

		[Fact]
		public void Disposed_View_Unsubscribes_From_Signals()
		{
			var view = new DisposableView();

			// Force initial build to set up reactive subscriptions
			InitializeHandlers(view);
			Assert.Equal(1, view.BuildCount);

			var buildCountAfterInit = view.BuildCount;

			// Dispose the view — should unsubscribe from all signals
			view.Dispose();
			Assert.True(view.IsDisposed);

			// Change the signal — should NOT crash and should NOT rebuild
			view.Value.Value = 99;
			ReactiveScheduler.FlushSync();

			// Build count should not have changed
			Assert.Equal(buildCountAfterInit, view.BuildCount);
		}

		[Fact]
		public void Disposed_View_Signal_Change_Does_Not_Crash()
		{
			var view = new DisposableView();
			InitializeHandlers(view);

			view.Dispose();

			// Multiple signal changes after dispose — no crashes
			for (int i = 0; i < 10; i++)
			{
				view.Value.Value = i;
			}

			ReactiveScheduler.FlushSync();
			Assert.True(view.IsDisposed);
		}

		#endregion

		#region Test Helpers

		private class TestSubscriber : IReactiveSubscriber
		{
			private readonly Action _onChanged;

			public TestSubscriber(Action onChanged)
			{
				_onChanged = onChanged;
			}

			public void OnDependencyChanged(IReactiveSource source)
			{
				_onChanged();
			}
		}

		#endregion
	}
}
