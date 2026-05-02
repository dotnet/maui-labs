using System;
using System.Collections.Generic;
using Comet.Handlers;
using Comet.Styles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Platform;

#if WINDOWS
using Microsoft.Maui.Graphics.Win2D;
#endif

#if __MACOS__
using Microsoft.Maui.Platform.MacOS.Hosting;
// Redirect MAUI built-in handler types to Platform.Maui.MacOS implementations.
// The MAUI built-in handlers (Microsoft.Maui.Handlers.*) throw NotImplementedException
// on macOS. Platform.Maui.MacOS provides AppKit-based implementations.
using ActivityIndicatorHandler = Microsoft.Maui.Platform.MacOS.Handlers.ActivityIndicatorHandler;
using ButtonHandler = Microsoft.Maui.Platform.MacOS.Handlers.ButtonHandler;
using CheckBoxHandler = Microsoft.Maui.Platform.MacOS.Handlers.CheckBoxHandler;
using ContentViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.ContentViewHandler;
using DatePickerHandler = Microsoft.Maui.Platform.MacOS.Handlers.DatePickerHandler;
using EditorHandler = Microsoft.Maui.Platform.MacOS.Handlers.EditorHandler;
using EntryHandler = Microsoft.Maui.Platform.MacOS.Handlers.EntryHandler;
using GraphicsViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.GraphicsViewHandler;
using ImageButtonHandler = Microsoft.Maui.Platform.MacOS.Handlers.ImageButtonHandler;
using ImageHandler = Microsoft.Maui.Platform.MacOS.Handlers.ImageHandler;
using IndicatorViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.IndicatorViewHandler;
using LabelHandler = Microsoft.Maui.Platform.MacOS.Handlers.LabelHandler;
using LayoutHandler = Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler;
using PickerHandler = Microsoft.Maui.Platform.MacOS.Handlers.PickerHandler;
using ProgressBarHandler = Microsoft.Maui.Platform.MacOS.Handlers.ProgressBarHandler;
using RefreshViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.RefreshViewHandler;
using SearchBarHandler = Microsoft.Maui.Platform.MacOS.Handlers.SearchBarHandler;
using SliderHandler = Microsoft.Maui.Platform.MacOS.Handlers.SliderHandler;
using StepperHandler = Microsoft.Maui.Platform.MacOS.Handlers.StepperHandler;
using SwipeViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.SwipeViewHandler;
using SwitchHandler = Microsoft.Maui.Platform.MacOS.Handlers.SwitchHandler;
using TimePickerHandler = Microsoft.Maui.Platform.MacOS.Handlers.TimePickerHandler;
using WebViewHandler = Microsoft.Maui.Platform.MacOS.Handlers.WebViewHandler;
#endif

namespace Comet
{
	public static class AppHostBuilderExtensions
	{
		// Weak tables to track event subscriptions per platform view, preventing
		// duplicate handlers when mappers re-fire during SetVirtualView/Reload.
#if __IOS__ || MACCATALYST
		static readonly ConditionalWeakTable<UIKit.UITextField, EventHandler> _pickerEditingDidEndHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UITextField, EventHandler> _entryEditingChangedHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UITextView, EventHandler> _editorChangedHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UISearchBar, EventHandler<UIKit.UISearchBarTextChangedEventArgs>> _searchBarTextChangedHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UISlider, EventHandler> _sliderValueChangedHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UISwitch, EventHandler> _switchValueChangedHandlers = new();
		static readonly ConditionalWeakTable<UIKit.UIButton, EventHandler> _checkBoxCheckedHandlers = new();
#elif ANDROID
		static readonly ConditionalWeakTable<object, object> _pickerTextChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _entryTextChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _editorTextChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _searchBarTextChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _sliderValueChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _switchValueChangedHandlers = new();
		static readonly ConditionalWeakTable<object, object> _checkBoxCheckedHandlers = new();
#endif
		static void AddHandlers(this IMauiHandlersCollection collection, Dictionary<Type, Type> handlers) => handlers.ForEach(x => collection.AddHandler(x.Key, x.Value));
		public static MauiAppBuilder UseCometApp<TApp>(this MauiAppBuilder builder)
			where TApp : class, IApplication
		{
			builder.Services.TryAddSingleton<IApplication, TApp>();
			builder.UseCometHandlers();
			return builder;
		}
#if __MACOS__
		/// <summary>
		/// Registers a Comet app for macOS AppKit. Combines Platform.Maui.MacOS setup
		/// (UseMauiAppMacOS) with Comet handler overrides (UseCometHandlers).
		/// </summary>
		public static MauiAppBuilder UseCometAppMacOS<TApp>(this MauiAppBuilder builder)
			where TApp : class, IApplication
		{
			builder.UseMauiAppMacOS<TApp>();
			builder.UseCometHandlers();
			return builder;
		}
#endif
		public static MauiAppBuilder UseCometHandlers(this MauiAppBuilder builder)
		{

			//AnimationManger.SetTicker(new iOSTicker());

			// Initialize the token-based theme system with Material 3 defaults.
			// Fork the shared Defaults.Light via `with { }` so the framework's
			// ButtonStyles.Filled registration doesn't mutate the shared singleton
			// (records copy the _controlStyles ImmutableDictionary on `with`, so
			// subsequent SetControlStyle calls affect only this derived theme).
			var defaultTheme = Defaults.Light with { };
			defaultTheme.SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Filled);
			ThemeManager.SetTheme(defaultTheme);

			// Register style resolution mappers for controls BEFORE the
			// property-specific mappers so resolved values are available.
			RegisterStyleResolutionMappers();

			ViewHandler.ViewMapper.AppendToMapping(nameof(IGestureView.Gestures), CometViewHandler.AddGestures);
			ViewHandler.ViewCommandMapper.AppendToMapping(Gesture.AddGestureProperty, CometViewHandler.AddGesture);
			ViewHandler.ViewCommandMapper.AppendToMapping(Gesture.RemoveGestureProperty, CometViewHandler.RemoveGesture);
			ViewHandler.ViewMapper.AppendToMapping(nameof(IView.AutomationId), (handler, view) =>
			{
				if (view is View cometView)
					ApplyInspectionMetadata(handler, cometView);
			});
			ViewHandler.ViewMapper.AppendToMapping(nameof(IView.Visibility), (handler, view) =>
			{
				if (view is View cometView)
					ApplyInspectionMetadata(handler, cometView);
			});
			ViewHandler.ViewMapper.AppendToMapping(nameof(IView.IsEnabled), (handler, view) =>
			{
				if (view is View cometView)
					ApplyInspectionMetadata(handler, cometView);
			});
			ViewHandler.ViewMapper.AppendToMapping(nameof(IView.InputTransparent), (handler, view) =>
			{
				if (view is View cometView)
					ApplyInspectionMetadata(handler, cometView);
			});
			ViewHandler.ViewMapper.AppendToMapping(nameof(IView.Semantics), (handler, view) =>
			{
				if (view is View cometView)
					ApplyInspectionMetadata(handler, cometView);
			});

