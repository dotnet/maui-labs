using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls.Handlers;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.Controls.Platform.WPF;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Controls.Hosting.WPF
{
	public static partial class AppHostBuilderExtensions
	{
		public static MauiAppBuilder UseMauiAppWPF<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(this MauiAppBuilder builder)
			where TApp : class, IApplication
		{
			builder.UseMauiApp<TApp>();
			builder.SetupDefaults();
			return builder;
		}

		public static MauiAppBuilder UseMauiAppWPF<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(this MauiAppBuilder builder, Func<IServiceProvider, TApp> implementationFactory)
			where TApp : class, IApplication
		{
			builder.Services.TryAddSingleton<IApplication>(implementationFactory);
			builder.SetupDefaults();
			return builder;
		}

		static IMauiHandlersCollection AddMauiControlsHandlers(this IMauiHandlersCollection handlersCollection)
		{
			handlersCollection.AddHandler<Application, ApplicationHandler>();
			handlersCollection.AddHandler<Window, WindowHandler>();
			handlersCollection.AddHandler<Label, LabelHandler>();
			handlersCollection.AddHandler<ContentPage, PageHandler>();
			handlersCollection.AddHandler<Layout, LayoutHandler>();
			handlersCollection.AddHandler<Button, ButtonHandler>();
			handlersCollection.AddHandler<Entry, EntryHandler>();
			handlersCollection.AddHandler<Editor, EditorHandler>();
			handlersCollection.AddHandler<Image, ImageHandler>();
			handlersCollection.AddHandler<ScrollView, ScrollViewHandler>();
			handlersCollection.AddHandler<ActivityIndicator, ActivityIndicatorHandler>();
			handlersCollection.AddHandler<ProgressBar, ProgressBarHandler>();
			handlersCollection.AddHandler<Slider, SliderHandler>();
			handlersCollection.AddHandler<Switch, SwitchHandler>();
			handlersCollection.AddHandler<Picker, PickerHandler>();
			handlersCollection.AddHandler<DatePicker, DatePickerHandler>();
			handlersCollection.AddHandler<Stepper, StepperHandler>();
			handlersCollection.AddHandler<CheckBox, CheckBoxHandler>();
			handlersCollection.AddHandler<SearchBar, SearchBarHandler>();
			handlersCollection.AddHandler<TimePicker, TimePickerHandler>();
			handlersCollection.AddHandler<Border, BorderHandler>();
			handlersCollection.AddHandler<Frame, BorderHandler>();
			handlersCollection.AddHandler<ContentView, ContentViewHandler>();
			handlersCollection.AddHandler<BoxView, Microsoft.Maui.Handlers.WPF.BoxViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Rectangle, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.RoundRectangle, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Ellipse, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Line, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Path, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polygon, ShapeViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polyline, ShapeViewHandler>();
			handlersCollection.AddHandler<AspNetCore.Components.WebView.Maui.BlazorWebView, AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler>();

			// Navigation handlers
			handlersCollection.AddHandler<NavigationPage, NavigationViewHandler>();
			handlersCollection.AddHandler<TabbedPage, TabbedViewHandler>();
			handlersCollection.AddHandler<FlyoutPage, FlyoutViewHandler>();
			handlersCollection.AddHandler<Shell, ShellHandler>();

			// Collection handlers
			handlersCollection.AddHandler<CollectionView, Microsoft.Maui.Handlers.WPF.CollectionViewHandler>();
			handlersCollection.AddHandler<ListView, ListViewHandler>();
			handlersCollection.AddHandler<CarouselView, Microsoft.Maui.Handlers.WPF.CarouselViewHandler>();
			handlersCollection.AddHandler<IndicatorView, IndicatorViewHandler>();
			handlersCollection.AddHandler<TableView, TableViewHandler>();
			handlersCollection.AddHandler<SwipeView, SwipeViewHandler>();
			handlersCollection.AddHandler<RefreshView, RefreshViewHandler>();

			// Additional control handlers
			handlersCollection.AddHandler<RadioButton, RadioButtonHandler>();
			handlersCollection.AddHandler<ImageButton, ImageButtonHandler>();

			handlersCollection.AddHandler<Microsoft.Maui.Controls.WebView, WebViewHandler>();
			handlersCollection.AddHandler<Microsoft.Maui.Controls.HybridWebView, HybridWebViewHandler>();
			handlersCollection.AddHandler<GraphicsView, GraphicsViewHandler>();

			return handlersCollection;
		}

		static MauiAppBuilder SetupDefaults(this MauiAppBuilder builder)
		{


#pragma warning disable CS0612, CA1416 // Type or member is obsolete, 'ResourcesProvider' is unsupported on: 'iOS' 14.0 and later
			DependencyService.Register<ResourcesProvider>();


			//DependencyService.Register<PlatformSizeService>();
			//DependencyService.Register<FontNamedSizeService>();


			builder.Services.AddSingleton<IDispatcherProvider>(svc =>
				// the DispatcherProvider might have already been initialized, so ensure that we are grabbing the
				// Current and putting it in the DI container.
				new WPFDispatcherProvider());

			builder.Services.AddScoped(svc =>
			{
				var provider = svc.GetRequiredService<IDispatcherProvider>();
				if (DispatcherProvider.SetCurrent(provider))
					svc.GetService<ILogger<Dispatcher>>()?.LogWarning("Replaced an existing DispatcherProvider with one from the service provider.");

				return Dispatcher.GetForCurrentThread()!;
			});

			// Register WPF-specific animation ticker (runs on dispatcher thread)
			builder.Services.AddSingleton<Microsoft.Maui.Animations.Ticker>(new Microsoft.Maui.Platforms.Windows.WPF.WPFTicker());

			// Register alert/prompt/action sheet handler
			Microsoft.Maui.Platforms.Windows.WPF.WPFAlertManagerSubscription.Register(builder.Services);

			// Initialize theme detection
			Microsoft.Maui.Platforms.Windows.WPF.ThemeManager.Initialize();

			// Register font services
			builder.Services.AddSingleton<IFontRegistrar, Microsoft.Maui.Platforms.Windows.WPF.WPFFontRegistrar>();
			builder.Services.AddSingleton<IEmbeddedFontLoader, Microsoft.Maui.Platforms.Windows.WPF.WPFEmbeddedFontLoader>();
			builder.Services.AddSingleton<IFontManager, Microsoft.Maui.Platforms.Windows.WPF.WPFFontManager>();

			// Register FontNamedSizeService
			DependencyService.Register<Microsoft.Maui.Platforms.Windows.WPF.WPFFontNamedSizeService>();

			builder.ConfigureImageSourceHandlers();
			builder
				.ConfigureMauiHandlers(handlers =>
				{
					handlers.AddMauiControlsHandlers();
				});

			// Register EffectsFactory (internal type required for page effects)
			var effectsFactoryType = typeof(Microsoft.Maui.Controls.Effect).Assembly
				.GetType("Microsoft.Maui.Controls.Hosting.EffectsFactory");
			if (effectsFactoryType != null)
				builder.Services.TryAddSingleton(effectsFactoryType);

			builder.RemapForControls();

			return builder;
		}

		class MauiControlsInitializer : IMauiInitializeService
		{
			public void Initialize(IServiceProvider services)
			{
			}
		}


		internal static MauiAppBuilder ConfigureImageSourceHandlers(this MauiAppBuilder builder)
		{
			//builder.ConfigureImageSources(services =>
			//{
			//	services.AddService<FileImageSource>(svcs => new FileImageSourceService(svcs.CreateLogger<FileImageSourceService>()));
			//	services.AddService<FontImageSource>(svcs => new FontImageSourceService(svcs.GetRequiredService<IFontManager>(), svcs.CreateLogger<FontImageSourceService>()));
			//	services.AddService<StreamImageSource>(svcs => new StreamImageSourceService(svcs.CreateLogger<StreamImageSourceService>()));
			//	services.AddService<UriImageSource>(svcs => new UriImageSourceService(svcs.CreateLogger<UriImageSourceService>()));
			//});

			return builder;
		}

		/// <summary>
		/// Execute an action on the WPF UI thread. If already on the UI thread, runs inline.
		/// Otherwise, marshals to the dispatcher. This prevents cross-thread exceptions
		/// when MAUI property mappers fire from background threads (animations, async bindings).
		/// </summary>
		static void RunOnUI(IViewHandler handler, Action<System.Windows.FrameworkElement> action)
		{
			if (handler.PlatformView is not System.Windows.FrameworkElement fe) return;
			if (fe.Dispatcher.CheckAccess())
				action(fe);
			else
				fe.Dispatcher.BeginInvoke(action, fe);
		}

		static void RunOnUIElement(IViewHandler handler, Action<System.Windows.UIElement> action)
		{
			if (handler.PlatformView is not System.Windows.UIElement ue) return;
			if (ue.Dispatcher.CheckAccess())
				action(ue);
			else
				ue.Dispatcher.BeginInvoke(action, ue);
		}

		internal static MauiAppBuilder RemapForControls(this MauiAppBuilder builder)
		{
			// Wire up InvalidateMeasure so MAUI property changes propagate to WPF layout
			Microsoft.Maui.Handlers.ViewHandler.ViewCommandMapper.ModifyMapping(nameof(IView.InvalidateMeasure), (handler, view, args, _) =>
			{
				RunOnUIElement(handler, ue => ue.InvalidateMeasure());
			});

			// Override base ViewMapper entries that target WinUI types
			// so they work with WPF FrameworkElement instead
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Width), (handler, view, _) =>
			{
				double w = view.Width;
				RunOnUI(handler, fe => { if (w >= 0) fe.Width = w; });
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Height), (handler, view, _) =>
			{
				double h = view.Height;
				RunOnUI(handler, fe => { if (h >= 0) fe.Height = h; });
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.MinimumWidth), (handler, view, _) =>
			{
				double v = view.MinimumWidth;
				if (!double.IsNaN(v) && v >= 0)
					RunOnUI(handler, fe => fe.MinWidth = v);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.MinimumHeight), (handler, view, _) =>
			{
				double v = view.MinimumHeight;
				if (!double.IsNaN(v) && v >= 0)
					RunOnUI(handler, fe => fe.MinHeight = v);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.MaximumWidth), (handler, view, _) =>
			{
				double v = view.MaximumWidth;
				if (!double.IsNaN(v) && !double.IsInfinity(v))
					RunOnUI(handler, fe => fe.MaxWidth = v);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.MaximumHeight), (handler, view, _) =>
			{
				double v = view.MaximumHeight;
				if (!double.IsNaN(v) && !double.IsInfinity(v))
					RunOnUI(handler, fe => fe.MaxHeight = v);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Opacity), (handler, view, _) =>
			{
				double o = view.Opacity;
				RunOnUIElement(handler, ue => ue.Opacity = o);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Visibility), (handler, view, _) =>
			{
				var vis = view.Visibility switch
				{
					Visibility.Collapsed => System.Windows.Visibility.Collapsed,
					Visibility.Hidden => System.Windows.Visibility.Hidden,
					_ => System.Windows.Visibility.Visible,
				};
				RunOnUIElement(handler, ue => ue.Visibility = vis);
			});

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.IsEnabled), (handler, view, _) =>
			{
				bool enabled = view.IsEnabled;
				RunOnUIElement(handler, ue => ue.IsEnabled = enabled);
			});

			// Shared background update logic for both Background and BackgroundColor properties.
			// IView.Background includes BackgroundColor as fallback, but the mapper only fires
			// when the matching property name changes. Styles often set BackgroundColor, so both must be mapped.
			static void ApplyBackground(IViewHandler handler, IView view)
			{
				var brush = ConvertPaintToBrush(view.Background);
				if (brush == null) return;
				if (handler.PlatformView is not System.Windows.UIElement element) return;

				if (!element.Dispatcher.CheckAccess())
					brush.Freeze();

				void Apply()
				{
					if (element is System.Windows.Controls.Control c)
						c.Background = brush;
					else if (element is System.Windows.Controls.Panel p)
						p.Background = brush;
				}

				if (element.Dispatcher.CheckAccess())
					Apply();
				else
					element.Dispatcher.BeginInvoke((Action)Apply);
			}

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Background), (handler, view, _) =>
				ApplyBackground(handler, view));
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping("BackgroundColor", (handler, view, _) =>
				ApplyBackground(handler, view));

			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Margin), (handler, view, _) =>
			{
				var m = view.Margin;
				var thickness = new System.Windows.Thickness(m.Left, m.Top, m.Right, m.Bottom);
				RunOnUI(handler, fe => fe.Margin = thickness);
			});

			// Transforms — TranslationX/Y, Rotation, Scale, Anchor
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.TranslationX), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.TranslationY), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Rotation), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.RotationX), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.RotationY), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Scale), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.ScaleX), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.ScaleY), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.AnchorX), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.AnchorY), (handler, view, _) =>
			{
				RunOnUI(handler, fe => UpdateTransformGroup(fe, view));
			});

			// Shadow → DropShadowEffect
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Shadow), (handler, view, _) =>
			{
				// Check Frame.HasShadow (legacy property, used as fallback when Shadow is null)
				bool hasFrameShadow = false;
				if (view is Microsoft.Maui.Controls.Frame frame)
					hasFrameShadow = frame.HasShadow;

				var shadow = view.Shadow;
				System.Windows.Media.Effects.DropShadowEffect? effect = null;

				if (shadow != null && shadow.Paint is SolidPaint sp && sp.Color != null)
				{
					var c = sp.Color;
					effect = new System.Windows.Media.Effects.DropShadowEffect
					{
						Color = System.Windows.Media.Color.FromArgb(
							(byte)(c.Alpha * 255), (byte)(c.Red * 255),
							(byte)(c.Green * 255), (byte)(c.Blue * 255)),
						BlurRadius = Math.Abs(shadow.Radius),
						ShadowDepth = Math.Sqrt(shadow.Offset.X * shadow.Offset.X + shadow.Offset.Y * shadow.Offset.Y),
						Direction = Math.Atan2(-shadow.Offset.Y, shadow.Offset.X) * 180.0 / Math.PI,
						Opacity = shadow.Opacity
					};
				}
				else if (hasFrameShadow)
				{
					effect = new System.Windows.Media.Effects.DropShadowEffect
					{
						Color = System.Windows.Media.Colors.Gray,
						Direction = 320,
						Opacity = 0.5,
						BlurRadius = 6,
						ShadowDepth = 2
					};
				}

				RunOnUIElement(handler, ue => ue.Effect = effect);
			});

			// Clip → UIElement.Clip
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Clip), (handler, view, _) =>
			{
				var clipShape = view.Clip;
				System.Windows.Media.Geometry? clipGeometry = null;
				if (clipShape != null)
				{
					try
					{
						var w = view.Frame.Width > 0 ? view.Frame.Width : 100;
						var h = view.Frame.Height > 0 ? view.Frame.Height : 100;
						var path = clipShape.PathForBounds(new Graphics.Rect(0, 0, w, h));
						if (path != null)
						{
							var pathStr = path.ToDefinitionString();
							if (!string.IsNullOrEmpty(pathStr))
								clipGeometry = System.Windows.Media.Geometry.Parse(pathStr);
						}
					}
					catch { }
				}
				RunOnUIElement(handler, ue => ue.Clip = clipGeometry);
			});

			// InputTransparent → IsHitTestVisible
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.InputTransparent), (handler, view, _) =>
			{
				bool transparent = view.InputTransparent;
				RunOnUIElement(handler, ue => ue.IsHitTestVisible = !transparent);
			});

			// FlowDirection
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.FlowDirection), (handler, view, _) =>
			{
				var dir = view.FlowDirection == Microsoft.Maui.FlowDirection.RightToLeft
					? System.Windows.FlowDirection.RightToLeft
					: System.Windows.FlowDirection.LeftToRight;
				RunOnUI(handler, fe => fe.FlowDirection = dir);
			});

			// AutomationId
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.AutomationId), (handler, view, _) =>
			{
				if (!string.IsNullOrEmpty(view.AutomationId))
					RunOnUIElement(handler, ue => System.Windows.Automation.AutomationProperties.SetAutomationId(ue, view.AutomationId));
			});

			// Semantics → Accessibility
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.Semantics), (handler, view, _) =>
			{
				if (view.Semantics == null) return;
				var desc = view.Semantics.Description;
				var hint = view.Semantics.Hint;
				RunOnUIElement(handler, ue =>
				{
					if (!string.IsNullOrEmpty(desc))
						System.Windows.Automation.AutomationProperties.SetName(ue, desc);
					if (!string.IsNullOrEmpty(hint))
						System.Windows.Automation.AutomationProperties.SetHelpText(ue, hint);
				});
			});

			// ToolTip
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping("ToolTip", (handler, view, _) =>
			{
				object? tip = null;
				try { if (view is BindableObject bo) tip = ToolTipProperties.GetText(bo); } catch { }
				string? tipStr = tip is string s && !string.IsNullOrEmpty(s) ? s : null;
				RunOnUI(handler, fe => fe.ToolTip = tipStr);
			});

			// ZIndex → Panel.ZIndex
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping(nameof(IView.ZIndex), (handler, view, _) =>
			{
				int z = view.ZIndex;
				RunOnUIElement(handler, ue => System.Windows.Controls.Panel.SetZIndex(ue, z));
			});

			// ContextFlyout → ContextMenu
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping("ContextFlyout", (handler, view, _) =>
			{
				if (handler.PlatformView is System.Windows.FrameworkElement fe && view is BindableObject bo)
				{
					try
					{
						var flyout = FlyoutBase.GetContextFlyout(bo);
						if (flyout is MenuFlyout menuFlyout)
						{
							var contextMenu = new System.Windows.Controls.ContextMenu();
							foreach (var item in menuFlyout)
							{
								if (item is MenuFlyoutItem mfi)
								{
									var mi = new System.Windows.Controls.MenuItem { Header = mfi.Text };
									mi.Click += (s, e) =>
									{
										mfi.Command?.Execute(mfi.CommandParameter);
									};
									contextMenu.Items.Add(mi);
								}
								else if (item is MenuFlyoutSeparator)
								{
									contextMenu.Items.Add(new System.Windows.Controls.Separator());
								}
							}
							fe.ContextMenu = contextMenu;
						}
					}
					catch { }
				}
			});

			// Wire gesture recognizers to WPF mouse events
			Microsoft.Maui.Handlers.ViewHandler.ViewMapper.ModifyMapping("GestureRecognizers", (handler, view, _) =>
			{
				if (handler.PlatformView is System.Windows.FrameworkElement fe)
				{
					Microsoft.Maui.Platforms.Windows.WPF.GestureManager.SetupGestures(fe, view);
					// Hook VisualStateManager for hover/pressed/focused states
					Microsoft.Maui.Platforms.Windows.WPF.VisualStateManagerHooks.AttachVisualStateHooks(fe, view);
				}
			});

			return builder;
		}

		static void UpdateTransformGroup(System.Windows.FrameworkElement fe, IView view)
		{
			var group = fe.RenderTransform as System.Windows.Media.TransformGroup;
			if (group == null)
			{
				group = new System.Windows.Media.TransformGroup();
				group.Children.Add(new System.Windows.Media.ScaleTransform());
				group.Children.Add(new System.Windows.Media.RotateTransform());
				group.Children.Add(new System.Windows.Media.TranslateTransform());
				fe.RenderTransform = group;
			}

			if (group.Children[0] is System.Windows.Media.ScaleTransform scale)
			{
				scale.ScaleX = view.Scale * view.ScaleX;
				scale.ScaleY = view.Scale * view.ScaleY;
			}
			if (group.Children[1] is System.Windows.Media.RotateTransform rotate)
			{
				rotate.Angle = view.Rotation;
			}
			if (group.Children[2] is System.Windows.Media.TranslateTransform translate)
			{
				translate.X = view.TranslationX;
				translate.Y = view.TranslationY;
			}

			fe.RenderTransformOrigin = new System.Windows.Point(view.AnchorX, view.AnchorY);
		}

		static System.Windows.Media.Color ToWpfColor(Microsoft.Maui.Graphics.Color c) =>
			System.Windows.Media.Color.FromArgb(
				(byte)(c.Alpha * 255), (byte)(c.Red * 255),
				(byte)(c.Green * 255), (byte)(c.Blue * 255));

		static System.Windows.Media.Brush? ConvertPaintToBrush(Paint? paint)
		{
			if (paint == null) return null;

			if (paint is SolidPaint sp && sp.Color != null)
				return new System.Windows.Media.SolidColorBrush(ToWpfColor(sp.Color));

			if (paint is LinearGradientPaint lgp && lgp.GradientStops != null)
			{
				var wpfBrush = new System.Windows.Media.LinearGradientBrush();
				wpfBrush.StartPoint = new System.Windows.Point(lgp.StartPoint.X, lgp.StartPoint.Y);
				wpfBrush.EndPoint = new System.Windows.Point(lgp.EndPoint.X, lgp.EndPoint.Y);
				foreach (var stop in lgp.GradientStops)
				{
					if (stop.Color != null)
						wpfBrush.GradientStops.Add(new System.Windows.Media.GradientStop(ToWpfColor(stop.Color), stop.Offset));
				}
				return wpfBrush;
			}

			if (paint is RadialGradientPaint rgp && rgp.GradientStops != null)
			{
				var wpfBrush = new System.Windows.Media.RadialGradientBrush();
				wpfBrush.Center = new System.Windows.Point(rgp.Center.X, rgp.Center.Y);
				wpfBrush.RadiusX = rgp.Radius;
				wpfBrush.RadiusY = rgp.Radius;
				foreach (var stop in rgp.GradientStops)
				{
					if (stop.Color != null)
						wpfBrush.GradientStops.Add(new System.Windows.Media.GradientStop(ToWpfColor(stop.Color), stop.Offset));
				}
				return wpfBrush;
			}

			return null;
		}

	}
}
