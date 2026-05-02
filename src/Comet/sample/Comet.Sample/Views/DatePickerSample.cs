using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class DatePickerSample : Component
	{
		readonly Reactive<DateTime?> currentDate = DateTime.Today;
		public DatePickerSample()
		{
			currentDate.PropertyChanged += CurrentDate_PropertyChanged;
		}

				public override View Render() => VStack(
			DatePicker(currentDate,
				minimumDate: new DateTime(2015, 10, 1),
				maximumDate: new DateTime(2018, 10, 01)).Format("dd/MM/yyyy")
			.Frame(width: 200)
		);



		private void CurrentDate_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			Console.WriteLine((sender as Reactive<DateTime?>)?.Value);
		}
	}
}
