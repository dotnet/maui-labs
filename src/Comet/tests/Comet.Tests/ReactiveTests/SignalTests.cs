using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comet.Reactive;
using Xunit;


namespace Comet.Tests
{
	public class SignalTests : TestBase
	{
		#region Read/Write Basic Values

		[Fact]
		public void Signal_Int_ReadWriteRoundTrip()
		{
			var signal = new Signal<int>(42);

			Assert.Equal(42, signal.Value);

			signal.Value = 99;
			Assert.Equal(99, signal.Value);
		}

		[Fact]
		public void Signal_String_ReadWriteRoundTrip()
		{
			var signal = new Signal<string>("hello");

			Assert.Equal("hello", signal.Value);

			signal.Value = "world";
			Assert.Equal("world", signal.Value);
		}

		public readonly record struct Point3D(double X, double Y, double Z);

		[Fact]
		public void Signal_CustomStruct_ReadWriteRoundTrip()
		{
			var point = new Point3D(1.0, 2.0, 3.0);
			var signal = new Signal<Point3D>(point);

			Assert.Equal(point, signal.Value);

			var newPoint = new Point3D(4.0, 5.0, 6.0);
			signal.Value = newPoint;
			Assert.Equal(newPoint, signal.Value);
		}

		#endregion

		#region Equality Check Skips Notification

		[Fact]
		public void Signal_SameValue_DoesNotNotifySubscribers()
		{
			var signal = new Signal<int>(10);
			int notifyCount = 0;

			var subscriber = new TestSubscriber(() => notifyCount++);
			signal.Subscribe(subscriber);

			// Write the same value — should NOT notify
			signal.Value = 10;
			Assert.Equal(0, notifyCount);

			// Write a different value — SHOULD notify
			signal.Value = 20;
			Assert.Equal(1, notifyCount);
		}

		[Fact]
		public void Signal_SameValue_DoesNotIncrementVersion()
		{
			var signal = new Signal<int>(10);
			var versionBefore = signal.Version;

			signal.Value = 10; // same value
			Assert.Equal(versionBefore, signal.Version);
		}

		#endregion

		#region Version Increments

		[Fact]
		public void Signal_VersionIncrementsOnEachChange()
		{
			var signal = new Signal<int>(0);
			Assert.Equal(0u, signal.Version);

			signal.Value = 1;
			Assert.Equal(1u, signal.Version);

			signal.Value = 2;
			Assert.Equal(2u, signal.Version);

			signal.Value = 3;
			Assert.Equal(3u, signal.Version);
		}

		#endregion

		#region Peek Without Tracking

		[Fact]
		public void Signal_Peek_ReadsValueWithoutTracking()
		{
			var signal = new Signal<int>(42);

			using var scope = ReactiveScope.BeginTracking();
			var peeked = signal.Peek();
			var reads = scope.EndTracking();

			Assert.Equal(42, peeked);
			Assert.Empty(reads); // Peek should NOT register a dependency
		}

		[Fact]
		public void Signal_Value_RegistersDependencyInScope()
		{
			var signal = new Signal<int>(42);

			using var scope = ReactiveScope.BeginTracking();
			var read = signal.Value;
			var reads = scope.EndTracking();

			Assert.Equal(42, read);
			Assert.Contains(signal, reads); // Value SHOULD register a dependency
		}

		#endregion

		#region Implicit Conversion

		[Fact]
		public void Signal_ImplicitConversionFromValue()
		{
			Signal<int> signal = 42;

			Assert.Equal(42, signal.Value);
			Assert.Equal(0u, signal.Version);
		}

		[Fact]
		public void Signal_ImplicitConversionFromString()
		{
			Signal<string> signal = "hello";

			Assert.Equal("hello", signal.Value);
		}

		#endregion

		#region Thread-Safe Concurrent Writes

