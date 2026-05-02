using System;
using System.Collections.Generic;
using Comet.Reactive;
using Xunit;
using ReactiveEffect = Comet.Reactive.Effect;

namespace Comet.Tests
{
	public class EffectTests : TestBase
	{
		#region Runs Immediately

		[Fact]
		public void Effect_RunsImmediately_WhenRunImmediatelyIsTrue()
		{
			var signal = new Signal<int>(1);
			int runCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				_ = signal.Value; // track dependency
				runCount++;
			}, runImmediately: true);

			Assert.Equal(1, runCount);
		}

		[Fact]
		public void Effect_DoesNotRunImmediately_WhenRunImmediatelyIsFalse()
		{
			var signal = new Signal<int>(1);
			int runCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				_ = signal.Value;
				runCount++;
			}, runImmediately: false);

			Assert.Equal(0, runCount);
		}

		#endregion

		#region Re-Runs on Dependency Change

		[Fact]
		public void Effect_ReRunsWhenDependencyChanges()
		{
			var signal = new Signal<int>(0);
			var observedValues = new List<int>();

			var effect = new ReactiveEffect(() =>
			{
				observedValues.Add(signal.Value);
			}, runImmediately: true);

			Assert.Single(observedValues);
			Assert.Equal(0, observedValues[0]);

			// Change the signal and flush
			signal.Value = 42;
			ReactiveScheduler.FlushSync();

			// Effect observed all values including the last write
			Assert.True(observedValues.Count >= 2, "Effect should have re-run");
			Assert.Equal(42, observedValues[observedValues.Count - 1]);
		}

		[Fact]
		public void Effect_TracksMultipleDependencies()
		{
			var a = new Signal<int>(1);
			var b = new Signal<int>(2);
			int lastSum = 0;

			var effect = new ReactiveEffect(() =>
			{
				lastSum = a.Value + b.Value;
			}, runImmediately: true);

			Assert.Equal(3, lastSum);

			a.Value = 10;
			ReactiveScheduler.FlushSync();
			Assert.Equal(12, lastSum);

			b.Value = 20;
			ReactiveScheduler.FlushSync();
			Assert.Equal(30, lastSum);
		}

		#endregion

		#region Dynamic Dependencies (Diff-Based)

		[Fact]
		public void Effect_DynamicDeps_TracksConditionalReads()
		{
			var useA = new Signal<bool>(true);
			var a = new Signal<int>(10);
			var b = new Signal<int>(20);
			int lastValue = 0;

			var effect = new ReactiveEffect(() =>
			{
				lastValue = useA.Value ? a.Value : b.Value;
			}, runImmediately: true);

			Assert.Equal(10, lastValue);

			// Changing b should NOT change lastValue (b not tracked)
			b.Value = 30;
			ReactiveScheduler.FlushSync();
			Assert.Equal(10, lastValue);

			// Switch branch — now reads b
			useA.Value = false;
			ReactiveScheduler.FlushSync();
			Assert.Equal(30, lastValue);

			// Now changing a should NOT change lastValue (a no longer tracked)
			a.Value = 99;
			ReactiveScheduler.FlushSync();
			Assert.Equal(30, lastValue);

			// Changing b SHOULD now update
			b.Value = 50;
			ReactiveScheduler.FlushSync();
			Assert.Equal(50, lastValue);
		}

		#endregion

		#region Exception Recovery

		[Fact]
		public void Effect_ExceptionRecovery_ClearsDirtyAndAllowsRerun()
		{
			var signal = new Signal<int>(0);
			bool shouldThrow = true;
			int runCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				runCount++;
				_ = signal.Value;
				if (shouldThrow)
					throw new InvalidOperationException("transient error");
			}, runImmediately: false);

			// Manually run — exception is swallowed inside Run(), deps discarded
			effect.Run();
			Assert.Equal(1, runCount);

			// After exception, the effect has no subscriptions (deps were
			// discarded), so signal changes can't re-queue it automatically.
			// Manual Run() is required to reestablish subscriptions.
			shouldThrow = false;
			effect.Run();
			Assert.Equal(2, runCount);

			// Now the effect is subscribed. Future signal changes will re-run it.
			signal.Value = 42;
			ReactiveScheduler.FlushSync();
			Assert.True(runCount >= 3, "Effect should re-run after resubscribing");
		}

		#endregion

		#region Disposed Effect Never Re-Runs

		[Fact]
		public void Effect_Disposed_NeverReRuns()
		{
			var signal = new Signal<int>(0);
			int runCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				_ = signal.Value;
				runCount++;
			}, runImmediately: true);

			Assert.Equal(1, runCount);

			effect.Dispose();

			// Changing signal should NOT trigger re-run
			signal.Value = 99;
			ReactiveScheduler.FlushSync();

			Assert.Equal(1, runCount); // still 1
		}

		#endregion

		#region Effect Deduplication

		[Fact]
		public void Effect_DirtiedTwice_SeesLatestValues()
		{
			var a = new Signal<int>(1);
			var b = new Signal<int>(2);
			int lastA = 0;
			int lastB = 0;

			var effect = new ReactiveEffect(() =>
			{
				lastA = a.Value;
				lastB = b.Value;
			}, runImmediately: true);

			Assert.Equal(1, lastA);
			Assert.Equal(2, lastB);

			// Write both signals — effect should eventually see both new values
			a.Value = 10;
			b.Value = 20;
			ReactiveScheduler.FlushSync();

			Assert.Equal(10, lastA);
			Assert.Equal(20, lastB);
		}

		[Fact]
		public void Effect_MultipleWritesToSameSignal_SeesLatestValue()
		{
			var signal = new Signal<int>(0);
			int lastSeen = -1;

			var effect = new ReactiveEffect(() =>
			{
				lastSeen = signal.Value;
			}, runImmediately: true);

			Assert.Equal(0, lastSeen);

			// Multiple writes — effect should see the final value
			signal.Value = 1;
			signal.Value = 2;
			signal.Value = 3;
			ReactiveScheduler.FlushSync();

			Assert.Equal(3, lastSeen);
		}

		#endregion
	}
}
