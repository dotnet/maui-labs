using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comet.Reactive;
using Xunit;

//
// NOTE: SubscriberList is internal in the proposal. These tests may require
// InternalsVisibleTo("Comet.Tests") which already exists in Comet's AssemblyInfo.
// If SubscriberList is not accessible, wrap test access via Signal/Computed public API.

namespace Comet.Tests
{
	public class SubscriberListTests : TestBase
	{
		#region Basic Add/Remove/NotifyAll

		[Fact]
		public void SubscriberList_Add_And_NotifyAll()
		{
			// Use a Signal as a vehicle — its internal SubscriberList handles notifications
			var signal = new Signal<int>(0);
			int notifyCount = 0;

			var subscriber = new TestSubscriber(() => notifyCount++);
			signal.Subscribe(subscriber);

			signal.Value = 1;
			Assert.Equal(1, notifyCount);

			signal.Value = 2;
			Assert.Equal(2, notifyCount);
		}

		[Fact]
		public void SubscriberList_Remove_StopsNotifications()
		{
			var signal = new Signal<int>(0);
			int notifyCount = 0;

			var subscriber = new TestSubscriber(() => notifyCount++);
			signal.Subscribe(subscriber);

			signal.Value = 1;
			Assert.Equal(1, notifyCount);

			signal.Unsubscribe(subscriber);

			signal.Value = 2;
			Assert.Equal(1, notifyCount); // no new notification
		}

		[Fact]
		public void SubscriberList_MultipleSubscribers_AllNotified()
		{
			var signal = new Signal<int>(0);
			int count1 = 0, count2 = 0, count3 = 0;

			signal.Subscribe(new TestSubscriber(() => count1++));
			signal.Subscribe(new TestSubscriber(() => count2++));
			signal.Subscribe(new TestSubscriber(() => count3++));

			signal.Value = 1;

			Assert.Equal(1, count1);
			Assert.Equal(1, count2);
			Assert.Equal(1, count3);
		}

		#endregion

		#region GC'd Subscriber Auto-Pruned

		[Fact]
		public void SubscriberList_GCdSubscriber_AutoPrunedDuringNotify()
		{
			var signal = new Signal<int>(0);
			int liveCount = 0;

			// Create a subscriber that we'll let get GC'd
			AddEphemeralSubscriber(signal);

			// Keep a live subscriber
			var liveSubscriber = new TestSubscriber(() => liveCount++);
			signal.Subscribe(liveSubscriber);

			// Force GC to collect the ephemeral subscriber
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			// NotifyAll should prune the dead subscriber without error
			signal.Value = 1;
			Assert.Equal(1, liveCount);

			// Second notify to verify pruning completed cleanly
			signal.Value = 2;
			Assert.Equal(2, liveCount);
		}

		// Helper method to create a subscriber with no strong reference
		private static void AddEphemeralSubscriber(Signal<int> signal)
		{
			var ephemeral = new TestSubscriber(() => { /* no-op */ });
			signal.Subscribe(ephemeral);
			// ephemeral goes out of scope here — eligible for GC
		}

		#endregion

		#region Thread-Safe Concurrent Add + NotifyAll

		[Fact]
		public async Task SubscriberList_ConcurrentAddAndNotify_NoCorruption()
		{
			var signal = new Signal<int>(0);
			var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			int totalNotifications = 0;

			// Continuously add subscribers
			var adder = Task.Run(() =>
			{
				for (int i = 0; i < 500; i++)
				{
					if (cts.Token.IsCancellationRequested) break;
					signal.Subscribe(new TestSubscriber(() =>
						Interlocked.Increment(ref totalNotifications)));
				}
			});

			// Continuously write (triggers NotifyAll)
			var writer = Task.Run(() =>
			{
				for (int i = 1; i <= 100; i++)
				{
					if (cts.Token.IsCancellationRequested) break;
					signal.Value = i;
					Thread.Sleep(1); // small yield
				}
			});

			await Task.WhenAll(adder, writer);

			// No exception = thread safety is maintained
			// Total notifications > 0 means at least some subscribers were notified
			Assert.True(totalNotifications > 0, "At least some notifications should have fired");
		}

		#endregion

		#region Clear Removes All

		[Fact]
		public void SubscriberList_Clear_RemovesAllSubscribers()
		{
			var signal = new Signal<int>(0);
			int count1 = 0, count2 = 0;

			signal.Subscribe(new TestSubscriber(() => count1++));
			signal.Subscribe(new TestSubscriber(() => count2++));

			signal.Value = 1;
			Assert.Equal(1, count1);
			Assert.Equal(1, count2);

			// Dispose clears subscribers
			signal.Dispose();

			// After clear, new writes should NOT notify old subscribers
			// Note: Dispose also prevents new subscriptions
			signal.Value = 2;
			Assert.Equal(1, count1); // unchanged
			Assert.Equal(1, count2); // unchanged
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
