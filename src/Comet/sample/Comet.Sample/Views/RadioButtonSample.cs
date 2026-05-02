using static Comet.CometControls;

﻿namespace Comet.Samples
{
	// Known issue: Comet's RadioButton does not implement IRadioButton, so MAUI's
	// RadioButtonHandler throws InvalidCastException at runtime. Fixing this requires
	// reconciling Comet's container-based grouping model (RadioGroup) with MAUI's
	// property-based model (GroupName). See ControlsGenerator.cs line 20 where the
	// CometGenerate attribute for IRadioButton is intentionally commented out.
	public class RadioButtonSample : Component
	{
				public override View Render() => VStack(
			Text("RadioButton Sample")
				.FontSize(24),
			Text("Known Issue: RadioButton is not yet compatible with MAUI's RadioButtonHandler.")
				.Color(Colors.Orange),
			Text("Comet's RadioButton needs to implement IRadioButton to work with the native handler. "
				+ "This requires reconciling Comet's RadioGroup container model with MAUI's GroupName-based approach.")
				.FontSize(14)
		);
	}
}