			// Apply shadow to any view that has it set via environment
			ViewHandler.ViewMapper.AppendToMapping("CometShadow", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var shadow = cometView.GetEnvironment<Comet.Graphics.Shadow>(EnvironmentKeys.View.Shadow);
				if (shadow is null)
					return;
#if __IOS__ || MACCATALYST
				var platformView = handler.PlatformView as UIKit.UIView;
				if (platformView is null)
					return;
				var layer = platformView.Layer;
				layer.ShadowOpacity = shadow.Opacity;
				layer.ShadowRadius = shadow.Radius;
				layer.ShadowOffset = new CoreGraphics.CGSize(shadow.Offset.X, shadow.Offset.Y);
				if (shadow.Paint is SolidPaint sp && sp.Color is not null)
					layer.ShadowColor = sp.Color.ToPlatform().CGColor;
				else
					layer.ShadowColor = UIKit.UIColor.Black.CGColor;
				layer.MasksToBounds = false;
#elif ANDROID
				var platformView = handler.PlatformView as global::Android.Views.View;
				if (platformView is null)
					return;
				var density = platformView.Context?.Resources?.DisplayMetrics?.Density ?? 1;
				platformView.Elevation = shadow.Radius * density;
#endif
			});

			// Apply border visual styling to Border's platform view via handler mapper
			LayoutHandler.Mapper.AppendToMapping("CometBorderStyling", (handler, view) =>
			{
				if (view is not Border border)
					return;
				var borderStroke = (IBorderStroke)border;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				var layer = platformView.Layer;
				if (borderStroke.Shape is RoundedRectangle rr)
				{
					layer.CornerRadius = rr.CornerRadius;
				}
				else if (borderStroke.Shape is not null)
				{
					layer.CornerRadius = 0;
				}
				layer.MasksToBounds = true;
				if (borderStroke.Stroke is SolidPaint sp && sp.Color is not null)
				{
					layer.BorderColor = sp.Color.ToPlatform().CGColor;
					layer.BorderWidth = (float)borderStroke.StrokeThickness;
				}
				else
				{
					layer.BorderWidth = 0;
				}
				var bg = border.GetBackground();
				if (bg is SolidPaint bgPaint && bgPaint.Color is not null)
				{
					layer.BackgroundColor = bgPaint.Color.ToPlatform().CGColor;
				}
#elif ANDROID
				var context = platformView.Context;
				if (context is not null)
				{
					var drawable = new global::Android.Graphics.Drawables.GradientDrawable();
					if (borderStroke.Shape is RoundedRectangle rr)
						drawable.SetCornerRadius((float)(rr.CornerRadius * context.Resources.DisplayMetrics.Density));
					if (borderStroke.Stroke is SolidPaint sp && sp.Color is not null)
						drawable.SetStroke((int)(borderStroke.StrokeThickness * context.Resources.DisplayMetrics.Density), sp.Color.ToPlatform());
					var bg = border.GetBackground();
					if (bg is SolidPaint bgPaint && bgPaint.Color is not null)
						drawable.SetColor(bgPaint.Color.ToPlatform());
					platformView.Background = drawable;
				}
#endif
			});

			// Apply PlaceholderColor to TextField/SecureField via handler mapper
			EntryHandler.Mapper.AppendToMapping("CometPlaceholderColor", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var color = cometView.GetEnvironment<Color>(EnvironmentKeys.Entry.PlaceholderColor);
				if (color is null)
					return;
				var entry = handler.PlatformView;
				if (entry is null)
					return;
#if __IOS__ || MACCATALYST
				entry.AttributedPlaceholder = new Foundation.NSAttributedString(
					entry.Placeholder ?? "",
					new UIKit.UIStringAttributes { ForegroundColor = color.ToPlatform() });
#elif ANDROID
				entry.SetHintTextColor(new global::Android.Content.Res.ColorStateList(
					new[] { Array.Empty<int>() },
					new[] { (int)color.ToPlatform() }));
#endif
			});

			// Apply Keyboard type to TextField via handler mapper
			EntryHandler.Mapper.AppendToMapping("CometKeyboard", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var keyboard = cometView.GetEnvironment<Microsoft.Maui.Keyboard>(EnvironmentKeys.Entry.Keyboard);
				if (keyboard is null)
					return;
				var entry = handler.PlatformView;
				if (entry is null)
					return;
#if __IOS__ || MACCATALYST
				entry.ApplyKeyboard(keyboard);
#elif ANDROID
				// Android keyboard handled via MAUI's IEntry.Keyboard interface
				if (view is IEntry entryView)
					EntryHandler.MapKeyboard(handler, entryView);
#endif
			});

			// Apply ReturnType to TextField via handler mapper
			EntryHandler.Mapper.AppendToMapping("CometReturnType", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var returnType = cometView.GetEnvironment<ReturnType?>(EnvironmentKeys.Entry.ReturnType);
				if (returnType is null)
					return;
				var entry = handler.PlatformView;
				if (entry is null)
					return;
#if __IOS__ || MACCATALYST
				entry.ReturnKeyType = returnType.Value switch
				{
					ReturnType.Go => UIKit.UIReturnKeyType.Go,
					ReturnType.Next => UIKit.UIReturnKeyType.Next,
					ReturnType.Search => UIKit.UIReturnKeyType.Search,
					ReturnType.Send => UIKit.UIReturnKeyType.Send,
					ReturnType.Done => UIKit.UIReturnKeyType.Done,
					_ => UIKit.UIReturnKeyType.Default,
				};
#endif
			});

			// Apply OnColor/ThumbColor to Toggle/Switch via handler mapper
			SwitchHandler.Mapper.AppendToMapping("CometSwitchColors", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				var onColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Switch.OnColor);
				if (onColor is not null)
					platformView.OnTintColor = onColor.ToPlatform();
				var thumbColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Switch.ThumbColor);
				if (thumbColor is not null)
					platformView.ThumbTintColor = thumbColor.ToPlatform();
#elif ANDROID
				var onColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Switch.OnColor);
				if (onColor is not null)
					platformView.TrackTintList = new global::Android.Content.Res.ColorStateList(
						new[] { new[] { global::Android.Resource.Attribute.StateChecked } },
						new[] { (int)onColor.ToPlatform() });
				var thumbColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Switch.ThumbColor);
				if (thumbColor is not null)
					platformView.ThumbTintList = new global::Android.Content.Res.ColorStateList(
						new[] { Array.Empty<int>() },
						new[] { (int)thumbColor.ToPlatform() });
