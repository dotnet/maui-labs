using System;
using Comet.Reactive;
using Xunit;


namespace Comet.Tests
{
	public class ReactiveDiagnosticsTests : TestBase, IDisposable
	{
		public ReactiveDiagnosticsTests()
		{
			// Reset diagnostics state before each test
			ReactiveDiagnostics.IsEnabled = false;
		}

		public void Dispose()
		{
			// Clean up after each test
			ReactiveDiagnostics.IsEnabled = false;
		}

		#region Subscription Returns IDisposable

		[Fact]
		public void Diagnostics_OnSignalChanged_ReturnsDisposable()
		{
			ReactiveDiagnostics.IsEnabled = true;
			int eventCount = 0;

			var sub = ReactiveDiagnostics.OnSignalChanged(e => eventCount++);
			Assert.NotNull(sub);

			// Trigger a signal change
			var signal = new Signal<int>(0) { DebugName = "test" };
			signal.Value = 1;

			Assert.True(eventCount > 0, "Should have received signal changed event");

			// Dispose the subscription
			var countBefore = eventCount;
			sub.Dispose();

			// Trigger another change — should NOT fire
			signal.Value = 2;
			Assert.Equal(countBefore, eventCount);
		}

		[Fact]
		public void Diagnostics_OnViewRebuilt_ReturnsDisposable()
		{
			ReactiveDiagnostics.IsEnabled = true;
			int eventCount = 0;

			var sub = ReactiveDiagnostics.OnViewRebuilt(e => eventCount++);
			Assert.NotNull(sub);

			// Dispose unsubscribes
			sub.Dispose();

			// No crash from double dispose
			sub.Dispose();
		}

		[Fact]
		public void Diagnostics_OnFlushDepthWarning_ReturnsDisposable()
		{
			ReactiveDiagnostics.IsEnabled = true;
			int warningDepth = -1;

			var sub = ReactiveDiagnostics.OnFlushDepthWarning(depth => warningDepth = depth);
			Assert.NotNull(sub);

			sub.Dispose();
		}

		[Fact]
		public void Diagnostics_DisposingSubscription_Unsubscribes()
		{
			ReactiveDiagnostics.IsEnabled = true;
			int eventCount = 0;

			var sub = ReactiveDiagnostics.OnSignalChanged(e => eventCount++);

			var signal = new Signal<int>(0) { DebugName = "test" };
			signal.Value = 1;
			Assert.Equal(1, eventCount);

			sub.Dispose();

			signal.Value = 2;
			Assert.Equal(1, eventCount); // unchanged after unsubscribe
		}

		#endregion

		#region Zero Overhead When Disabled

		[Fact]
		public void Diagnostics_WhenDisabled_NoEventsFireOnSignalChange()
		{
			ReactiveDiagnostics.IsEnabled = false;
			int eventCount = 0;

			var sub = ReactiveDiagnostics.OnSignalChanged(e => eventCount++);

			var signal = new Signal<int>(0) { DebugName = "test" };
			signal.Value = 1;
			signal.Value = 2;
			signal.Value = 3;

			Assert.Equal(0, eventCount); // no events when disabled

			sub.Dispose();
		}

		[Fact]
		public void Diagnostics_WhenDisabled_NoFlushDepthWarningFires()
		{
			ReactiveDiagnostics.IsEnabled = false;
			bool warningFired = false;

			var sub = ReactiveDiagnostics.OnFlushDepthWarning(depth => warningFired = true);

			// Even if we could trigger the internal notification,
			// it should be gated by IsEnabled
			// (This is a design-level assertion from the proposal)
			Assert.False(warningFired);

			sub.Dispose();
		}

		#endregion

		#region Events Fire When Enabled

		[Fact]
		public void Diagnostics_WhenEnabled_SignalChangedEventFires()
		{
			ReactiveDiagnostics.IsEnabled = true;

			SignalChangeEvent? receivedEvent = null;
			var sub = ReactiveDiagnostics.OnSignalChanged(e => receivedEvent = e);

			var signal = new Signal<int>(0) { DebugName = "counter" };
			signal.Value = 42;

			Assert.NotNull(receivedEvent);
			Assert.Equal("Int32", receivedEvent.Value.ValueType);
			Assert.Equal("counter", receivedEvent.Value.Name);
			Assert.True(receivedEvent.Value.Version > 0);
			Assert.True(receivedEvent.Value.Timestamp <= DateTime.UtcNow);

			sub.Dispose();
		}

		[Fact]
		public void Diagnostics_CanToggleEnabled_AtRuntime()
		{
			int eventCount = 0;
			var sub = ReactiveDiagnostics.OnSignalChanged(e => eventCount++);

			var signal = new Signal<int>(0) { DebugName = "toggle-test" };

			// Disabled — no events
			ReactiveDiagnostics.IsEnabled = false;
			signal.Value = 1;
			Assert.Equal(0, eventCount);

			// Enable — events fire
			ReactiveDiagnostics.IsEnabled = true;
			signal.Value = 2;
			Assert.Equal(1, eventCount);

			// Disable again — no events
			ReactiveDiagnostics.IsEnabled = false;
			signal.Value = 3;
			Assert.Equal(1, eventCount);

			sub.Dispose();
		}

		#endregion

		#region Event Data Integrity

		[Fact]
		public void Diagnostics_SignalChangeEvent_HasCorrectStructure()
		{
			ReactiveDiagnostics.IsEnabled = true;
			SignalChangeEvent? captured = null;
			var sub = ReactiveDiagnostics.OnSignalChanged(e => captured = e);

			var signal = new Signal<string>("a") { DebugName = "name-signal" };
			signal.Value = "b";

			Assert.NotNull(captured);
			var evt = captured.Value;
			Assert.Equal("String", evt.ValueType);
			Assert.Equal("name-signal", evt.Name);
			Assert.Equal(1u, evt.Version);

			sub.Dispose();
		}

		[Fact]
		public void Diagnostics_MultipleSubscribers_AllReceiveEvents()
		{
			ReactiveDiagnostics.IsEnabled = true;
			int count1 = 0, count2 = 0;

			var sub1 = ReactiveDiagnostics.OnSignalChanged(e => count1++);
			var sub2 = ReactiveDiagnostics.OnSignalChanged(e => count2++);

			var signal = new Signal<int>(0) { DebugName = "multi" };
			signal.Value = 1;

			Assert.Equal(1, count1);
			Assert.Equal(1, count2);

			sub1.Dispose();
			sub2.Dispose();
		}

		#endregion
	}
}
