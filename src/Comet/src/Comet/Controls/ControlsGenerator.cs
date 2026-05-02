using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui;
using Comet;
//Property:NewPropertyName=DefaultValue
[assembly: CometGenerate(typeof(ITextButton), nameof(ITextButton.Text), nameof(IButton.Clicked), ClassName = "Button", Skip = new[] { $"{nameof(ITextStyle.TextColor)}:{EnvironmentKeys.Colors.Color}" }, Namespace = "Comet")]
[assembly: CometGenerate(typeof(IImageButton), nameof(IImageButton.Source), nameof(IButton.Clicked), ClassName = "ImageButton", Namespace = "Comet")]

//[assembly: CometGenerate(typeof(IBorder), BaseClass = "ContentView", Namespace = "Comet")]
[assembly: CometGenerate(typeof(IIndicatorView), nameof(IIndicatorView.Count), ClassName = "IndicatorView", Namespace = "Comet")]
[assembly: CometGenerate(typeof(IRefreshView), nameof(IRefreshView.IsRefreshing), ClassName = "RefreshView", Namespace = "Comet")]
[assembly: CometGenerate(typeof(ILabel), $"{nameof(ILabel.Text)}:Value", Namespace = "Comet", ClassName = "Text", DefaultValues = new[] { "MaxLines = 1" }, Skip = new[] { $"{nameof(ILabel.TextColor)}:{EnvironmentKeys.Colors.Color}", $"{nameof(ITextAlignment.HorizontalTextAlignment)}", $"{nameof(ITextAlignment.VerticalTextAlignment)}=TextAlignment.Center" })]

[assembly: CometGenerate(typeof(IEntry), nameof(IEntry.Text), nameof(IEntry.Placeholder), nameof(IEntry.Completed), ClassName = "SecureField", Skip = new[] { $"{nameof(ITextStyle.TextColor)}:{EnvironmentKeys.Colors.Color}", $"{nameof(IEntry.IsPassword)}= true", $"{nameof(ITextAlignment.HorizontalTextAlignment)}", $"{nameof(ITextAlignment.VerticalTextAlignment)}=TextAlignment.Center" }, DefaultValues = new[] { $"{nameof(ITextInput.MaxLength)}=-1" }, Namespace = "Comet")]
[assembly: CometGenerate(typeof(IActivityIndicator), nameof(IActivityIndicator.IsRunning), Namespace = "Comet", DefaultValues = new[] { $"{nameof(IActivityIndicator.IsRunning)}=true" })]
[assembly: CometGenerate(typeof(ICheckBox), nameof(ICheckBox.IsChecked), Namespace = "Comet")]
[assembly: CometGenerate(typeof(IDatePicker), nameof(IDatePicker.Date), nameof(IDatePicker.MinimumDate), nameof(IDatePicker.MaximumDate), Namespace = "Comet")]
[assembly: CometGenerate(typeof(IProgress), $"{nameof(IProgress.Progress)}:Value", ClassName = "ProgressBar", Namespace = "Comet")]
//[assembly: CometGenerate(typeof(IRadioButton), nameof(IRadioButton.IsChecked), Namespace = "Comet")]
[assembly: CometGenerate(typeof(ISearchBar), nameof(ISearchBar.Text), $"{nameof(ISearchBar.SearchButtonPressed)}:Search", Skip = new[] { $"{nameof(ITextStyle.TextColor)}:{EnvironmentKeys.Colors.Color}", $"{nameof(ITextAlignment.HorizontalTextAlignment)}", $"{nameof(ITextAlignment.VerticalTextAlignment)}=TextAlignment.Center" }, DefaultValues = new[] { $"{nameof(ITextInput.MaxLength)}=-1" }, Namespace = "Comet")]
[assembly: CometGenerate(typeof(IEditor), nameof(IEditor.Text), Skip = new[] { $"{nameof(ITextStyle.TextColor)}:{EnvironmentKeys.Colors.Color}" }, DefaultValues = new[] { $"{nameof(ITextInput.MaxLength)}=-1" }, ClassName = "TextEditor", Namespace = "Comet")]
[assembly: CometGenerate(typeof(IEntry), nameof(IEntry.Text), nameof(IEntry.Placeholder), nameof(IEntry.Completed), Skip = new[] { $"{nameof(ITextStyle.TextColor)}:{EnvironmentKeys.Colors.Color}", $"{nameof(ITextAlignment.HorizontalTextAlignment)}", $"{nameof(ITextAlignment.VerticalTextAlignment)}=TextAlignment.Center" }, DefaultValues = new[] { $"{nameof(ITextInput.MaxLength)}=-1" }, ClassName = "TextField", Namespace = "Comet")]
[assembly: CometGenerate(typeof(ISlider), nameof(ISlider.Value), $"{nameof(ISlider.Minimum)}=0", $"{nameof(ISlider.Maximum)}=1", Namespace = "Comet")]
[assembly: CometGenerate(typeof(ISwitch), $"{nameof(ISwitch.IsOn)}:Value", ClassName = "Toggle", Namespace = "Comet")]
[assembly: CometGenerate(typeof(ITimePicker), nameof(ITimePicker.Time), Namespace = "Comet")]
[assembly: CometGenerate(typeof(IStepper), nameof(IStepper.Value), nameof(IStepper.Minimum), nameof(IStepper.Maximum), nameof(IStepper.Interval), Namespace = "Comet")]
[assembly: CometGenerate(typeof(IToolbar), nameof(IToolbar.BackButtonVisible), nameof(IToolbar.IsVisible), Namespace = "Comet")]
[assembly: CometGenerate(typeof(IFlyoutView),  Namespace = "Comet", BaseClass = typeof(Comet.ContentView), DefaultValues = new[] { $"{nameof(IFlyoutView.FlyoutWidth)} = -1", $"{nameof(IFlyoutView.IsGestureEnabled)} = true", $"{nameof(IFlyoutView.FlyoutBehavior)} = FlyoutBehavior.Flyout" })]

// Per-control style state metadata (Design Decision D6).
// The source generator reads these to emit Configuration structs,
// scoped .{Control}Style() extensions, and ResolveCurrentStyle() methods.
[assembly: CometControlState(typeof(ITextButton), ControlName = "Button",
	States = new[] { "IsPressed", "IsHovered", "IsFocused" },
	ConfigProperties = new[] { "Label:string:Text" })]
[assembly: CometControlState(typeof(ISwitch), ControlName = "Toggle",
	States = new[] { "IsFocused" },
	ConfigProperties = new[] { "IsOn:bool:Value" })]
[assembly: CometControlState(typeof(ISlider), ControlName = "Slider",
	States = new[] { "IsDragging", "IsFocused" },
	ConfigProperties = new[] { "Value:double:Value", "Minimum:double:Minimum", "Maximum:double:Maximum" })]
[assembly: CometControlState(typeof(IEntry), ControlName = "TextField",
	States = new[] { "IsEditing", "IsFocused" },
	ConfigProperties = new[] { "Placeholder:string:Placeholder" })]