		[Fact]
		public async Task Signal_ConcurrentWrites_Decimal_NoTornReads()
		{
			var signal = new Signal<decimal>(0m);
			var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			var readValues = new List<decimal>();
			var readLock = new object();

			// Writer tasks: write known values concurrently
			var writers = new Task[4];
			for (int w = 0; w < writers.Length; w++)
			{
				int writerIndex = w;
				writers[w] = Task.Run(() =>
				{
					for (int i = 0; i < 1000; i++)
					{
						if (cts.Token.IsCancellationRequested) break;
						signal.Value = writerIndex * 1000m + i;
					}
				});
			}

			// Reader task: read values concurrently and verify they are valid
			var reader = Task.Run(() =>
			{
				for (int i = 0; i < 4000; i++)
				{
					if (cts.Token.IsCancellationRequested) break;
					var val = signal.Peek(); // use Peek to avoid scope tracking overhead
					lock (readLock) { readValues.Add(val); }
				}
			});

			await Task.WhenAll(writers);
			cts.Cancel();
			await reader;

			// All reads should be valid decimals (no torn reads would produce NaN-like garbage)
			foreach (var val in readValues)
			{
				Assert.True(val >= 0m, $"Torn read detected: {val}");
			}
		}

		[Fact]
		public async Task Signal_ConcurrentWrites_LargeTuple_NoTornReads()
		{
			var signal = new Signal<(long, long, long)>((0L, 0L, 0L));
			var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			var tornReadDetected = false;

			var writers = new Task[4];
			for (int w = 0; w < writers.Length; w++)
			{
				long marker = (w + 1) * 1000;
				writers[w] = Task.Run(() =>
				{
					for (int i = 0; i < 1000; i++)
					{
						if (cts.Token.IsCancellationRequested) break;
						// All three fields from the same writer share the same marker prefix
						signal.Value = (marker + i, marker + i, marker + i);
					}
				});
			}

			var reader = Task.Run(() =>
			{
				for (int i = 0; i < 4000; i++)
				{
					if (cts.Token.IsCancellationRequested) break;
					var (a, b, c) = signal.Peek();
					// A non-torn read should have all three values equal
					if (a != b || b != c)
					{
						tornReadDetected = true;
						break;
					}
				}
			});

			await Task.WhenAll(writers);
			cts.Cancel();
			await reader;

			Assert.False(tornReadDetected, "Torn read detected in large tuple Signal");
		}

		#endregion

		#region Disposed Signal

		[Fact]
		public void Signal_Disposed_IgnoresNewSubscriptions()
		{
			var signal = new Signal<int>(10);
			signal.Dispose();

			int notifyCount = 0;
			var subscriber = new TestSubscriber(() => notifyCount++);
			signal.Subscribe(subscriber);

			// Even after subscribing post-dispose, writing should not notify
			// (the subscriber was silently ignored by Subscribe)
			signal.Value = 20;

			// The value does change (disposal doesn't prevent writes)
			// but notifications don't fire because subscribers were cleared
			Assert.Equal(0, notifyCount);
		}

		#endregion

		#region ToString

		[Fact]
		public void Signal_ToString_ReturnsValueString()
		{
			var signal = new Signal<int>(42);
			Assert.Equal("42", signal.ToString());
		}

		[Fact]
		public void Signal_ToString_NullValue_ReturnsEmpty()
		{
			var signal = new Signal<string?>(null);
			Assert.Equal("", signal.ToString());
		}

		#endregion

		#region DebugName

		[Fact]
		public void Signal_DebugName_CanBeSet()
		{
			var signal = new Signal<int>(0) { DebugName = "counter" };
			Assert.Equal("counter", signal.DebugName);
		}

		[Fact]
		public void Signal_DebugName_DefaultsToNull()
		{
			var signal = new Signal<int>(0);
			Assert.Null(signal.DebugName);
		}

		#endregion

		#region Test Helpers

		/// <summary>
		/// Minimal IReactiveSubscriber for testing notification counts.
		/// </summary>
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
