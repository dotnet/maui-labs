using System;
using System.Collections.Generic;
using Comet.Reactive;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Proves the fine-grained update path works correctly under rapid Slider-drag-style
	/// mutations. Prerequisite for the state unification proposal (skeptic issue #5).
	/// </summary>
	public class SliderDragIntegrationTests : TestBase
	{
		#region Test Views

		/// <summary>
		/// View with a Slider bound through a Func (fine-grained binding path).
		/// Body uses Reactive&lt;double&gt; inside a Func constructor so that the binding
		/// captures the dependency — updates go through Binding.BindingValueChanged
		/// rather than triggering a full body rebuild.
		/// </summary>
		class FuncBoundSliderView : View
		{
			public readonly Reactive<double> sliderValue = new Reactive<double>(0.0);
			public int BodyCallCount;
			public Slider CapturedSlider;

			public FuncBoundSliderView()
			{
				Body = () =>
				{
					BodyCallCount++;
					CapturedSlider = new Slider(() => sliderValue.Value);
					return CapturedSlider;
				};
			}
		}

		/// <summary>
		/// View with two Reactive signals and a cross-write callback simulating
		/// two-way binding (Slider writes → display label reads).
		/// </summary>
		class TwoWayBindingView : View
		{
			public readonly Reactive<double> sliderValue = new Reactive<double>(0.0);
			public readonly Reactive<double> displayValue = new Reactive<double>(0.0);
			public int BodyCallCount;

			public TwoWayBindingView()
			{
				Body = () =>
				{
					BodyCallCount++;
					return new VStack
					{
						new Slider(() => sliderValue.Value),
						new Text(() => $"{displayValue.Value:F1}"),
					};
				};
			}
		}

		/// <summary>
		/// View with body-level Signal (pageIndex) and property-level Reactive (sliderValue).
		/// Used to prove that after a body rebuild (pageIndex change), fine-grained
		/// subscriptions for the Slider still work.
		/// </summary>
		class MixedDependencyView : View
		{
			public readonly Signal<int> pageIndex = new(0);
			public readonly Reactive<double> sliderValue = new Reactive<double>(0.0);
			public int BodyCallCount;
			public Slider CapturedSlider;

			public MixedDependencyView()
			{
				Body = () =>
				{
					BodyCallCount++;
					var header = new Text($"Page {pageIndex.Value}");
					CapturedSlider = new Slider(() => sliderValue.Value);
					return new VStack
					{
						header,
						CapturedSlider,
					};
				};
			}
		}

		#endregion

		#region Test 1: Rapid Signal Writes Don't Trigger Body Rebuild

		[Fact]
		public void RapidStateWrites_UseFinegrainedPath_NotBodyRebuild()
		{
			var view = new FuncBoundSliderView();
			var viewHandler = view.SetViewHandlerToGeneric();
			var sliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();

			// After initial build, body should have been called exactly once
			Assert.Equal(1, view.BodyCallCount);

			// Simulate 60 rapid slider drag updates
			for (int i = 1; i <= 60; i++)
			{
				view.sliderValue.Value = i / 60.0;
			}

			// Body should NOT have been rebuilt — fine-grained binding path
			// updates the Slider's Value property directly
			Assert.Equal(1, view.BodyCallCount);

			// The Slider's binding should reflect the latest value
			Assert.Equal(60.0 / 60.0, view.CapturedSlider.Value.CurrentValue, 5);

			// The handler should have been notified of property changes
			Assert.True(sliderHandler.ChangedProperties.ContainsKey(nameof(IRange.Value)),
				"Slider handler should have received Value updates via fine-grained path");
		}

		#endregion

		#region Test 2: No Deadlocks Under Two-Way Binding

		[Fact]
		public void TwoWayBinding_NoDeadlock_UnderRapidUpdates()
		{
			var view = new TwoWayBindingView();
			var viewHandler = view.SetViewHandlerToGeneric();

			// Set up cross-write: when sliderValue changes, mirror to displayValue
			view.sliderValue.ValueChanged += newValue =>
			{
				view.displayValue.Value = newValue;
			};

			int initialBodyCount = view.BodyCallCount;

			// Simulate 60 rapid updates — this would deadlock if State
			// change notifications aren't re-entrant safe
			for (int i = 1; i <= 60; i++)
			{
				view.sliderValue.Value = i / 60.0;
			}

			// Primary assertion: test completed (no deadlock / infinite loop)
			// Secondary: displayValue should match the last written sliderValue
			Assert.Equal(60.0 / 60.0, view.displayValue.Value, 5);
			Assert.Equal(view.sliderValue.Value, view.displayValue.Value, 5);
		}

		#endregion

		#region Test 3: Handler Property Updates (Not View Replacement)

		[Fact]
		public void HandlerReference_PreservedAcross_FinegrainedUpdates()
		{
			var view = new FuncBoundSliderView();
			var viewHandler = view.SetViewHandlerToGeneric();
			var sliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();

			// Capture the handler reference after initial build
			var originalHandler = view.CapturedSlider.ViewHandler;
			Assert.NotNull(originalHandler);

			// Write 10 rapid updates through the fine-grained path
			for (int i = 1; i <= 10; i++)
			{
				view.sliderValue.Value = i / 10.0;
			}

			// The handler should be the exact same object — in-place update, not recreated
			Assert.Same(originalHandler, view.CapturedSlider.ViewHandler);

			// Body was not rebuilt (no view replacement)
			Assert.Equal(1, view.BodyCallCount);
		}

		#endregion

		#region Test 4: Body Rebuild Preserves Fine-Grained Subscriptions

		[Fact]
		public void BodyRebuild_PreservesFinegrained_SliderSubscriptions()
		{
			var view = new MixedDependencyView();
			var viewHandler = view.SetViewHandlerToGeneric();

			Assert.Equal(1, view.BodyCallCount);

			// Change pageIndex — this IS read directly in body (string interpolation),
			// so it should trigger a body rebuild via ReactiveScheduler
			view.pageIndex.Value = 1;
			ReactiveScheduler.FlushSync();

			int bodyCountAfterPageChange = view.BodyCallCount;
			Assert.True(bodyCountAfterPageChange >= 2,
				$"Body should have rebuilt after pageIndex change, but was called {bodyCountAfterPageChange} times");

			// Re-attach handler to the new Slider instance created by body rebuild
			var newSliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();

			// Now write to sliderValue 10 times — these should use the fine-grained path
			// (body should NOT rebuild again)
			for (int i = 1; i <= 10; i++)
			{
				view.sliderValue.Value = i / 10.0;
			}

			// Body should not have been called again — the fine-grained Binding
			// subscriptions should still work after the body rebuild
			Assert.Equal(bodyCountAfterPageChange, view.BodyCallCount);

			// Slider value should reflect the latest write
			Assert.Equal(10.0 / 10.0, view.CapturedSlider.Value.CurrentValue, 5);

			// Handler should have received updates
			Assert.True(newSliderHandler.ChangedProperties.ContainsKey(nameof(IRange.Value)),
				"Slider handler should have received Value updates after body rebuild");
		}

		#endregion
	}
}
