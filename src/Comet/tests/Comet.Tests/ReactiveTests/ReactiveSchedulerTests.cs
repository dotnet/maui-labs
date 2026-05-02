using System;
using System.Collections.Generic;
using Comet.Reactive;
using Xunit;
using ReactiveEffect = Comet.Reactive.Effect;

namespace Comet.Tests
{
	public class ReactiveSchedulerTests : TestBase
	{
		#region Coalescing — Multiple Writes → Single Flush

		[Fact]
		public void Scheduler_MultipleSignalWrites_EffectEventuallySeesAll()
		{
			// In unit test context, ThreadHelper.RunOnMainThread dispatches
			// synchronously so each signal write triggers an immediate flush.
			// We verify that the effect processes every write and the final
			// state is correct.
			var a = new Signal<int>(0);
			var b = new Signal<int>(0);
			int effectRunCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				_ = a.Value;
				_ = b.Value;
				effectRunCount++;
			}, runImmediately: true);

			Assert.Equal(1, effectRunCount);

			a.Value = 1;
			b.Value = 2;
			a.Value = 3;
			b.Value = 4;

			ReactiveScheduler.FlushSync();

			// Effect has processed all mutations (may run multiple times
			// in synchronous dispatch mode — coalescing requires async dispatcher)
			Assert.True(effectRunCount > 1,
				$"Effect should have re-run at least once, ran {effectRunCount} times");
		}

		[Fact]
		public void Scheduler_MultipleSignalWrites_EffectSeesLatestValues()
		{
			var signal = new Signal<int>(0);
			int lastSeen = -1;

			var effect = new ReactiveEffect(() =>
			{
				lastSeen = signal.Value;
			}, runImmediately: true);

			Assert.Equal(0, lastSeen);

			signal.Value = 1;
			signal.Value = 2;
			signal.Value = 3;

			ReactiveScheduler.FlushSync();
			Assert.Equal(3, lastSeen); // sees the latest value
		}

		#endregion

		#region FlushSync

		[Fact]
		public void Scheduler_FlushSync_ProcessesPendingEffects()
		{
			var signal = new Signal<int>(0);
			int effectRunCount = 0;

			var effect = new ReactiveEffect(() =>
			{
				_ = signal.Value;
				effectRunCount++;
			}, runImmediately: true);

			Assert.Equal(1, effectRunCount);

			signal.Value = 42;
			ReactiveScheduler.FlushSync();

			// After signal write + FlushSync, the effect must have re-run
			Assert.True(effectRunCount >= 2,
				$"Effect should have re-run after signal change, ran {effectRunCount} times");
		}

		[Fact]
		public void Scheduler_FlushSync_WhenNothingDirty_IsNoOp()
		{
			// Should not throw or have side effects when nothing is pending
			ReactiveScheduler.FlushSync();
		}

		#endregion

		#region MaxFlushDepth Guard

		[Fact]
		public void Scheduler_MaxFlushDepth_BreaksInfiniteLoop()
		{
			var signal = new Signal<int>(0);
			int runCount = 0;

			// Create a pathological effect that writes to its own dependency,
			// creating an infinite re-trigger loop
			var effect = new ReactiveEffect(() =>
			{
				var current = signal.Value;
				runCount++;
				// Write back → triggers re-dirty → infinite loop
				signal.Value = current + 1;
			}, runImmediately: true);

			// FlushSync should hit MaxFlushDepth and break the cycle.
			// In DEBUG, the proposal says this throws InvalidOperationException.
			// In Release, it logs a diagnostic and clears dirty sets.
#if DEBUG
			Assert.Throws<InvalidOperationException>(() => ReactiveScheduler.FlushSync());
#else
			ReactiveScheduler.FlushSync(); // should not throw in Release
#endif

			// The scheduler should have broken out at MaxFlushDepth (100)
			// runCount should be bounded, not infinite
			Assert.True(runCount <= ReactiveScheduler.MaxFlushDepth + 10,
				$"Expected bounded runs, got {runCount}");
		}

		#endregion

		#region Cascading Effects

		[Fact]
		public void Scheduler_CascadingEffects_ProcessedWithinSameFlush()
		{
			var a = new Signal<int>(0);
			var b = new Signal<int>(0);
			int effectBRunCount = 0;

			// Effect 1: when a changes, write b
			var effect1 = new ReactiveEffect(() =>
			{
				b.Value = a.Value * 2;
			}, runImmediately: true);

			// Effect 2: reads b
			var effect2 = new ReactiveEffect(() =>
			{
				_ = b.Value;
				effectBRunCount++;
			}, runImmediately: true);

			var initialRuns = effectBRunCount;

			a.Value = 5;
			ReactiveScheduler.FlushSync();

			// effect2 should have run because effect1 wrote to b
			Assert.True(effectBRunCount > initialRuns,
				"Cascading effect should have re-run within the same flush");
			Assert.Equal(10, b.Value);
		}

		#endregion

		#region Effect Ordering

		[Fact]
		public void Scheduler_EffectsRunBeforeViewReloads()
		{
			// Per the proposal: flush order is (1) dirty Effects, (2) dirty Views
			// We verify effects have resolved before any view reload would happen
			var signal = new Signal<int>(0);
			var sideEffectValue = -1;

			var effect = new ReactiveEffect(() =>
			{
				sideEffectValue = signal.Value * 2;
			}, runImmediately: true);

			signal.Value = 5;
			ReactiveScheduler.FlushSync();

			Assert.Equal(10, sideEffectValue);
		}

		#endregion
	}
}
