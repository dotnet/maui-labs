using System;
using System.Threading;
using System.Threading.Tasks;
using Comet.Reactive;
using Xunit;


namespace Comet.Tests
{
	public class ReactiveScopeTests : TestBase
	{
		#region BeginTracking Captures Reads

		[Fact]
		public void ReactiveScope_CapturesSignalReads()
		{
			var a = new Signal<int>(1);
			var b = new Signal<string>("hi");

			using var scope = ReactiveScope.BeginTracking();
			_ = a.Value;
			_ = b.Value;
			var reads = scope.EndTracking();

			Assert.Equal(2, reads.Count);
			Assert.Contains(a, reads);
			Assert.Contains(b, reads);
		}

		[Fact]
		public void ReactiveScope_DuplicateReads_OnlyRecordedOnce()
		{
			var signal = new Signal<int>(1);

			using var scope = ReactiveScope.BeginTracking();
			_ = signal.Value;
			_ = signal.Value;
			_ = signal.Value;
			var reads = scope.EndTracking();

			Assert.Single(reads); // HashSet deduplicates
		}

		[Fact]
		public void ReactiveScope_NoReads_ReturnsEmptySet()
		{
			using var scope = ReactiveScope.BeginTracking();
			var reads = scope.EndTracking();

			Assert.Empty(reads);
		}

		#endregion

		#region Nesting

		[Fact]
		public void ReactiveScope_InnerScopeCapturesItsReads_OuterCapturesIts()
		{
			var outerSignal = new Signal<int>(1);
			var innerSignal = new Signal<int>(2);
			var sharedSignal = new Signal<int>(3);

			using var outerScope = ReactiveScope.BeginTracking();
			_ = outerSignal.Value;

			// Inner scope
			using (var innerScope = ReactiveScope.BeginTracking())
			{
				_ = innerSignal.Value;
				_ = sharedSignal.Value;
				var innerReads = innerScope.EndTracking();

				Assert.Equal(2, innerReads.Count);
				Assert.Contains(innerSignal, innerReads);
				Assert.Contains(sharedSignal, innerReads);
			} // inner scope disposes, restores outer

			// Continue reading in outer scope
			_ = sharedSignal.Value;
			var outerReads = outerScope.EndTracking();

			// Outer scope should only see its own reads (outerSignal + sharedSignal)
			Assert.Contains(outerSignal, outerReads);
			Assert.Contains(sharedSignal, outerReads);
			Assert.DoesNotContain(innerSignal, outerReads);
		}

		[Fact]
		public void ReactiveScope_InnerDispose_RestoresOuterAsCurrent()
		{
			using var outer = ReactiveScope.BeginTracking();
			Assert.Equal(outer, ReactiveScope.Current);

			using (var inner = ReactiveScope.BeginTracking())
			{
				Assert.Equal(inner, ReactiveScope.Current);
			}

			// After inner dispose, outer should be current again
			Assert.Equal(outer, ReactiveScope.Current);
		}

		#endregion

		#region Dispose Restores Previous (Exception-Safe)

		[Fact]
		public void ReactiveScope_DisposeRestoresPrevious_OnException()
		{
			Assert.Null(ReactiveScope.Current);

			try
			{
				using var scope = ReactiveScope.BeginTracking();
				Assert.NotNull(ReactiveScope.Current);
				throw new InvalidOperationException("boom");
			}
			catch
			{
				// Expected
			}

			// After exception + dispose, Current should be null (previous)
			Assert.Null(ReactiveScope.Current);
		}

		[Fact]
		public void ReactiveScope_NestedDispose_RestoresCorrectPrevious_OnException()
		{
			using var outer = ReactiveScope.BeginTracking();

			try
			{
				using var inner = ReactiveScope.BeginTracking();
				throw new InvalidOperationException("boom");
			}
			catch
			{
				// Expected
			}

			// After inner exception, outer should be current
			Assert.Equal(outer, ReactiveScope.Current);
		}

		#endregion

		#region Background Thread

		[Fact]
		public async Task ReactiveScope_BackgroundThread_CurrentIsNull()
		{
			ReactiveScope? bgCurrent = null;

			await Task.Run(() =>
			{
				bgCurrent = ReactiveScope.Current;
			});

			Assert.Null(bgCurrent);
		}

		[Fact]
		public void ReactiveScope_UIThreadScope_NotVisibleOnBackgroundThread()
		{
			using var uiScope = ReactiveScope.BeginTracking();
			Assert.NotNull(ReactiveScope.Current);

			ReactiveScope? bgCurrent = null;
			var bgThread = new System.Threading.Thread(() =>
			{
				bgCurrent = ReactiveScope.Current;
			});
			bgThread.Start();
			bgThread.Join();

			// [ThreadStatic] means background thread has its own null scope
			Assert.Null(bgCurrent);

			// Original thread's scope should still be intact
			Assert.Equal(uiScope, ReactiveScope.Current);
		}

		#endregion
	}
}
