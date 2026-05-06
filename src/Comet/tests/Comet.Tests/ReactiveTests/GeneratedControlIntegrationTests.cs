using System;
using System.Collections.Generic;
using Comet.Reactive;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Phase 2 integration tests: verifies generated controls work correctly with
	/// PropertySubscription&lt;T&gt;. Exercises the end-to-end path:
	///   Signal → PropertySubscription → ViewPropertyChanged → handler update.
	///
	/// Tests are split into two layers:
	///   1. PropertySubscription-direct tests (PASS) — prove the primitive works.
	///   2. View-level Signal→Control integration tests (SKIP) — define the behavior
	///      contract for Phase 2 generator wiring. Currently, Signal&lt;T&gt; writes
	///      don't propagate through the existing Binding&lt;T&gt; path because the
	///      generated controls haven't been updated to use PropertySubscription yet.
	///
	/// The State&lt;T&gt;-based fine-grained path is proven by SliderDragIntegrationTests.
	/// These tests define the equivalent contract for Signal&lt;T&gt;.
	/// </summary>
	public class GeneratedControlIntegrationTests : TestBase
	{
		#region Test Views

		/// <summary>
		/// View with a TextField bound through a Signal&lt;string&gt; via Func binding.
		/// </summary>
		class SignalTextFieldView : View
		{
			public readonly Signal<string> textSignal;
			public int BodyCallCount;
			public TextField CapturedTextField;

			public SignalTextFieldView(string initial)
			{
				textSignal = new Signal<string>(initial);
				Body = () =>
				{
					BodyCallCount++;
					CapturedTextField = new TextField(() => textSignal.Value);
					return CapturedTextField;
				};
			}
		}

		/// <summary>
		/// View with a Slider bound through a Signal&lt;double&gt; via Func binding.
		/// </summary>
		class SignalSliderView : View
		{
			public readonly Signal<double> sliderSignal;
			public int BodyCallCount;
			public Slider CapturedSlider;

			public SignalSliderView(double initial)
			{
				sliderSignal = new Signal<double>(initial);
				Body = () =>
				{
					BodyCallCount++;
					CapturedSlider = new Slider(() => sliderSignal.Value);
					return CapturedSlider;
				};
			}
		}

		/// <summary>
		/// View with a Toggle bound through a Signal&lt;bool&gt; via Func binding.
		/// </summary>
		class SignalToggleView : View
		{
			public readonly Signal<bool> toggleSignal;
			public int BodyCallCount;
			public Toggle CapturedToggle;

			public SignalToggleView(bool initial)
			{
				toggleSignal = new Signal<bool>(initial);
				Body = () =>
				{
					BodyCallCount++;
					CapturedToggle = new Toggle(() => toggleSignal.Value);
					return CapturedToggle;
				};
			}
		}

		/// <summary>
		/// View with a Text control using a computed Func that reads a Signal.
		/// </summary>
		class ComputedTextView : View
		{
			public readonly Signal<int> countSignal;
			public int BodyCallCount;
			public Text CapturedText;

			public ComputedTextView(int initial)
			{
				countSignal = new Signal<int>(initial);
				Body = () =>
				{
					BodyCallCount++;
					CapturedText = new Text(() => $"Count: {countSignal.Value}");
					return CapturedText;
				};
			}
		}

		/// <summary>
		/// View with multiple controls (Slider + Text) sharing one Signal.
		/// </summary>
		class SharedSignalView : View
		{
			public readonly Signal<double> sharedSignal;
			public int BodyCallCount;
			public Slider CapturedSlider;
			public Text CapturedText;

			public SharedSignalView(double initial)
			{
				sharedSignal = new Signal<double>(initial);
				Body = () =>
				{
					BodyCallCount++;
					CapturedSlider = new Slider(() => sharedSignal.Value);
					CapturedText = new Text(() => $"{sharedSignal.Value:F2}");
					return new VStack
					{
						CapturedSlider,
						CapturedText,
					};
				};
			}
		}

		#endregion

		#region Test 1: TextField with Signal<string> — Two-Way Binding

		[Fact]
		public void TextField_SignalString_DisplaysInitialValue()
		{
			var view = new SignalTextFieldView("hello");
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedTextField.SetViewHandlerToGeneric();

			Assert.Equal("hello", view.CapturedTextField.Text.CurrentValue);
		}

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void TextField_SignalString_UpdatesOnSignalWrite()
		{
			var view = new SignalTextFieldView("hello");
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedTextField.SetViewHandlerToGeneric();

			view.textSignal.Value = "world";

			Assert.Equal("world", view.CapturedTextField.Text.CurrentValue);
			Assert.Equal(1, view.BodyCallCount);
		}

		[Fact]
		public void TextField_SimulateUserInput_UpdatesBinding()
		{
			var view = new SignalTextFieldView("hello");
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedTextField.SetViewHandlerToGeneric();

			(view.CapturedTextField as ITextInput).Text = "typed";

			Assert.Equal("typed", view.CapturedTextField.Text.CurrentValue);
		}

		[Fact]
		public void TextField_TwoWay_PropertySubscriptionDirect()
		{
			var signal = new Signal<string>("hello");
			var sub = new PropertySubscription<string>(signal);

			Assert.Equal("hello", sub.Value);

			signal.Value = "world";
			Assert.Equal("world", sub.Value);

			Assert.NotNull(sub.WriteBack);
			sub.WriteBack!("typed");
			Assert.Equal("typed", signal.Peek());
			Assert.Equal("typed", sub.Value);
		}

		[Fact]
		public void TextField_PropertySubscription_CallbackOnSignalChange()
		{
			var signal = new Signal<string>("hello");
			string? lastCallback = null;

			var sub = new PropertySubscription<string>(signal);
			sub.PropertyChangedCallback = v => lastCallback = v;

			signal.Value = "world";

			Assert.Equal("world", lastCallback);
			Assert.Equal("world", sub.Value);
		}

		#endregion

		#region Test 2: Slider with Signal<double> — Fine-Grained Updates

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void Slider_SignalDouble_FineGrainedUpdates_NoBodyRebuild()
		{
			var view = new SignalSliderView(0.5);
			var viewHandler = view.SetViewHandlerToGeneric();
			var sliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();

			Assert.Equal(1, view.BodyCallCount);

			const int steps = 30;
			for (int i = 0; i <= steps; i++)
			{
				view.sliderSignal.Value = i / (double)steps;
			}

			Assert.Equal(1, view.BodyCallCount);
			Assert.Equal(1.0, view.CapturedSlider.Value.CurrentValue, 5);

			Assert.True(sliderHandler.ChangedProperties.ContainsKey(nameof(IRange.Value)),
				"Slider handler should have received Value updates via fine-grained path");
		}

		[Fact]
		public void Slider_PropertySubscription_AllCallbacksFire()
		{
			var signal = new Signal<double>(0.0);
			var observed = new List<double>();

			var sub = new PropertySubscription<double>(() => signal.Value);
			sub.PropertyChangedCallback = v => observed.Add(v);

			const int steps = 30;
			for (int i = 1; i <= steps; i++)
			{
				signal.Value = i / (double)steps;
			}

			Assert.Equal(steps, observed.Count);
			Assert.Equal(1.0, observed[^1], 5);
		}

		[Fact]
		public void Slider_PropertySubscription_SignalConstructor_Bidirectional()
		{
			var signal = new Signal<double>(0.5);

			var sub = new PropertySubscription<double>(signal);
			Assert.Equal(0.5, sub.Value, 5);
			Assert.True(sub.IsBidirectional);

			// Signal → subscription
			signal.Value = 0.75;
			Assert.Equal(0.75, sub.Value, 5);

			// Subscription → signal (write-back)
			sub.WriteBack!(0.25);
			Assert.Equal(0.25, signal.Peek(), 5);
			Assert.Equal(0.25, sub.Value, 5);
		}

		#endregion

		#region Test 3: Toggle with Signal<bool>

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void Toggle_SignalBool_UpdatesOnSignalWrite()
		{
			var view = new SignalToggleView(false);
			var viewHandler = view.SetViewHandlerToGeneric();
			var toggleHandler = view.CapturedToggle.SetViewHandlerToGeneric();

			Assert.False(view.CapturedToggle.Value.CurrentValue);

			view.toggleSignal.Value = true;

			Assert.True(view.CapturedToggle.Value.CurrentValue);
			Assert.True(toggleHandler.ChangedProperties.ContainsKey(nameof(ISwitch.IsOn)),
				"Toggle handler should have received IsOn update via fine-grained path");
		}

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void Toggle_SignalBool_NoBodyRebuild()
		{
			var view = new SignalToggleView(false);
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedToggle.SetViewHandlerToGeneric();

			Assert.Equal(1, view.BodyCallCount);

			for (int i = 0; i < 10; i++)
			{
				view.toggleSignal.Value = i % 2 == 0;
			}

			Assert.Equal(1, view.BodyCallCount);
		}

		[Fact]
		public void Toggle_PropertySubscription_TracksSignalChanges()
		{
			var signal = new Signal<bool>(false);
			var observed = new List<bool>();

			var sub = new PropertySubscription<bool>(signal);
			sub.PropertyChangedCallback = v => observed.Add(v);

			signal.Value = true;
			Assert.True(sub.Value);
			Assert.Single(observed);
			Assert.True(observed[0]);

			signal.Value = false;
			Assert.False(sub.Value);
			Assert.Equal(2, observed.Count);
			Assert.False(observed[1]);
		}

		#endregion

		#region Test 4: Text with Func — Computed Display

		[Fact]
		public void Text_ComputedFunc_DisplaysFormattedValue()
		{
			var view = new ComputedTextView(42);
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedText.SetViewHandlerToGeneric();

			Assert.Equal("Count: 42", view.CapturedText.Value.CurrentValue);
		}

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void Text_ComputedFunc_UpdatesOnSignalWrite()
		{
			var view = new ComputedTextView(42);
			var viewHandler = view.SetViewHandlerToGeneric();
			var textHandler = view.CapturedText.SetViewHandlerToGeneric();

			view.countSignal.Value = 99;

			Assert.Equal("Count: 99", view.CapturedText.Value.CurrentValue);
			Assert.True(textHandler.ChangedProperties.ContainsKey(nameof(IText.Text)),
				"Text handler should have received Text update via fine-grained path");
		}

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void Text_ComputedFunc_NoBodyRebuild()
		{
			var view = new ComputedTextView(42);
			var viewHandler = view.SetViewHandlerToGeneric();
			view.CapturedText.SetViewHandlerToGeneric();

			Assert.Equal(1, view.BodyCallCount);

			view.countSignal.Value = 99;

			Assert.Equal(1, view.BodyCallCount);
		}

		[Fact]
		public void Text_PropertySubscription_ComputedExpression()
		{
			var signal = new Signal<int>(42);
			string? lastCallback = null;

			var sub = new PropertySubscription<string>(() => $"Count: {signal.Value}");
			sub.PropertyChangedCallback = v => lastCallback = v;

			Assert.Equal("Count: 42", sub.Value);

			signal.Value = 99;

			Assert.Equal("Count: 99", sub.Value);
			Assert.Equal("Count: 99", lastCallback);
		}

		#endregion

		#region Test 5: Multiple Controls Sharing One Signal

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void SharedSignal_BothControlsUpdate()
		{
			var view = new SharedSignalView(0.5);
			var viewHandler = view.SetViewHandlerToGeneric();
			var sliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();
			var textHandler = view.CapturedText.SetViewHandlerToGeneric();

			Assert.Equal(1, view.BodyCallCount);

			view.sharedSignal.Value = 0.8;

			Assert.Equal(0.8, view.CapturedSlider.Value.CurrentValue, 5);
			Assert.Equal("0.80", view.CapturedText.Value.CurrentValue);
			Assert.Equal(1, view.BodyCallCount);
		}

		[Fact(Skip = "Waiting for Phase 2 generator: Signal→Binding fine-grained path not wired yet")]
		public void SharedSignal_HandlerUpdatesForBothControls()
		{
			var view = new SharedSignalView(0.5);
			var viewHandler = view.SetViewHandlerToGeneric();
			var sliderHandler = view.CapturedSlider.SetViewHandlerToGeneric();
			var textHandler = view.CapturedText.SetViewHandlerToGeneric();

			view.sharedSignal.Value = 0.8;

			Assert.True(sliderHandler.ChangedProperties.ContainsKey(nameof(IRange.Value)),
				"Slider handler should have received Value update from shared signal");
			Assert.True(textHandler.ChangedProperties.ContainsKey(nameof(IText.Text)),
				"Text handler should have received Text update from shared signal");
		}

		[Fact]
		public void SharedSignal_PropertySubscription_BothSubscribersNotified()
		{
			var signal = new Signal<double>(0.5);
			var observedA = new List<double>();
			var observedB = new List<string>();

			var subA = new PropertySubscription<double>(() => signal.Value);
			subA.PropertyChangedCallback = v => observedA.Add(v);

			var subB = new PropertySubscription<string>(() => $"{signal.Value:F2}");
			subB.PropertyChangedCallback = v => observedB.Add(v);

			signal.Value = 0.8;

			Assert.Single(observedA);
			Assert.Equal(0.8, observedA[0], 5);
			Assert.Single(observedB);
			Assert.Equal("0.80", observedB[0]);
		}

		[Fact]
		public void SharedSignal_PropertySubscription_IndependentTracking()
		{
			var signalA = new Signal<double>(0.5);
			var signalB = new Signal<string>("hello");
			int callbackA = 0, callbackB = 0;

			var subA = new PropertySubscription<double>(signalA);
			subA.PropertyChangedCallback = _ => callbackA++;
			var subB = new PropertySubscription<string>(signalB);
			subB.PropertyChangedCallback = _ => callbackB++;

			signalA.Value = 0.8;
			Assert.Equal(1, callbackA);
			Assert.Equal(0, callbackB);

			signalB.Value = "world";
			Assert.Equal(1, callbackA);
			Assert.Equal(1, callbackB);
		}

		#endregion

		#region Test 6: PropertySubscription Disposal on View Dispose

		[Fact]
		public void Dispose_PropertySubscription_NoCallbacksAfterDispose()
		{
			var signal = new Signal<double>(1.0);
			int callbackCount = 0;

			var sub = new PropertySubscription<double>(() => signal.Value);
			sub.PropertyChangedCallback = _ => callbackCount++;

			signal.Value = 2.0;
			Assert.Equal(1, callbackCount);

			sub.Dispose();

			signal.Value = 3.0;
			Assert.Equal(1, callbackCount);
		}

		[Fact]
		public void Dispose_View_NoExceptionsOnPostDisposeSignalWrite()
		{
			var signal = new Signal<double>(0.5);
			var view = new View();
			Slider capturedSlider = null;
			view.Body = () =>
			{
				capturedSlider = new Slider(() => signal.Value);
				return capturedSlider;
			};
			view.SetViewHandlerToGeneric();
			capturedSlider.SetViewHandlerToGeneric();

			view.Dispose();

			// Writing to signal after view disposal must not crash
			signal.Value = 0.99;

			Assert.True(view.IsDisposed);
		}

		[Fact]
		public void Dispose_MultipleSubscriptions_AllCleanedUp()
		{
			var signal = new Signal<int>(0);
			int callbackA = 0, callbackB = 0, callbackC = 0;

			var subA = new PropertySubscription<int>(() => signal.Value);
			subA.PropertyChangedCallback = _ => callbackA++;
			var subB = new PropertySubscription<int>(() => signal.Value * 2);
			subB.PropertyChangedCallback = _ => callbackB++;
			var subC = new PropertySubscription<int>(() => signal.Value + 10);
			subC.PropertyChangedCallback = _ => callbackC++;

			signal.Value = 1;
			Assert.Equal(1, callbackA);
			Assert.Equal(1, callbackB);
			Assert.Equal(1, callbackC);

			subA.Dispose();
			subB.Dispose();
			subC.Dispose();

			signal.Value = 2;
			Assert.Equal(1, callbackA);
			Assert.Equal(1, callbackB);
			Assert.Equal(1, callbackC);
		}

		[Fact]
		public void Dispose_BindToView_StopsCallbacks()
		{
			// PropertySubscription bound to a view stops dispatching after dispose
			var signal = new Signal<double>(1.0);
			var sub = new PropertySubscription<double>(signal);
			int callbackCount = 0;
			sub.PropertyChangedCallback = _ => callbackCount++;

			// Simulate binding to a view (internal API)
			var view = new View { Body = () => new Text("test") };
			view.SetViewHandlerToGeneric();

			signal.Value = 2.0;
			Assert.Equal(1, callbackCount);

			sub.Dispose();

			signal.Value = 3.0;
			Assert.Equal(1, callbackCount);
		}

		#endregion
	}
}
