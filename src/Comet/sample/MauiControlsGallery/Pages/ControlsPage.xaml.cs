namespace MauiControlsGallery.Pages;

public partial class ControlsPage : ContentPage
{
	int clickCount;

	public ControlsPage()
	{
		InitializeComponent();
	}

	void OnClickButtonClicked(object? sender, EventArgs e)
	{
		clickCount++;
		ClickCountLabel.Text = $"Clicks: {clickCount}";
		ClickProgress.Progress = Math.Min(1.0, clickCount / 20.0);
	}

	void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
	{
		EntryEcho.Text = $"Echo: {e.NewTextValue}";
	}

	void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
	{
		SliderLabel.Text = $"Slider: {e.NewValue:F0}";
	}

	void OnSwitchToggled(object? sender, ToggledEventArgs e)
	{
		SwitchLabel.Text = e.Value ? "On" : "Off";
	}

	void OnCheckBoxChanged(object? sender, CheckedChangedEventArgs e)
	{
		CheckBoxLabel.Text = e.Value ? "Checked" : "Unchecked";
	}

	void OnStepperValueChanged(object? sender, ValueChangedEventArgs e)
	{
		StepperLabel.Text = $"Stepper: {e.NewValue:F0}";
	}
}
