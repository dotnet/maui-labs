using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// TDD contract tests for PropertySubscription&lt;T&gt;.
	/// These define the behavior Holden must satisfy in src/Comet/Reactive/PropertySubscription.cs.
	/// Tests are expected to FAIL until the implementation is complete.
	/// </summary>
	public class PropertySubscriptionTests : TestBase
	{
		#region Core Behavior

		/// <summary>
		/// 1. A PropertySubscription constructed with a static T value has no reactive
		///    dependencies — it's just a value holder.
		/// </summary>
		[Fact]
		public void StaticValue_NoTracking()
		{
			var sub = new PropertySubscription<int>(42);

			Assert.Equal(42, sub.Value);

			// Static subscriptions should have no dependencies
			// Changing an unrelated signal should have no effect
			var unrelated = new Signal<int>(0);
			int callbackCount = 0;
			sub.PropertyChangedCallback = _ => callbackCount++;

			unrelated.Value = 99;

			Assert.Equal(0, callbackCount);
			Assert.Equal(42, sub.Value);
		}

		/// <summary>
		/// 2. A PropertySubscription constructed with Func&lt;T&gt; that reads a Signal
		///    tracks that Signal as a dependency via ReactiveScope.
		/// </summary>
		[Fact]
		public void FuncConstructor_TracksSignalReads()
		{
			var signal = new Signal<int>(10);

			var sub = new PropertySubscription<int>(() => signal.Value);

			// Should have evaluated and captured the current value
			Assert.Equal(10, sub.Value);
		}

		/// <summary>
		/// 3. A PropertySubscription constructed with a Signal directly creates a
		///    bidirectional binding — reads the signal's current value and tracks it.
		/// </summary>
		[Fact]
		public void SignalConstructor_DirectBinding()
		{
			var signal = new Signal<string>("hello");

			var sub = new PropertySubscription<string>(signal);

			Assert.Equal("hello", sub.Value);
		}

		/// <summary>
		/// 4. When a tracked Signal changes, the Func is re-evaluated and the
		///    PropertySubscription holds the new value.
		/// </summary>
		[Fact]
		public void OnDependencyChanged_ReEvaluatesFunc()
		{
			var signal = new Signal<int>(1);
			var sub = new PropertySubscription<int>(() => signal.Value * 2);

			Assert.Equal(2, sub.Value);

			// Mutate the signal — PropertySubscription should re-evaluate synchronously
			// via OnDependencyChanged
			signal.Value = 5;

			Assert.Equal(10, sub.Value);
		}

		/// <summary>
		/// 5. When the re-evaluated value differs from the old value, the
		///    PropertyChangedCallback is fired with the new value.
		/// </summary>
		[Fact]
		public void OnDependencyChanged_FiresCallback()
		{
			var signal = new Signal<int>(1);
			int? callbackValue = null;

			var sub = new PropertySubscription<int>(
				() => signal.Value * 3);
			sub.PropertyChangedCallback = v => callbackValue = v;

			signal.Value = 4;

			Assert.Equal(12, callbackValue);
		}

		/// <summary>
		/// 6. If re-evaluation produces the same value as before, the callback
		///    is NOT fired (equality check skips notification).
		/// </summary>
		[Fact]
		public void EqualityCheck_SkipsCallback()
		{
			var signal = new Signal<int>(5);
			int callbackCount = 0;

			// Func clamps to max 5 — changing signal above 5 produces same result
			var sub = new PropertySubscription<int>(
				() => Math.Min(signal.Value, 5));
			sub.PropertyChangedCallback = _ => callbackCount++;

			signal.Value = 10; // still evaluates to 5

			Assert.Equal(0, callbackCount);
			Assert.Equal(5, sub.Value);
		}

		/// <summary>
		/// 7. A Func reading 2+ Signals tracks ALL of them as dependencies.
		///    Changes to any tracked Signal trigger re-evaluation.
		/// </summary>
		[Fact]
		public void MultipleSignals_TracksAll()
		{
			var a = new Signal<int>(1);
			var b = new Signal<int>(2);
			var c = new Signal<int>(3);
			var callbackValues = new List<int>();

			var sub = new PropertySubscription<int>(
				() => a.Value + b.Value + c.Value);
			sub.PropertyChangedCallback = v => callbackValues.Add(v);

			Assert.Equal(6, sub.Value);

			a.Value = 10;
			Assert.Equal(15, sub.Value);
			Assert.Contains(15, callbackValues);

			b.Value = 20;
			Assert.Equal(33, sub.Value);
			Assert.Contains(33, callbackValues);

			c.Value = 30;
			Assert.Equal(60, sub.Value);
			Assert.Contains(60, callbackValues);
		}

		/// <summary>
		/// 8. If re-evaluation no longer reads a previously-tracked Signal,
		///    the PropertySubscription unsubscribes from it (dynamic dep diffing).
		/// </summary>
		[Fact]
		public void SignalRemoved_Unsubscribes()
		{
			var useA = new Signal<bool>(true);
			var a = new Signal<int>(10);
			var b = new Signal<int>(20);
			int callbackCount = 0;

			var sub = new PropertySubscription<int>(
				() => useA.Value ? a.Value : b.Value);
			sub.PropertyChangedCallback = _ => callbackCount++;

			Assert.Equal(10, sub.Value);

			// Switch to branch b
			useA.Value = false;
			Assert.Equal(20, sub.Value);

			// Reset callback counter after the branch switch
			callbackCount = 0;

			// Changing 'a' should NOT fire callback — a is no longer tracked
			a.Value = 99;
			Assert.Equal(0, callbackCount);
			Assert.Equal(20, sub.Value); // unchanged

			// Changing 'b' SHOULD fire callback — b is now tracked
			b.Value = 30;
			Assert.Equal(30, sub.Value);
			Assert.Equal(1, callbackCount);
		}

		/// <summary>
		/// 9. After Dispose(), signal changes don't fire the callback and
		///    dependencies are released.
		/// </summary>
		[Fact]
		public void Dispose_UnsubscribesAll()
		{
			var signal = new Signal<int>(1);
			int callbackCount = 0;

			var sub = new PropertySubscription<int>(() => signal.Value);
			sub.PropertyChangedCallback = _ => callbackCount++;

			sub.Dispose();

			signal.Value = 42;

			Assert.Equal(0, callbackCount);
		}

		#endregion

		#region Nesting (Critical — from skeptic review)

		/// <summary>
		/// 10. PropertySubscription.Evaluate() inside an active ReactiveScope
		///     doesn't leak its reads to the outer (body) scope.
		/// </summary>
		[Fact]
		public void NestedInBodyScope_IsolatesReads()
		{
			var bodySignal = new Signal<int>(1);
			var propSignal = new Signal<string>("hello");

			// Simulate body scope
			using var bodyScope = ReactiveScope.BeginTracking();

			// Body-level read
			_ = bodySignal.Value;

			// PropertySubscription evaluates inside the body scope
			var sub = new PropertySubscription<string>(() => propSignal.Value);

			// Body-level read after
			_ = bodySignal.Value;

			var bodyReads = bodyScope.EndTracking();

			// Body scope should contain ONLY bodySignal — propSignal must not leak
			Assert.Contains(bodySignal, bodyReads);
			Assert.DoesNotContain(propSignal, bodyReads);
		}

		/// <summary>
		/// 11. Two PropertySubscriptions evaluated sequentially each track
		///     their own Signals independently — no cross-contamination.
		/// </summary>
		[Fact]
		public void SequentialEvaluations_IndependentTracking()
		{
			var signalA = new Signal<int>(10);
			var signalB = new Signal<string>("world");
			int callbackCountA = 0;
			int callbackCountB = 0;

			var subA = new PropertySubscription<int>(() => signalA.Value * 2);
			subA.PropertyChangedCallback = _ => callbackCountA++;

			var subB = new PropertySubscription<string>(() => $"Hello {signalB.Value}");
			subB.PropertyChangedCallback = _ => callbackCountB++;

			// Changing signalA should only affect subA
			signalA.Value = 20;
			Assert.Equal(1, callbackCountA);
			Assert.Equal(0, callbackCountB);

			// Changing signalB should only affect subB
			signalB.Value = "there";
			Assert.Equal(1, callbackCountA);
			Assert.Equal(1, callbackCountB);
		}

		/// <summary>
		/// 12. After PropertySubscription.Evaluate() completes inside a body scope,
		///     the body ReactiveScope is restored as Current.
		/// </summary>
		[Fact]
		public void BodyScope_RestoredAfterEvaluation()
		{
			var bodySignal = new Signal<int>(1);
			var propSignal = new Signal<double>(2.0);

			using var bodyScope = ReactiveScope.BeginTracking();
			Assert.Equal(bodyScope, ReactiveScope.Current);

			// PropertySubscription evaluates (pushes/pops its own scope)
			var sub = new PropertySubscription<double>(() => propSignal.Value);

			// Body scope must be restored
			Assert.Equal(bodyScope, ReactiveScope.Current);

			// Body-level read should still work
			_ = bodySignal.Value;
			var bodyReads = bodyScope.EndTracking();

			Assert.Contains(bodySignal, bodyReads);
			Assert.DoesNotContain(propSignal, bodyReads);
		}

		#endregion

		#region Integration

		/// <summary>
		/// 13. Rapid signal updates produce callbacks for every distinct value
		///     change — PropertySubscription is fine-grained, NOT coalesced like
		///     body rebuilds.
		/// </summary>
		[Fact]
		public void RapidUpdates_AllCallbacksFire()
		{
			var signal = new Signal<int>(0);
			var observed = new List<int>();

			var sub = new PropertySubscription<int>(() => signal.Value);
			sub.PropertyChangedCallback = v => observed.Add(v);

			const int iterations = 60;
			for (int i = 1; i <= iterations; i++)
			{
				signal.Value = i;
			}

			// Every write produces a distinct new value → every write should fire
			Assert.Equal(iterations, observed.Count);
			for (int i = 0; i < iterations; i++)
			{
				Assert.Equal(i + 1, observed[i]);
			}
		}

		/// <summary>
		/// 14. A helper method SetPropertySubscription should dispose the old
		///     subscription when assigning a new one.
		/// </summary>
		[Fact]
		public void SetPropertySubscription_DisposesOld()
		{
			var signalOld = new Signal<int>(1);
			var signalNew = new Signal<int>(100);
			int callbackCountOld = 0;
			int callbackCountNew = 0;

			var oldSub = new PropertySubscription<int>(() => signalOld.Value);
			oldSub.PropertyChangedCallback = _ => callbackCountOld++;

			var newSub = new PropertySubscription<int>(() => signalNew.Value);
			newSub.PropertyChangedCallback = _ => callbackCountNew++;

			// Simulate SetPropertySubscription: dispose old, keep new
			oldSub.Dispose();

			// Old subscription should no longer fire
			signalOld.Value = 42;
			Assert.Equal(0, callbackCountOld);

			// New subscription should fire
			signalNew.Value = 200;
			Assert.Equal(1, callbackCountNew);
		}

		/// <summary>
		/// 15. Multiple threads writing to the same Signal shouldn't crash the
		///     PropertySubscription (thread safety).
		/// </summary>
		[Fact]
		public void ThreadSafety_ConcurrentWrites()
		{
			var signal = new Signal<int>(0);
			int callbackCount = 0;

			var sub = new PropertySubscription<int>(() => signal.Value);
			sub.PropertyChangedCallback = _ => Interlocked.Increment(ref callbackCount);

			const int threadCount = 4;
			const int writesPerThread = 100;
			var tasks = new Task[threadCount];

			for (int t = 0; t < threadCount; t++)
			{
				int threadId = t;
				tasks[t] = Task.Run(() =>
				{
					for (int i = 0; i < writesPerThread; i++)
					{
						signal.Value = threadId * writesPerThread + i;
					}
				});
			}

			Task.WaitAll(tasks);

			// Should not have crashed. Callback count may vary due to
			// equality checks and threading, but should be > 0.
			Assert.True(callbackCount > 0,
				$"Expected callbacks from concurrent writes, got {callbackCount}");
		}

		#endregion

		#region Two-Way Binding (Signal Overload)

		/// <summary>
		/// 16. PropertySubscription(signal) evaluates to the signal's current value.
		/// </summary>
		[Fact]
		public void SignalOverload_ReadsCurrentValue()
		{
			var signal = new Signal<double>(3.14);

			var sub = new PropertySubscription<double>(signal);

			Assert.Equal(3.14, sub.Value);
		}

		/// <summary>
		/// 17. The Signal overload provides a WriteBack delegate that can set
		///     the signal's value (two-way binding).
		/// </summary>
		[Fact]
		public void SignalOverload_WritesBack()
		{
			var signal = new Signal<double>(1.0);

			var sub = new PropertySubscription<double>(signal);

			// The subscription should expose a way to write back to the signal
			Assert.NotNull(sub.WriteBack);
			sub.WriteBack!(2.5);

			Assert.Equal(2.5, signal.Peek());
		}

		/// <summary>
		/// 18. Signal changes trigger the PropertyChangedCallback on the subscription.
		/// </summary>
		[Fact]
		public void SignalOverload_TracksSignal()
		{
			var signal = new Signal<int>(0);
			int? callbackValue = null;

			var sub = new PropertySubscription<int>(signal);
			sub.PropertyChangedCallback = v => callbackValue = v;

			signal.Value = 42;

			Assert.Equal(42, callbackValue);
			Assert.Equal(42, sub.Value);
		}

		#endregion
	}
}
