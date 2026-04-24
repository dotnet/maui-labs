using System.Threading;
using Microsoft.Maui.ApplicationModel;
using static Comet.CometControls;
using Comet.Reactive;

namespace Comet.Samples
{
	public class ProgressBarSample1 : Component
	{
		readonly Signal<double> percentage = new(.1);
		private readonly Timer _timer;

		public ProgressBarSample1()
		{
			_timer = new Timer(state => {
				var p = (Signal<double>)state;
				var current = p.Peek();
				var value = current < 1 ? current + .001f : 0;
				MainThread.BeginInvokeOnMainThread(() => p.Value = value);
			}, percentage, 100, 100);
		}

				public override View Render() => VStack(
			ProgressBar(percentage),
			Text(()=>$"{percentage.Value.ToString("P2")}")
		);

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			// TODO: Stop when lifecycle events for views are available
			_timer.Dispose();
		}
	}
}