#endif
			});

			// Apply Slider track/thumb colors via handler mapper
			SliderHandler.Mapper.AppendToMapping("CometSliderColors", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				var minTrackColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Slider.ProgressColor);
				if (minTrackColor is not null)
					platformView.MinimumTrackTintColor = minTrackColor.ToPlatform();
				var maxTrackColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Slider.TrackColor);
				if (maxTrackColor is not null)
					platformView.MaximumTrackTintColor = maxTrackColor.ToPlatform();
				var thumbColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Slider.ThumbColor);
				if (thumbColor is not null)
					platformView.ThumbTintColor = thumbColor.ToPlatform();
#elif ANDROID
				var minTrackColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Slider.ProgressColor);
				if (minTrackColor is not null && platformView.ProgressTintList is not null)
					platformView.ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(
						new global::Android.Graphics.Color((int)minTrackColor.ToPlatform()));
				var thumbColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Slider.ThumbColor);
				if (thumbColor is not null)
					platformView.ThumbTintList = global::Android.Content.Res.ColorStateList.ValueOf(
						new global::Android.Graphics.Color((int)thumbColor.ToPlatform()));
#endif
			});

			// Apply OnValueChanged callback to Slider via handler mapper
			SliderHandler.Mapper.AppendToMapping("CometSliderValueChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<double>>(EnvironmentKeys.Slider.ValueChanged);
				if (callback is null)
					return;
				var slider = handler.PlatformView;
				if (slider is null)
					return;
#if __IOS__ || MACCATALYST
				if (_sliderValueChangedHandlers.TryGetValue(slider, out var oldHandler))
				{
					slider.ValueChanged -= oldHandler;
					_sliderValueChangedHandlers.Remove(slider);
				}
				EventHandler newHandler = (s, e) =>
				{
					try { callback(slider.Value); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Slider ValueChanged callback failed: {ex.Message}"); }
				};
				_sliderValueChangedHandlers.AddOrUpdate(slider, newHandler);
				slider.ValueChanged += newHandler;
#elif ANDROID
				if (_sliderValueChangedHandlers.TryGetValue(slider, out var oldObj))
				{
					_sliderValueChangedHandlers.Remove(slider);
				}
				EventHandler<global::Android.Widget.SeekBar.ProgressChangedEventArgs> newAndroidHandler = (s, e) =>
				{
					try
					{
						if (e.FromUser && view is ISlider iSlider)
							callback(iSlider.Value);
					}
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Slider ValueChanged callback failed: {ex.Message}"); }
				};
				_sliderValueChangedHandlers.AddOrUpdate(slider, newAndroidHandler);
				slider.ProgressChanged += newAndroidHandler;
#endif
			});

			// Prevent native slider value snapping during active drag gesture
			SliderHandler.Mapper.ModifyMapping(nameof(IRange.Value), (handler, view, existingAction) =>
			{
#if __IOS__ || MACCATALYST
				if (handler.PlatformView is UIKit.UISlider uiSlider && uiSlider.Tracking)
					return;
#endif
				existingAction?.Invoke(handler, view);
			});

			// Apply OnToggled callback to Switch via handler mapper
			SwitchHandler.Mapper.AppendToMapping("CometSwitchToggled", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<bool>>(EnvironmentKeys.Switch.Toggled);
				if (callback is null)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				if (_switchValueChangedHandlers.TryGetValue(platformView, out var oldSwitchHandler))
				{
					platformView.ValueChanged -= oldSwitchHandler;
					_switchValueChangedHandlers.Remove(platformView);
				}
				EventHandler newSwitchHandler = (s, e) =>
				{
					try { callback(platformView.On); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Switch Toggled callback failed: {ex.Message}"); }
				};
				_switchValueChangedHandlers.AddOrUpdate(platformView, newSwitchHandler);
				platformView.ValueChanged += newSwitchHandler;
#elif ANDROID
				if (_switchValueChangedHandlers.TryGetValue(platformView, out var oldSwitchObj))
				{
					_switchValueChangedHandlers.Remove(platformView);
				}
				EventHandler<global::Android.Widget.CompoundButton.CheckedChangeEventArgs> newSwitchAndroidHandler = (s, e) =>
				{
					try { callback(e.IsChecked); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Switch Toggled callback failed: {ex.Message}"); }
				};
				_switchValueChangedHandlers.AddOrUpdate(platformView, newSwitchAndroidHandler);
				platformView.CheckedChange += newSwitchAndroidHandler;
#endif
			});

			// Apply OnCheckedChanged callback to CheckBox via handler mapper
			CheckBoxHandler.Mapper.AppendToMapping("CometCheckBoxCheckedChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<bool>>(EnvironmentKeys.CheckBox.IsCheckedChanged);
				if (callback is null)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				if (_checkBoxCheckedHandlers.TryGetValue(platformView, out var oldCheckHandler))
				{
					platformView.CheckedChanged -= oldCheckHandler;
					_checkBoxCheckedHandlers.Remove(platformView);
				}
				EventHandler newCheckHandler = (s, e) =>
				{
					try { callback(platformView.IsChecked); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] CheckBox CheckedChanged callback failed: {ex.Message}"); }
				};
				_checkBoxCheckedHandlers.AddOrUpdate(platformView, newCheckHandler);
				platformView.CheckedChanged += newCheckHandler;
#elif ANDROID
				if (_checkBoxCheckedHandlers.TryGetValue(platformView, out var oldCheckObj))
				{
					_checkBoxCheckedHandlers.Remove(platformView);
				}
				EventHandler<global::Android.Widget.CompoundButton.CheckedChangeEventArgs> newCheckAndroidHandler = (s, e) =>
				{
					try { callback(e.IsChecked); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] CheckBox CheckedChanged callback failed: {ex.Message}"); }
				};
				_checkBoxCheckedHandlers.AddOrUpdate(platformView, newCheckAndroidHandler);
				platformView.CheckedChange += newCheckAndroidHandler;
#endif
			});

			// Apply OnValueChanged callback to Stepper via handler mapper
			StepperHandler.Mapper.AppendToMapping("CometStepperValueChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<double>>(EnvironmentKeys.Stepper.ValueChanged);
				if (callback is null)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				platformView.ValueChanged += (s, e) =>
				{
					try { callback(platformView.Value); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Stepper ValueChanged callback failed: {ex.Message}"); }
				};
#elif ANDROID
				// Android MauiStepper is a custom LinearLayout without a direct ValueChanged event
				// TODO: Wire up button click events on the MauiStepper's internal +/- buttons
#endif
			});

			// Apply OnSelectedIndexChanged callback to Picker via handler mapper
			PickerHandler.Mapper.AppendToMapping("CometPickerSelectedIndexChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<int>>(EnvironmentKeys.Picker.SelectedIndexChanged);
				if (callback is null)
					return;
				var picker = handler.PlatformView;
				if (picker is null)
					return;
#if __IOS__ || MACCATALYST
				// Remove previous subscription to prevent duplicates on re-map
				if (_pickerEditingDidEndHandlers.TryGetValue(picker, out var oldHandler))
				{
					picker.EditingDidEnd -= oldHandler;
					_pickerEditingDidEndHandlers.Remove(picker);
				}
				EventHandler newHandler = (s, e) =>
				{
					try
					{
						if (view is IPicker iPicker)
							callback(iPicker.SelectedIndex);
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[Comet] Picker SelectedIndexChanged callback failed: {ex.Message}");
					}
				};
				_pickerEditingDidEndHandlers.AddOrUpdate(picker, newHandler);
				picker.EditingDidEnd += newHandler;
#elif ANDROID
				picker.AfterTextChanged += (s, e) =>
				{
					try
					{
						if (view is IPicker iPicker)
							callback(iPicker.SelectedIndex);
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[Comet] Picker SelectedIndexChanged callback failed: {ex.Message}");
					}
				};
#endif
			});

			// Apply Aspect to Image via handler mapper
			ImageHandler.Mapper.AppendToMapping("CometImageAspect", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var aspect = cometView.GetEnvironment<Microsoft.Maui.Aspect?>(EnvironmentKeys.Image.Aspect);
				if (aspect is null)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				platformView.ContentMode = aspect.Value switch
				{
					Microsoft.Maui.Aspect.AspectFit => UIKit.UIViewContentMode.ScaleAspectFit,
					Microsoft.Maui.Aspect.AspectFill => UIKit.UIViewContentMode.ScaleAspectFill,
					Microsoft.Maui.Aspect.Fill => UIKit.UIViewContentMode.ScaleToFill,
					_ => UIKit.UIViewContentMode.ScaleAspectFit
				};
#elif ANDROID
				platformView.SetScaleType(aspect.Value switch
				{
					Microsoft.Maui.Aspect.AspectFit => global::Android.Widget.ImageView.ScaleType.FitCenter,
					Microsoft.Maui.Aspect.AspectFill => global::Android.Widget.ImageView.ScaleType.CenterCrop,
					Microsoft.Maui.Aspect.Fill => global::Android.Widget.ImageView.ScaleType.FitXy,
					_ => global::Android.Widget.ImageView.ScaleType.FitCenter
				});
#endif
			});

			// Apply PlaceholderColor and Keyboard to TextEditor (Editor) via handler mapper
			EditorHandler.Mapper.AppendToMapping("CometEditorPlaceholderColor", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var color = cometView.GetEnvironment<Color>(EnvironmentKeys.Entry.PlaceholderColor);
				if (color is null)
					return;
				var editor = handler.PlatformView;
				if (editor is null)
					return;
#if __IOS__ || MACCATALYST
				// iOS UITextView doesn't have a built-in placeholder; MAUI handles it
				// through the EditorHandler. We can set the placeholder color via attributed string.
#elif ANDROID
				editor.SetHintTextColor(new global::Android.Content.Res.ColorStateList(
					new[] { Array.Empty<int>() },
					new[] { (int)color.ToPlatform() }));
#endif
			});

			EditorHandler.Mapper.AppendToMapping("CometEditorKeyboard", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var keyboard = cometView.GetEnvironment<Microsoft.Maui.Keyboard>(EnvironmentKeys.Entry.Keyboard);
				if (keyboard is null)
					return;
				var editor = handler.PlatformView;
				if (editor is null)
					return;
#if __IOS__ || MACCATALYST
				editor.ApplyKeyboard(keyboard);
#elif ANDROID
				if (view is IEditor editorView)
					EditorHandler.MapKeyboard(handler, editorView);
#endif
			});

			// Apply ProgressBar track/progress colors via handler mapper
			ProgressBarHandler.Mapper.AppendToMapping("CometProgressBarColors", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				var progressColor = cometView.GetEnvironment<Color>(EnvironmentKeys.ProgressBar.ProgressColor);
				if (progressColor is not null)
					platformView.ProgressTintColor = progressColor.ToPlatform();
				var trackColor = cometView.GetEnvironment<Color>(EnvironmentKeys.ProgressBar.TrackColor);
				if (trackColor is not null)
					platformView.TrackTintColor = trackColor.ToPlatform();
#elif ANDROID
				var progressColor = cometView.GetEnvironment<Color>(EnvironmentKeys.ProgressBar.ProgressColor);
				if (progressColor is not null)
					platformView.ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(
						new global::Android.Graphics.Color((int)progressColor.ToPlatform()));
				var trackColor = cometView.GetEnvironment<Color>(EnvironmentKeys.ProgressBar.TrackColor);
				if (trackColor is not null)
					platformView.ProgressBackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(
						new global::Android.Graphics.Color((int)trackColor.ToPlatform()));
#endif
			});

			// Apply DatePicker format via handler mapper
			DatePickerHandler.Mapper.AppendToMapping("CometDatePickerFormat", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var format = cometView.GetEnvironment<string>(EnvironmentKeys.DatePicker.Format);
				if (string.IsNullOrEmpty(format))
					return;
				if (view is IDatePicker datePicker)
				{
					// Format is handled by MAUI's IDatePicker.Format property
					// The environment value is read by the generated DatePicker class
				}
			});

			// Apply DatePicker text color via handler mapper
			DatePickerHandler.Mapper.AppendToMapping("CometDatePickerTextColor", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var color = cometView.GetEnvironment<Color>(EnvironmentKeys.DatePicker.TextColor);
				if (color is null)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				// UIDatePicker uses tintColor for text color on iOS 15+
				platformView.TintColor = color.ToPlatform();
#elif ANDROID
				platformView.SetTextColor(color.ToPlatform());
#endif
			});

			// Apply Button CornerRadius/BorderWidth/BorderColor via handler mapper
			ButtonHandler.Mapper.AppendToMapping("CometButtonStyling", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var platformView = handler.PlatformView;
				if (platformView is null)
					return;
#if __IOS__ || MACCATALYST
				var cornerRadius = cometView.GetEnvironment<int?>(EnvironmentKeys.Button.CornerRadius);
				if (cornerRadius is not null)
					platformView.Layer.CornerRadius = cornerRadius.Value;
				var borderWidth = cometView.GetEnvironment<double?>(EnvironmentKeys.Button.BorderWidth);
				if (borderWidth is not null)
					platformView.Layer.BorderWidth = (float)borderWidth.Value;
				var borderColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Button.BorderColor);
				if (borderColor is not null)
					platformView.Layer.BorderColor = borderColor.ToPlatform().CGColor;
				if (cornerRadius is not null || borderWidth is not null)
					platformView.ClipsToBounds = true;
