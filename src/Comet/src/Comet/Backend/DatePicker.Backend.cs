#nullable enable
using System.ComponentModel;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	// Partial extension of the source-generated DatePicker (from IDatePicker).
	// Adds dialog-open signal and backend property emission for the native
	// DatePickerDialog (Compose) / DatePicker (SwiftUI) backends.
	public partial class DatePicker
	{
		/// <summary>Signal driving dialog presentation. When omitted, platforms that support
		/// an inline picker render it in-flow; when supplied, the picker uses dialog mode.</summary>
		public Signal<bool>? IsOpen { get; set; }

		INotifyPropertyChanged? _hookedIsOpen;
		PropertyChangedEventHandler? _isOpenChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			UpdateIsOpenSubscription();
			node.ApplyProperty(PropertyIds.DatePicker_IsDialogMode,
				PropertyValue.From(IsOpen is not null));
			node.ApplyProperty(PropertyIds.DatePicker_IsOpen,
				PropertyValue.From(IsOpen?.Peek() ?? false));
			var date = Date?.CurrentValue ?? System.DateTime.Today;
			node.ApplyProperty(PropertyIds.DatePicker_SelectedTicks, PropertyValue.From(date.Ticks));
			var minimum = MinimumDate?.CurrentValue;
			var maximum = MaximumDate?.CurrentValue;
			node.ApplyProperty(PropertyIds.DatePicker_MinimumTicks,
				PropertyValue.From(minimum?.Ticks ?? 0L));
			node.ApplyProperty(PropertyIds.DatePicker_MaximumTicks,
				PropertyValue.From(maximum?.Ticks ?? 0L));
		}

		void UpdateIsOpenSubscription()
		{
			if (ReferenceEquals(_hookedIsOpen, IsOpen))
				return;
			DetachIsOpenSubscription();
			_hookedIsOpen = IsOpen;
			if (_hookedIsOpen is null)
				return;

			var signal = IsOpen;
			if (signal is null)
				return;
			var owner = new WeakReference<DatePicker>(this);
			PropertyChangedEventHandler? handler = null;
			handler = (_, _) =>
			{
				if (!owner.TryGetTarget(out var picker))
				{
					if (handler is not null)
						signal.PropertyChanged -= handler;
					return;
				}

				if (ReferenceEquals(picker.IsOpen, signal))
					picker.Node?.ApplyProperty(
						PropertyIds.DatePicker_IsOpen,
						PropertyValue.From(signal.Peek()));
			};
			_isOpenChanged = handler;
			signal.PropertyChanged += handler;
		}

		void DetachIsOpenSubscription()
		{
			if (_hookedIsOpen is not null && _isOpenChanged is not null)
				_hookedIsOpen.PropertyChanged -= _isOpenChanged;
			_hookedIsOpen = null;
			_isOpenChanged = null;
		}

		protected internal override void OnBackendEvent(EventId id)
		{
			if (id == EventIds.DialogDismissed && IsOpen is { } open)
				open.Value = false;
		}

		protected internal override void OnBackendEvent<T>(EventId id, T payload)
		{
			if (id == EventIds.DateConfirmed && payload is long ticks)
			{
				Node?.ApplyProperty(PropertyIds.DatePicker_SelectedTicks, PropertyValue.From(ticks));
				Date?.Set(new System.DateTime(ticks));
				if (IsOpen is { } open)
					open.Value = false;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DetachIsOpenSubscription();
			base.Dispose(disposing);
		}
	}

	internal readonly struct DatePickerPresentationState
	{
		DatePickerPresentationState(bool isDialogMode, bool isOpen)
		{
			IsDialogMode = isDialogMode;
			IsOpen = isOpen;
		}

		public bool IsDialogMode { get; }
		public bool IsOpen { get; }
		public bool RendersInline => !IsDialogMode;
		public bool PresentsDialog => IsDialogMode && IsOpen;
		public bool MeasuresInline => !IsDialogMode;
		public Rect NativeFrame(Rect allocatedFrame)
			=> IsDialogMode
				? new Rect(allocatedFrame.X, allocatedFrame.Y, 0, 0)
				: allocatedFrame;

		public static DatePickerPresentationState Resolve(
			bool isDialogMode,
			bool isOpen)
			=> new(isDialogMode, isOpen);
	}

	internal static class DatePickerDateProtocol
	{
		public static long ToUnixSeconds(System.DateTime date)
			=> new System.DateTimeOffset(
				System.DateTime.SpecifyKind(date.Date, System.DateTimeKind.Utc))
				.ToUnixTimeSeconds();

		public static System.DateTime FromUnixSeconds(long seconds)
			=> System.DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.Date;
	}
}
