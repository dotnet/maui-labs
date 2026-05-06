using System.Windows;
using WStackPanel = System.Windows.Controls.StackPanel;
using WButton = System.Windows.Controls.Button;
using WTextBlock = System.Windows.Controls.TextBlock;
using WOrientation = System.Windows.Controls.Orientation;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class StepperHandler : WPFViewHandler<IStepper, WStackPanel>
	{
		WButton _minusButton = null!;
		WButton _plusButton = null!;
		WTextBlock _valueLabel = null!;
		double _increment = 1.0;

		protected override WStackPanel CreatePlatformView()
		{
			var panel = new WStackPanel { Orientation = WOrientation.Horizontal };
			_minusButton = new WButton { Content = "−", Width = 40, Height = 30 };
			_valueLabel = new WTextBlock
			{
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				Margin = new System.Windows.Thickness(4, 0, 4, 0),
				Text = "0",
			};
			_plusButton = new WButton { Content = "+", Width = 40, Height = 30 };
			panel.Children.Add(_minusButton);
			panel.Children.Add(_valueLabel);
			panel.Children.Add(_plusButton);
			return panel;
		}

		protected override void ConnectHandler(WStackPanel platformView)
		{
			base.ConnectHandler(platformView);
			_minusButton.Click += OnMinusClicked;
			_plusButton.Click += OnPlusClicked;
		}

		protected override void DisconnectHandler(WStackPanel platformView)
		{
			_minusButton.Click -= OnMinusClicked;
			_plusButton.Click -= OnPlusClicked;
			base.DisconnectHandler(platformView);
		}

		void OnMinusClicked(object sender, System.Windows.RoutedEventArgs e)
		{
			if (VirtualView == null) return;
			var newValue = VirtualView.Value - _increment;
			if (newValue >= VirtualView.Minimum)
				VirtualView.Value = newValue;
		}

		void OnPlusClicked(object sender, System.Windows.RoutedEventArgs e)
		{
			if (VirtualView == null) return;
			var newValue = VirtualView.Value + _increment;
			if (newValue <= VirtualView.Maximum)
				VirtualView.Value = newValue;
		}

		void UpdateButtonStates()
		{
			if (VirtualView == null) return;
			_minusButton.IsEnabled = VirtualView.Value > VirtualView.Minimum;
			_plusButton.IsEnabled = VirtualView.Value < VirtualView.Maximum;
		}

		public static void MapValue(StepperHandler handler, IStepper stepper)
		{
			handler._valueLabel.Text = stepper.Value.ToString("G");
			handler.UpdateButtonStates();
		}

		public static void MapMinimum(StepperHandler handler, IStepper stepper)
		{
			handler.UpdateButtonStates();
		}

		public static void MapMaximum(StepperHandler handler, IStepper stepper)
		{
			handler.UpdateButtonStates();
		}

		public static void MapIncrement(StepperHandler handler, IStepper stepper)
		{
			handler._increment = stepper.Interval > 0 ? stepper.Interval : 1.0;
		}
	}
}