#elif ANDROID
				var cornerRadius = cometView.GetEnvironment<int?>(EnvironmentKeys.Button.CornerRadius);
				var borderWidth = cometView.GetEnvironment<double?>(EnvironmentKeys.Button.BorderWidth);
				var borderColor = cometView.GetEnvironment<Color>(EnvironmentKeys.Button.BorderColor);
				if (cornerRadius is not null || borderWidth is not null || borderColor is not null)
				{
					var context = platformView.Context;
					if (context is not null)
					{
						var drawable = new global::Android.Graphics.Drawables.GradientDrawable();
						if (cornerRadius is not null)
							drawable.SetCornerRadius((float)(cornerRadius.Value * context.Resources.DisplayMetrics.Density));
						if (borderWidth is not null && borderColor is not null)
							drawable.SetStroke((int)(borderWidth.Value * context.Resources.DisplayMetrics.Density), borderColor.ToPlatform());
						else if (borderWidth is not null)
							drawable.SetStroke((int)(borderWidth.Value * context.Resources.DisplayMetrics.Density), global::Android.Graphics.Color.Transparent);
						var bg = cometView.GetBackground();
						if (bg is SolidPaint bgPaint && bgPaint.Color is not null)
							drawable.SetColor(bgPaint.Color.ToPlatform());
						platformView.Background = drawable;
					}
				}
