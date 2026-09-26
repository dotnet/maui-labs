#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeButton = AndroidX.Compose.Button;
using ComposeText = AndroidX.Compose.Text;
using ComposeDatePicker = AndroidX.Compose.DatePicker;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.DatePicker"/> as the real Material 3
	/// <c>DatePickerDialog</c> + <c>DatePicker</c> widgets. The dialog composes only
	/// while <c>DatePicker_IsOpen</c> is true (same zero-size overlay model as
	/// <see cref="ComposeAlertDialogNode"/>). Confirm writes the selection as ticks
	/// back through <see cref="EventIds.DateConfirmed"/>; cancel/dismiss writes
	/// <see cref="EventIds.DialogDismissed"/>.</summary>
	sealed class ComposeDatePickerNode : ComposeNode, IBackendManagesOwnContent
	{
		Comet.DatePicker _picker;
		readonly MutableState<bool> _open = new(false);
		readonly MutableState<int> _rangeVersion = new(0);
		readonly DateRangeSelectableDates _selectableDates = new();
		readonly DatePickerState _state;

		public ComposeDatePickerNode(Comet.DatePicker picker)
		{
			_picker = picker;
			var initial = picker.Date?.CurrentValue ?? System.DateTime.Today;
			_state = new DatePickerState(
				initialSelectedDateMillis: initial == default ? null : ToUtcMillis(initial),
				initialSelectableDates: _selectableDates);
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.DatePicker_IsOpen)
				_open.Value = value.AsBool;
			else if (id == PropertyIds.DatePicker_SelectedTicks)
				SetSelectedDate(value.AsLong);
			else if (id == PropertyIds.DatePicker_MinimumTicks)
			{
				SetMinimum(value.AsLong);
				_rangeVersion.Value++;
			}
			else if (id == PropertyIds.DatePicker_MaximumTicks)
			{
				SetMaximum(value.AsLong);
				_rangeVersion.Value++;
			}
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is Comet.DatePicker dp)
				_picker = dp;
		}

		public override void Render(IComposer composer)
		{
			_ = _rangeVersion.Value;
			if (!_open.Value)
				return;

			var confirmButton = new ComposeButton(onClick: () =>
			{
				var ms = _state.SelectedDateMillis;
				if (ms.HasValue)
				{
					var dt = System.DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).DateTime;
					Sink?.OnEvent(EventIds.DateConfirmed, dt.Ticks);
				}
				else
				{
					Sink?.OnEvent(EventIds.DialogDismissed);
				}
			});
			confirmButton.Add(new ComposeText("OK"));

			var dismissButton = new ComposeButton(onClick: () =>
				Sink?.OnEvent(EventIds.DialogDismissed));
			dismissButton.Add(new ComposeText("Cancel"));
			var picker = new ComposeDatePicker(_state)
			{
				// Dialog content must keep native modal measurement. Only attach semantics;
				// the owning Comet node's Yoga frame is a zero-size popup host.
				Modifier = BuildAutomationModifier(),
			};

			var dialog = new DatePickerDialog(
				onDismissRequest: () => Sink?.OnEvent(EventIds.DialogDismissed))
			{
				ConfirmButton = confirmButton,
				DismissButton = dismissButton,
				Body = picker,
			};
			dialog.Render(composer);
		}

		void SetMinimum(long ticks)
		{
			if (ticks <= 0)
			{
				_selectableDates.MinUtcMillis = null;
				_selectableDates.MinYear = null;
				return;
			}

			var date = new System.DateTime(ticks);
			_selectableDates.MinUtcMillis = ToUtcMillis(date);
			_selectableDates.MinYear = date.Year;
		}

		void SetSelectedDate(long ticks)
		{
			var millis = ticks > 0 ? ToUtcMillis(new System.DateTime(ticks)) : (long?)null;
			_state.InitialSelectedDateMillis = millis is long initial
				? Java.Lang.Long.ValueOf(initial)
				: null;
			_state.SelectedDateMillis = millis;
		}

		void SetMaximum(long ticks)
		{
			if (ticks <= 0)
			{
				_selectableDates.MaxUtcMillis = null;
				_selectableDates.MaxYear = null;
				return;
			}
			var date = new System.DateTime(ticks);
			_selectableDates.MaxUtcMillis = ToUtcMillis(date);
			_selectableDates.MaxYear = date.Year;
		}

		static long ToUtcMillis(System.DateTime date)
			=> new System.DateTimeOffset(
				System.DateTime.SpecifyKind(date.Date, System.DateTimeKind.Utc))
				.ToUnixTimeMilliseconds();
	}
}
#endif
