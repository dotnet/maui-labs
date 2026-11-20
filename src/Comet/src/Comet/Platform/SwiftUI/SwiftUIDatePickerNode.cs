#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>Renders a Comet <see cref="Comet.DatePicker"/> as a REAL native SwiftUI
	/// <c>DatePicker</c> control — the actual iOS date picker wheel/calendar, not a text
	/// stub. When <c>IsOpen</c> is set, the dialog variant presents as a sheet with a
	/// graphical date picker; otherwise the compact inline picker renders in-flow.
	/// Date changes route back through <see cref="EventIds.DateConfirmed"/> as epoch seconds.</summary>
	sealed class SwiftUIDatePickerNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;
		ICometEventSink? _sink;
		Comet.DatePicker _picker;
		bool _isOpen;
		bool _isDialogMode;

		public CometNode Native => _native;

		public SwiftUIDatePickerNode(Comet.DatePicker picker, BackendContext context)
		{
			_picker = picker;
			_native = CometSwiftUIHost.MakeNode("datepicker");

			// Seed initial date as epoch seconds.
			var initial = picker.Date?.CurrentValue ?? System.DateTime.Today;
			if (initial != default)
			{
				var epoch = DatePickerDateProtocol.ToUnixSeconds(initial);
				CometSwiftUIHost.SetDouble(_native, "dateepoch", epoch);
			}

			CometSwiftUIHost.SetDateChangedHandler(_native, OnNativeDateChanged);
			CometSwiftUIHost.SetDialogDismissHandler(_native, OnNativeDismiss);
		}

		void OnNativeDateChanged(double epochSeconds)
		{
			var dt = DatePickerDateProtocol.FromUnixSeconds((long)epochSeconds);
			_sink?.OnEvent(EventIds.DateConfirmed, dt.Ticks);
		}

		void OnNativeDismiss()
		{
			_sink?.OnEvent(EventIds.DialogDismissed);
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (SwiftUINode.ApplyCommonProperty(_native, id, in value))
				return;

			if (id == PropertyIds.DatePicker_IsOpen)
			{
				_isOpen = value.AsBool;
				CometSwiftUIHost.SetBool(_native, "datepickeropen", value.AsBool);
			}
			else if (id == PropertyIds.DatePicker_IsDialogMode)
			{
				_isDialogMode = value.AsBool;
				CometSwiftUIHost.SetBool(_native, "datepickerdialogmode", value.AsBool);
			}
			else if (id == PropertyIds.DatePicker_SelectedTicks)
			{
				var ticks = value.AsLong;
				if (ticks > 0)
				{
					var dt = new System.DateTime(ticks);
					var epoch = DatePickerDateProtocol.ToUnixSeconds(dt);
					CometSwiftUIHost.SetDouble(_native, "dateepoch", epoch);
				}
			}
			else if (id == PropertyIds.DatePicker_MinimumTicks)
				SetRangeBound("datemin", value.AsLong);
			else if (id == PropertyIds.DatePicker_MaximumTicks)
				SetRangeBound("datemax", value.AsLong);
		}

		void SetRangeBound(string property, long ticks)
		{
			if (ticks <= 0)
			{
				CometSwiftUIHost.SetDouble(_native, property, double.NaN);
				return;
			}
			var date = new System.DateTime(ticks);
			var epoch = DatePickerDateProtocol.ToUnixSeconds(date);
			CometSwiftUIHost.SetDouble(_native, property, epoch);
		}

		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint)
		{
			var presentation = DatePickerPresentationState.Resolve(
				_isDialogMode,
				_isOpen);
			if (!presentation.MeasuresInline)
				return Size.Zero;
			var size = CometSwiftUIHost.MeasureNode(_native, widthConstraint, heightConstraint);
			if (size.Width > 0 && size.Height > 0)
				return new Size(size.Width, size.Height);

			using var nativePicker = new UIKit.UIDatePicker
			{
				Mode = UIKit.UIDatePickerMode.Date,
				PreferredDatePickerStyle = UIKit.UIDatePickerStyle.Compact,
			};
			var intrinsic = nativePicker.IntrinsicContentSize;
			if (intrinsic.Width > 0 && intrinsic.Height > 0)
				return new Size(intrinsic.Width, intrinsic.Height);

			var availableWidth = double.IsFinite(widthConstraint) && widthConstraint > 0
				? widthConstraint
				: UIKit.UIScreen.MainScreen.Bounds.Width;
			var availableHeight = double.IsFinite(heightConstraint) && heightConstraint > 0
				? heightConstraint
				: UIKit.UIScreen.MainScreen.Bounds.Height;
			var fitted = nativePicker.SizeThatFits(
				new CoreGraphics.CGSize(availableWidth, availableHeight));
			return new Size(
				System.Math.Max(1, fitted.Width),
				System.Math.Max(1, fitted.Height));
		}

		public void Arrange(Rect frame)
		{
			var nativeFrame = DatePickerPresentationState.Resolve(
				_isDialogMode,
				_isOpen).NativeFrame(frame);
			CometSwiftUIHost.SetFrame(
				_native,
				nativeFrame.X,
				nativeFrame.Y,
				nativeFrame.Width,
				nativeFrame.Height);
		}

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is Comet.DatePicker dp)
				_picker = dp;
		}

		public void Dispose() { }
	}
}
#endif