#endif
			});

			// Apply OnTextChanged callback to Entry via handler mapper
			EntryHandler.Mapper.AppendToMapping("CometEntryTextChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<string>>(EnvironmentKeys.Entry.TextChanged);
				if (callback is null)
					return;
				var entry = handler.PlatformView;
				if (entry is null)
					return;
#if __IOS__ || MACCATALYST
				// Remove previous subscription to prevent duplicates on re-map
				if (_entryEditingChangedHandlers.TryGetValue(entry, out var oldHandler))
				{
					entry.EditingChanged -= oldHandler;
					_entryEditingChangedHandlers.Remove(entry);
				}
				EventHandler newHandler = (s, e) =>
				{
					// Suppress reactive view rebuilds during text input to prevent focus loss.
					// The native TextField already shows the typed text; a rebuild would
					// transfer the handler to a new view instance, causing resignFirstResponder.
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(entry.Text ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Entry TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
				_entryEditingChangedHandlers.AddOrUpdate(entry, newHandler);
				entry.EditingChanged += newHandler;
#elif ANDROID
				entry.AfterTextChanged += (s, e) =>
				{
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(entry.Text ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Entry TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
#endif
			});

			// Apply OnTextChanged callback to Editor via handler mapper
			EditorHandler.Mapper.AppendToMapping("CometEditorTextChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<string>>(EnvironmentKeys.Entry.TextChanged);
				if (callback is null)
					return;
				var editor = handler.PlatformView;
				if (editor is null)
					return;
#if __IOS__ || MACCATALYST
				// Remove previous subscription to prevent duplicates on re-map
				if (_editorChangedHandlers.TryGetValue(editor, out var oldHandler))
				{
					editor.Changed -= oldHandler;
					_editorChangedHandlers.Remove(editor);
				}
				EventHandler newHandler = (s, e) =>
				{
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(editor.Text ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Editor TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
				_editorChangedHandlers.AddOrUpdate(editor, newHandler);
				editor.Changed += newHandler;
#elif ANDROID
				editor.AfterTextChanged += (s, e) =>
				{
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(editor.Text ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] Editor TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
#endif
			});

			// Apply OnTextChanged callback to SearchBar via handler mapper
			SearchBarHandler.Mapper.AppendToMapping("CometSearchBarTextChanged", (handler, view) =>
			{
				if (view is not View cometView)
					return;
				var callback = cometView.GetEnvironment<Action<string>>(EnvironmentKeys.Entry.TextChanged);
				if (callback is null)
					return;
				var searchBar = handler.PlatformView;
				if (searchBar is null)
					return;
#if __IOS__ || MACCATALYST
				if (_searchBarTextChangedHandlers.TryGetValue(searchBar, out var oldHandler))
				{
					searchBar.TextChanged -= oldHandler;
					_searchBarTextChangedHandlers.Remove(searchBar);
				}
				EventHandler<UIKit.UISearchBarTextChangedEventArgs> newHandler = (s, e) =>
				{
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(e.SearchText ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] SearchBar TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
				_searchBarTextChangedHandlers.AddOrUpdate(searchBar, newHandler);
				searchBar.TextChanged += newHandler;
#elif ANDROID
				if (_searchBarTextChangedHandlers.TryGetValue(searchBar, out var oldObj))
				{
					_searchBarTextChangedHandlers.Remove(searchBar);
				}
				EventHandler<global::AndroidX.AppCompat.Widget.SearchView.QueryTextChangeEventArgs> newAndroidHandler = (s, e) =>
				{
					Reactive.ReactiveScheduler.SuppressNotifications = true;
					try { callback(e.NewText ?? string.Empty); }
					catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Comet] SearchBar TextChanged callback failed: {ex.Message}"); }
					finally { Reactive.ReactiveScheduler.SuppressNotifications = false; }
				};
				_searchBarTextChangedHandlers.AddOrUpdate(searchBar, newAndroidHandler);
				searchBar.QueryTextChange += newAndroidHandler;
#endif
			});

			// WebView Source mapper: load URL via platform delegate
			WebViewHandler.Mapper.AppendToMapping(nameof(IWebView.Source), (handler, view) =>
			{
				if (view is IWebView webView)
				{
					var src = webView.Source;
					if (src is CometUrlWebViewSource urlSrc && !string.IsNullOrEmpty(urlSrc.Url))
					{
						if (handler.PlatformView is IWebViewDelegate del)
							del.LoadUrl(urlSrc.Url);
					}
					else if (src is CometHtmlWebViewSource htmlSrc && !string.IsNullOrEmpty(htmlSrc.Html))
					{
						if (handler.PlatformView is IWebViewDelegate del2)
							del2.LoadHtml(htmlSrc.Html, null);
					}
				}
			});

			Lerp.Lerps[typeof(FrameConstraints)] = new Lerp
			{
				Calculate = (s, e, progress) => {
					var start = (FrameConstraints)s;
					var end = (FrameConstraints)(e);
					return start.Lerp(end, progress);
				}
			};
			builder.ConfigureMauiHandlers((handlersCollection) => handlersCollection.AddHandlers(new Dictionary<Type, Type>
			{
				{ typeof(AbstractLayout), typeof(LayoutHandler) },
				{ typeof(AbsoluteLayout), typeof(LayoutHandler) },
				{ typeof(FlexLayout), typeof(LayoutHandler) },
				{ typeof(Border), typeof(LayoutHandler) },
				{ typeof(RadioGroup), typeof(LayoutHandler) },
				{ typeof(ActivityIndicator), typeof(ActivityIndicatorHandler) },
			{ typeof(MauiViewHost), typeof(Handlers.MauiViewHostHandler) },
			{ typeof(NativeHost), typeof(Handlers.NativeHostHandler) },
				{ typeof(Button), typeof(ButtonHandler) },
				{ typeof(CheckBox), typeof(CheckBoxHandler) },
	#if __MACOS__
				{ typeof(CometWindow), typeof(Comet.Handlers.CometWindowHandler) },
#else
				{ typeof(CometWindow), typeof(WindowHandler) },
#endif
				{ typeof(DatePicker), typeof(DatePickerHandler) },
				{ typeof(FlyoutView), typeof(FlyoutViewHandler) },
				{ typeof(Frame), typeof(ContentViewHandler) },
				{ typeof(GraphicsView), typeof(GraphicsViewHandler) },
				{ typeof(Image) , typeof(ImageHandler) },
				{ typeof(ImageButton) , typeof(ImageButtonHandler) },
				{ typeof(IndicatorView), typeof(IndicatorViewHandler) },
				{ typeof(Picker), typeof(PickerHandler) },
				{ typeof(ProgressBar), typeof(ProgressBarHandler) },
				{ typeof(RadioButton), typeof(Handlers.RadioButtonHandler) },
				{ typeof(RefreshView), typeof(RefreshViewHandler) },
				{ typeof(SearchBar), typeof(SearchBarHandler) },
				{ typeof(SecureField), typeof(EntryHandler) },
				{ typeof(Slider), typeof(SliderHandler) },
				{ typeof(Stepper), typeof(StepperHandler) },
				{ typeof(Spacer), typeof(SpacerHandler) },
				{ typeof(SwipeView), typeof(SwipeViewHandler) },
				{ typeof(TabView), typeof(TabViewHandler) },
				{ typeof(TextEditor), typeof(EditorHandler) },
				{ typeof(TextField), typeof(EntryHandler) },
				{ typeof(Text), typeof(LabelHandler) },
				{ typeof(TimePicker), typeof(TimePickerHandler) },
				{ typeof(Toggle), typeof(SwitchHandler) },
				{ typeof(Toolbar), typeof(ToolbarHandler) },
				{ typeof(CometApp), typeof(ApplicationHandler) },
				{ typeof(ListView),typeof(ListViewHandler) },
				{ typeof(CollectionView),typeof(Handlers.CollectionViewHandler) },
				{ typeof(CarouselView),typeof(Handlers.CollectionViewHandler) },
				{ typeof(BoxView), typeof(Handlers.ShapeViewHandler) },
#if __MOBILE__
				{typeof(ScrollView), typeof(Handlers.ScrollViewHandler) },
				{typeof(ShapeView), typeof(Handlers.ShapeViewHandler)},
#elif __MACOS__
				{typeof(ScrollView), typeof(Handlers.ScrollViewHandler) },
				{typeof(ShapeView), typeof(Handlers.ShapeViewHandler)},
#else
				
				{typeof(ScrollView), typeof(Microsoft.Maui.Handlers.ScrollViewHandler) },
#endif


#if __IOS__
				{typeof(NavigationView), typeof (Handlers.NavigationViewHandler)},
				{typeof(View), typeof(CometViewHandler)},
#elif __MACOS__
				{typeof(NavigationView), typeof (Handlers.NavigationViewHandler)},
				{typeof(View), typeof(CometViewHandler)},
#elif __ANDROID__
				{typeof(NavigationView), typeof (Handlers.NavigationViewHandler)},
				{typeof(View), typeof(CometViewHandler)},
#else
				
				{typeof(NavigationView), typeof (Microsoft.Maui.Handlers.NavigationViewHandler)},
#endif
#if __MACOS__
				{typeof(WebView), typeof(Microsoft.Maui.Platform.MacOS.Handlers.WebViewHandler)},
#else
				{typeof(WebView), typeof(Microsoft.Maui.Handlers.WebViewHandler)},
#endif
				{typeof(MenuBar), typeof(Microsoft.Maui.Handlers.MenuBarHandler)},
				{typeof(MenuBarItem), typeof(Microsoft.Maui.Handlers.MenuBarItemHandler)},
				{typeof(MenuFlyoutItem), typeof(Microsoft.Maui.Handlers.MenuFlyoutItemHandler)},
				{typeof(MenuFlyoutSubItem), typeof(Microsoft.Maui.Handlers.MenuFlyoutSubItemHandler)},
				{typeof(MenuFlyoutSeparator), typeof(Microsoft.Maui.Handlers.MenuFlyoutSeparatorHandler)},
			}));

			// Register MAUI Controls handlers needed by Comet's CollectionViewHandler
			// and CarouselViewHandler, which internally create MAUI Controls views and
			// call ToPlatform() on them.
			builder.ConfigureMauiHandlers(h =>
			{
				h.TryAddHandler<Microsoft.Maui.Controls.CollectionView, Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.CarouselView, Microsoft.Maui.Controls.Handlers.Items.CarouselViewHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.ContentView, Microsoft.Maui.Handlers.ContentViewHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.Label, Microsoft.Maui.Handlers.LabelHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.VerticalStackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.HorizontalStackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.Grid, Microsoft.Maui.Handlers.LayoutHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.StackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				h.TryAddHandler<Microsoft.Maui.Controls.BoxView, Microsoft.Maui.Controls.Handlers.BoxViewHandler>();
			});

			// MAUI 10 moved MaxLines/LineBreakMode off ILabel to the concrete Label class.
			// Comet's Text (View, not Label) can't provide them, so UILabel defaults to
			// numberOfLines=1 and truncation. Fix by customizing the LabelHandler mapper.
			LabelHandler.Mapper.AppendToMapping("CometTextWordWrap", (handler, view) =>
			{
				if (view is Text)
				{
#if __IOS__ || MACCATALYST
					handler.PlatformView.Lines = 0;
					handler.PlatformView.LineBreakMode = UIKit.UILineBreakMode.WordWrap;
#elif WINDOWS
					handler.PlatformView.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
#elif __ANDROID__
					handler.PlatformView.SetMaxLines(int.MaxValue);
#endif
				}
			});

			// Register standard MAUI Controls handlers for MauiViewHost embedding.
			// These enable Microsoft.Maui.Controls types (Label, Entry, Border, etc.)
			// to be rendered when hosted inside a Comet view tree via MauiViewHost.
			builder.ConfigureMauiHandlers((handlersCollection) =>
			{
				handlersCollection.AddHandler<CometHost, Handlers.CometHostHandler>();
#if __MACOS__
				// On macOS, Platform.Maui.MacOS's SetupDefaults() already registered
				// macOS-specific handlers for MAUI Controls types. Use those instead
				// of MAUI's built-in handlers which throw NotImplementedException.
				handlersCollection.TryAddHandler<IContentView, Microsoft.Maui.Platform.MacOS.Handlers.ContentViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Label, Microsoft.Maui.Platform.MacOS.Handlers.LabelHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Entry, Microsoft.Maui.Platform.MacOS.Handlers.EntryHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Editor, Microsoft.Maui.Platform.MacOS.Handlers.EditorHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Button, Microsoft.Maui.Platform.MacOS.Handlers.ButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.CheckBox, Microsoft.Maui.Platform.MacOS.Handlers.CheckBoxHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Switch, Microsoft.Maui.Platform.MacOS.Handlers.SwitchHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Slider, Microsoft.Maui.Platform.MacOS.Handlers.SliderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Stepper, Microsoft.Maui.Platform.MacOS.Handlers.StepperHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Picker, Microsoft.Maui.Platform.MacOS.Handlers.PickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.DatePicker, Microsoft.Maui.Platform.MacOS.Handlers.DatePickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.TimePicker, Microsoft.Maui.Platform.MacOS.Handlers.TimePickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Image, Microsoft.Maui.Platform.MacOS.Handlers.ImageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ImageButton, Microsoft.Maui.Platform.MacOS.Handlers.ImageButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.SearchBar, Microsoft.Maui.Platform.MacOS.Handlers.SearchBarHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ProgressBar, Microsoft.Maui.Platform.MacOS.Handlers.ProgressBarHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ActivityIndicator, Microsoft.Maui.Platform.MacOS.Handlers.ActivityIndicatorHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.RadioButton, Microsoft.Maui.Platform.MacOS.Handlers.RadioButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Border, Microsoft.Maui.Platform.MacOS.Handlers.BorderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.BoxView, Microsoft.Maui.Platform.MacOS.Handlers.ShapeViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ContentView, Microsoft.Maui.Platform.MacOS.Handlers.ContentViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Layout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Frame, Microsoft.Maui.Platform.MacOS.Handlers.BorderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ScrollView, Microsoft.Maui.Platform.MacOS.Handlers.ScrollViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Grid, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.StackLayout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.HorizontalStackLayout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.VerticalStackLayout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.FlexLayout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.AbsoluteLayout, Microsoft.Maui.Platform.MacOS.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.RefreshView, Microsoft.Maui.Platform.MacOS.Handlers.RefreshViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.SwipeView, Microsoft.Maui.Platform.MacOS.Handlers.SwipeViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.IndicatorView, Microsoft.Maui.Platform.MacOS.Handlers.IndicatorViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.WebView, Microsoft.Maui.Platform.MacOS.Handlers.WebViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ContentPage, Microsoft.Maui.Platform.MacOS.Handlers.ContentPageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.NavigationPage, Microsoft.Maui.Platform.MacOS.Handlers.NavigationPageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.TabbedPage, Microsoft.Maui.Platform.MacOS.Handlers.TabbedPageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.FlyoutPage, Microsoft.Maui.Platform.MacOS.Handlers.FlyoutPageHandler>();
#else
				// Interface-based handler registrations (matches MAUI's own registrations)
				handlersCollection.TryAddHandler<IContentView, Microsoft.Maui.Handlers.ContentViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Label, Microsoft.Maui.Handlers.LabelHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Entry, Microsoft.Maui.Handlers.EntryHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Editor, Microsoft.Maui.Handlers.EditorHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Button, Microsoft.Maui.Handlers.ButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.CheckBox, Microsoft.Maui.Handlers.CheckBoxHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Switch, Microsoft.Maui.Handlers.SwitchHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Slider, Microsoft.Maui.Handlers.SliderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Stepper, Microsoft.Maui.Handlers.StepperHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Picker, Microsoft.Maui.Handlers.PickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.DatePicker, Microsoft.Maui.Handlers.DatePickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.TimePicker, Microsoft.Maui.Handlers.TimePickerHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Image, Microsoft.Maui.Handlers.ImageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ImageButton, Microsoft.Maui.Handlers.ImageButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.SearchBar, Microsoft.Maui.Handlers.SearchBarHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ProgressBar, Microsoft.Maui.Handlers.ProgressBarHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ActivityIndicator, Microsoft.Maui.Handlers.ActivityIndicatorHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.RadioButton, Microsoft.Maui.Handlers.RadioButtonHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Border, Microsoft.Maui.Handlers.BorderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.BoxView, Microsoft.Maui.Handlers.ShapeViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ContentView, Microsoft.Maui.Handlers.ContentViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Layout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Frame, Microsoft.Maui.Handlers.BorderHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ScrollView, Microsoft.Maui.Handlers.ScrollViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Grid, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.StackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.HorizontalStackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.VerticalStackLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.FlexLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.AbsoluteLayout, Microsoft.Maui.Handlers.LayoutHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.RefreshView, Microsoft.Maui.Handlers.RefreshViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.SwipeView, Microsoft.Maui.Handlers.SwipeViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.IndicatorView, Microsoft.Maui.Handlers.IndicatorViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.WebView, Microsoft.Maui.Handlers.WebViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.Page, Microsoft.Maui.Handlers.PageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.ContentPage, Microsoft.Maui.Handlers.PageHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.NavigationPage, Microsoft.Maui.Handlers.NavigationViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.TabbedPage, Microsoft.Maui.Handlers.TabbedViewHandler>();
				handlersCollection.TryAddHandler<Microsoft.Maui.Controls.FlyoutPage, Microsoft.Maui.Handlers.FlyoutViewHandler>();
#endif
			});


#if !__MACOS__
			// macOS (AppKit) uses NSApplication.InvokeOnMainThread set in ThreadHelper's
			// field initializer. Don't overwrite it with MAUI's MainThread which has no
			// macOS implementation.
			ThreadHelper.SetFireOnMainThread(MainThread.BeginInvokeOnMainThread);
#endif

			return builder;
		}

		static void ApplyInspectionMetadata(IViewHandler handler, View view)
		{
			if (handler?.PlatformView is null || view is null)
				return;

			var automationId = view.AutomationId;
			var isEnabled = view.IsEnabled;
			var isVisible = view.IsVisible;
			var inputTransparent = view.InputTransparent;
			var hasGestures = view is IGestureView gv && gv.Gestures?.Count > 0;

			// Use explicit AutomationId if set, otherwise fall back to View.Id
			// so every Comet view gets a stable platform identifier for
			// automation tools (MauiDevFlow, Appium, accessibility inspectors).
			var platformId = !string.IsNullOrWhiteSpace(automationId)
				? automationId
				: view.Id;

#if __IOS__ || MACCATALYST
			if (handler.PlatformView is UIKit.UIView platformView)
			{
				if (!string.IsNullOrWhiteSpace(platformId))
					platformView.AccessibilityIdentifier = platformId;

				if (!string.IsNullOrWhiteSpace(automationId) || hasGestures)
					platformView.IsAccessibilityElement = true;

				platformView.Hidden = !isVisible;
				platformView.UserInteractionEnabled = isEnabled && !inputTransparent;
				if (hasGestures)
					platformView.UserInteractionEnabled = true;
			}
#elif ANDROID
			if (handler.PlatformView is global::Android.Views.View platformView)
			{
				if (!string.IsNullOrWhiteSpace(platformId))
					platformView.ContentDescription = platformId;

				platformView.Enabled = isEnabled;
				platformView.Visibility = isVisible
					? global::Android.Views.ViewStates.Visible
					: global::Android.Views.ViewStates.Gone;
				// Only set Clickable on non-ViewGroup views or on ViewGroups that
				// have explicit Comet gestures. Layout containers (VStack, HStack, etc.)
				// should NOT be clickable — a clickable ViewGroup consumes touch events
				// even when the touch lands on a child Button, preventing the child's
				// click handler from firing.
				if (platformView is global::Android.Views.ViewGroup)
				{
					platformView.Clickable = hasGestures;
				}
				else
				{
					// For leaf views (Button, EditText, etc.), preserve MAUI's default
					// clickable state — don't override unless inputTransparent is set.
					if (inputTransparent)
						platformView.Clickable = false;
				}

				// Layout containers (ViewGroups) must not steal focus from focusable
				// children like EditText. Set DescendantFocusability so children get
				// focus first, and only mark the container focusable if it has gestures.
				if (platformView is global::Android.Views.ViewGroup viewGroup)
				{
					viewGroup.DescendantFocusability = global::Android.Views.DescendantFocusability.AfterDescendants;

					// Don't restrict focus on scrollable containers — they need proper
					// gesture handling for scroll interception to work.
					bool isScrollable = platformView is global::Android.Widget.ScrollView
						|| platformView is global::Android.Widget.HorizontalScrollView;

					if (!hasGestures && !isScrollable)
					{
						viewGroup.Focusable = false;
						viewGroup.FocusableInTouchMode = false;
					}
				}
			}
#endif
		}


		/// <summary>
		/// Registers handler mapper entries for IControlStyle resolution on Button,
		/// Toggle, TextField, and Slider. These resolve the active control style
		/// from the environment (scoped or theme-level) and apply it as per-instance
		/// defaults via the target view's cascading context. Explicit fluent
		/// properties (written to LocalContext with cascades:false) take priority
		/// over style-resolved values, preserving user intent without leaking one
		/// view's state (pressed/hovered/etc.) onto every control of the same type.
		/// </summary>
		static void RegisterStyleResolutionMappers()
		{
			// Button style resolution
			ButtonHandler.Mapper.AppendToMapping("CometButtonStyleResolution", (handler, view) =>
			{
				if (view is not Button button)
					return;

				var resolved = button.ResolveCurrentStyle(new ButtonConfiguration
				{
					TargetView = button,
					IsEnabled = button.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true,
					Label = ((IText)button).Text,
				});

				if (resolved is null || resolved == ViewModifier.Empty)
					return;

				ApplyModifierAsInstanceDefaults(button, resolved);
			});

			// Toggle style resolution
			SwitchHandler.Mapper.AppendToMapping("CometToggleStyleResolution", (handler, view) =>
			{
				if (view is not Toggle toggle)
					return;

				var resolved = toggle.ResolveCurrentStyle(new ToggleConfiguration
				{
					TargetView = toggle,
					IsOn = ((ISwitch)toggle).IsOn,
					IsEnabled = toggle.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true,
				});

				if (resolved is null || resolved == ViewModifier.Empty)
					return;

				ApplyModifierAsInstanceDefaults(toggle, resolved);
			});

			// TextField style resolution
			EntryHandler.Mapper.AppendToMapping("CometTextFieldStyleResolution", (handler, view) =>
			{
				if (view is not TextField textField)
					return;

				var resolved = textField.ResolveCurrentStyle(new TextFieldConfiguration
				{
					TargetView = textField,
					IsEnabled = textField.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true,
					Placeholder = ((IPlaceholder)textField).Placeholder,
				});

				if (resolved is null || resolved == ViewModifier.Empty)
					return;

				ApplyModifierAsInstanceDefaults(textField, resolved);
			});

			// Slider style resolution
			SliderHandler.Mapper.AppendToMapping("CometSliderStyleResolution", (handler, view) =>
			{
				if (view is not Slider slider)
					return;

				var resolved = slider.ResolveCurrentStyle(new SliderConfiguration
				{
					TargetView = slider,
					Value = ((ISlider)slider).Value,
					Minimum = ((IRange)slider).Minimum,
					Maximum = ((IRange)slider).Maximum,
					IsEnabled = slider.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true,
				});

				if (resolved is null || resolved == ViewModifier.Empty)
					return;

				ApplyModifierAsInstanceDefaults(slider, resolved);
			});
		}

		/// <summary>
		/// Applies a resolved <see cref="ViewModifier"/>'s property writes as
		/// per-instance defaults on <paramref name="view"/>. State-dependent values
		/// (IsPressed / IsHovered / IsFocused colors) must stay scoped to the
		/// instance that owns the state — writing them to a global type-scoped
		/// environment would leak one view's pressed appearance onto every other
		/// view of the same type.
		///
		/// Values are written to the view's cascading context (cascades: true),
		/// which sits below the view's LocalContext in the lookup chain. Explicit
		/// fluent setters like <c>.Background(Colors.Red)</c> use cascades:false
		/// and therefore win over style-resolved defaults. See STYLE_THEME_SPEC.md
		/// §4.8, §10.3.
		/// </summary>
		static void ApplyModifierAsInstanceDefaults(View view, ViewModifier modifier)
		{
			// Snapshot what the modifier would write (without mutating the view)
			// so we can replay each write to the view's cascading context at the
			// right priority.
			Dictionary<(ContextualObject view, string property, bool cascades), (object oldValue, object newValue)> changes;
			ContextualObject.MonitorChanges();
			try
			{
				modifier.Apply(view);
			}
			finally
			{
				changes = ContextualObject.StopMonitoringChanges();
			}

			if (changes is null)
				return;

			foreach (var entry in changes)
			{
				// Only replay writes that targeted this specific view — the
				// modifier may set properties on child/composed views it
				// created internally, and those should reach their own
				// contexts untouched.
				if (!ReferenceEquals(entry.Key.view, view))
					continue;

				var key = entry.Key.property;
				var newValue = entry.Value.newValue;
				if (newValue is null)
					continue;

				// cascades: true routes to the view's Context slot, which is
				// below LocalContext (explicit fluent) but above the parent /
				// global environment. Exactly the precedence a style default
				// should have, and scoped to this instance only.
				view.SetEnvironment(key, newValue, cascades: true);
			}
		}
	}
}
