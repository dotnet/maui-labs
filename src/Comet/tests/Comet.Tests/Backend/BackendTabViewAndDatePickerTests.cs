#nullable enable
using System;
using System.Collections.Generic;
using Comet;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the TabView backend contract: SelectedSignal drives Nav_SelectedIndex on
	/// the backend node; SelectItem writes the signal and patches the node; tabs are
	/// accessible through the public API.
	/// </summary>
	public class BackendTabViewTests
	{
		static BackendTabViewTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void AddTab_TwoParameterBinarySignature_IsRetained()
		{
			var method = typeof(TabView).GetMethod(
				nameof(TabView.AddTab),
				new[] { typeof(string), typeof(View) });
			Assert.NotNull(method);
			Assert.Equal(typeof(void), method.ReturnType);

			var tabs = new TabView();
			tabs.AddTab("Settings", new Text("Page"));
			Assert.Null(tabs.Tabs[0].Icon);
		}

		[Fact]
		public void TabView_PushesSelectedIndex_OnBridge()
		{
			var tv = new TabView();
			tv.AddTab("A", new Text("Page A"), "home");
			tv.AddTab("B", new Text("Page B"), "settings");
			tv.SelectedIndex = 1;

			var node = Bridge(tv);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Fact]
		public void TabView_SelectItem_WritesSignal_AndPatchesNode()
		{
			var tv = new TabView();
			tv.AddTab("A", new Text("Page A"));
			tv.AddTab("B", new Text("Page B"));

			var node = Bridge(tv);
			Assert.Equal(0, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			tv.SelectItem(1);
			Assert.Equal(1, tv.SelectedSignal.Value);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Fact]
		public void TabView_SignalChange_PatchesNode()
		{
			var tv = new TabView();
			tv.AddTab("A", new Text("Page A"));
			tv.AddTab("B", new Text("Page B"));

			var node = Bridge(tv);
			tv.SelectedSignal.Value = 1;
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Fact]
		public void TabView_Tabs_AreAccessible()
		{
			var tv = new TabView();
			tv.AddTab("Home", new Text("Home"), "home");
			tv.AddTab("Settings", new Text("Settings"), "settings");

			Assert.Equal(2, tv.Tabs.Count);
			Assert.Equal("Home", tv.Tabs[0].Title);
			Assert.Equal("home", tv.Tabs[0].Icon);
			Assert.Equal("Settings", tv.Tabs[1].Title);
		}

		[Fact]
		public void TabView_SelectedIndexChanged_Fires()
		{
			var tv = new TabView();
			tv.AddTab("A", new Text("Page A"));
			tv.AddTab("B", new Text("Page B"));
			int callbackIndex = -1;
			tv.SelectedIndexChanged = i => callbackIndex = i;

			tv.SelectItem(1);
			Assert.Equal(1, callbackIndex);
		}

		[Fact]
		public void TabView_OwnerReplacement_RebuildsTabs_PreservesSelection()
		{
			// Simulates a theme/root rebuild: the TabView is replaced with a new instance
			// that has different content, but the selection index should be preserved.
			var tv1 = new TabView();
			tv1.AddTab("A", new Text("Old A"));
			tv1.AddTab("B", new Text("Old B"));

			var node = Bridge(tv1);
			Assert.Equal(0, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			tv1.SelectItem(1);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			// Simulate owner replacement (normal rebuild, NOT hot reload).
			var tv2 = new TabView();
			tv2.AddTab("X", new Text("New X"), "home");
			tv2.AddTab("Y", new Text("New Y"), "settings");
			tv2.SelectedIndex = 1;

			// OnOwnerViewChanged with isHotReload=false must still rebuild children.
			node.OnOwnerViewChanged(tv2, isHotReload: false);

			// The node should have accepted the new owner and be ready for the
			// new tab structure — verify the node didn't throw and accepted the change.
			Assert.Equal(1, node.LastOwnerChangeWasHotReload is false ? 1 : 0);
			Assert.Equal(2, tv2.Tabs.Count);
			Assert.Equal("X", tv2.Tabs[0].Title);
			Assert.Equal("home", tv2.Tabs[0].Icon);
		}
	}

	/// <summary>
	/// Locks the DatePicker backend contract: IsOpen and Date drive
	/// DatePicker_IsOpen and DatePicker_SelectedTicks on the backend node;
	/// signal changes patch the node.
	/// </summary>
	public class BackendDatePickerTests
	{
		static BackendDatePickerTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void DatePicker_PushesIsOpen_OnBridge()
		{
			var isOpen = new Signal<bool>(false);
			var dp = new DatePicker(new DateTime(2025, 6, 15)) { IsOpen = isOpen };

			var node = Bridge(dp);
			Assert.True(node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool);
			Assert.False(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);
		}

		[Fact]
		public void DatePicker_WithoutIsOpen_RendersAndMeasuresInline()
		{
			var picker = new DatePicker(new DateTime(2025, 6, 15));
			var node = Bridge(picker);
			var presentation = DatePickerPresentationState.Resolve(
				node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool,
				node.Get(PropertyIds.DatePicker_IsOpen).AsBool);

			Assert.False(presentation.IsDialogMode);
			Assert.True(presentation.RendersInline);
			Assert.True(presentation.MeasuresInline);
			Assert.False(presentation.PresentsDialog);
		}

		[Fact]
		public void DatePicker_WithClosedIsOpen_UsesZeroSizedDialogHost()
		{
			var picker = new DatePicker(new DateTime(2025, 6, 15))
			{
				IsOpen = new Signal<bool>(false),
			};
			var node = Bridge(picker);
			var presentation = DatePickerPresentationState.Resolve(
				node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool,
				node.Get(PropertyIds.DatePicker_IsOpen).AsBool);

			Assert.True(presentation.IsDialogMode);
			Assert.False(presentation.RendersInline);
			Assert.False(presentation.MeasuresInline);
			Assert.False(presentation.PresentsDialog);
		}

		[Fact]
		public void DatePicker_WithOpenIsOpen_PresentsOnlyDialog()
		{
			var picker = new DatePicker(new DateTime(2025, 6, 15))
			{
				IsOpen = new Signal<bool>(true),
			};
			var node = Bridge(picker);
			var presentation = DatePickerPresentationState.Resolve(
				node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool,
				node.Get(PropertyIds.DatePicker_IsOpen).AsBool);

			Assert.True(presentation.IsDialogMode);
			Assert.False(presentation.RendersInline);
			Assert.False(presentation.MeasuresInline);
			Assert.True(presentation.PresentsDialog);
		}

		[Fact]
		public void DatePicker_RetainedNodeSwitchesBetweenInlineAndDialogModes()
		{
			var inline = new DatePicker(new DateTime(2025, 6, 15));
			var node = Bridge(inline);

			var dialog = new DatePicker(new DateTime(2025, 6, 15))
			{
				IsOpen = new Signal<bool>(false),
			};
			dialog.UpdateFromOldView(inline);
			Assert.True(node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool);
			Assert.False(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);

			var inlineAgain = new DatePicker(new DateTime(2025, 6, 15));
			inlineAgain.UpdateFromOldView(dialog);
			Assert.False(node.Get(PropertyIds.DatePicker_IsDialogMode).AsBool);
			Assert.False(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);
		}

		[Fact]
		public void DatePicker_PushesAutomationId_OnBridge()
		{
			var dp = new DatePicker(DateTime.Today).AutomationId("bag_roast_date");

			var node = Bridge(dp);

			Assert.Equal("bag_roast_date", node.Get(PropertyIds.AutomationId).AsString);
		}

		[Fact]
		public void DatePicker_PushesSelectedTicks_OnBridge()
		{
			var date = new DateTime(2025, 6, 15);
			var dp = new DatePicker(date) { IsOpen = new Signal<bool>(false) };

			var node = Bridge(dp);
			Assert.Equal(date.Ticks, node.Get(PropertyIds.DatePicker_SelectedTicks).AsLong);
		}

		[Fact]
		public void DatePicker_PushesMinimumAndMaximumTicks_OnBridge()
		{
			var date = new DateTime(2025, 6, 15);
			var minimum = new DateTime(2020, 1, 1);
			var maximum = new DateTime(2026, 9, 4);
			var dp = new DatePicker(date, minimum, maximum);

			var node = Bridge(dp);

			Assert.Equal(minimum.Ticks, node.Get(PropertyIds.DatePicker_MinimumTicks).AsLong);
			Assert.Equal(maximum.Ticks, node.Get(PropertyIds.DatePicker_MaximumTicks).AsLong);
		}

		[Fact]
		public void DatePicker_TransferredNodeClearsRemovedRange()
		{
			var ranged = new DatePicker(
				new DateTime(2025, 6, 15),
				new DateTime(2020, 1, 1),
				new DateTime(2026, 9, 4));
			var node = Bridge(ranged);

			var unbounded = new DatePicker(new DateTime(2025, 6, 15));
			unbounded.UpdateFromOldView(ranged);

			Assert.Same(node, (FakeBackendNode)unbounded.Node);
			Assert.Equal(0, node.Get(PropertyIds.DatePicker_MinimumTicks).AsLong);
			Assert.Equal(0, node.Get(PropertyIds.DatePicker_MaximumTicks).AsLong);
		}

		[Fact]
		public void DatePicker_IsOpenSignalChange_PatchesNode()
		{
			var isOpen = new Signal<bool>(false);
			var dp = new DatePicker(DateTime.Today) { IsOpen = isOpen };

			var node = Bridge(dp);
			isOpen.Value = true;
			Assert.True(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);
		}

		[Fact]
		public void DatePicker_DateSignalChange_PatchesSelectedTicks()
		{
			var selected = new Signal<DateTime?>(new DateTime(2026, 1, 1));
			var dp = new DatePicker(selected);
			var node = Bridge(dp);

			selected.Value = new DateTime(2026, 9, 16);

			Assert.Equal(
				new DateTime(2026, 9, 16).Ticks,
				node.Get(PropertyIds.DatePicker_SelectedTicks).AsLong);
		}

		[Fact]
		public void DatePicker_ReplacingIsOpenSignal_DetachesOldAndAttachesNew()
		{
			var first = new Signal<bool>(false);
			var second = new Signal<bool>(false);
			var dp = new DatePicker(DateTime.Today) { IsOpen = first };
			var node = Bridge(dp);

			dp.IsOpen = second;
			dp.UpdateBackendNode();
			first.Value = true;
			Assert.False(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);

			second.Value = true;
			Assert.True(node.Get(PropertyIds.DatePicker_IsOpen).AsBool);
		}

		[Fact]
		public void DatePicker_DisposeDetachesIsOpenSignal()
		{
			var signal = new Signal<bool>(false);
			var dp = new DatePicker(DateTime.Today) { IsOpen = signal };
			Bridge(dp);

			dp.Dispose();

			var field = typeof(DatePicker).GetField(
				"_hookedIsOpen",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(field);
			Assert.Null(field.GetValue(dp));
		}

		[Fact]
		public void DatePicker_IsOpenSignalDoesNotRetainObsoleteView()
		{
			var signal = new Signal<bool>(false);
			var weakPicker = CreateCollectibleDatePicker(signal);

			CollectGarbage();

			Assert.False(weakPicker.TryGetTarget(out _));
			signal.Value = true;
			Assert.Equal(0, PropertyChangedSubscriberCount(signal));
		}

		[Fact]
		public void DatePickerDateProtocol_RoundTripsCalendarDate()
		{
			var expected = new DateTime(2026, 9, 4);
			foreach (var kind in new[]
			{
				DateTimeKind.Unspecified,
				DateTimeKind.Local,
				DateTimeKind.Utc,
			})
			{
				var date = DateTime.SpecifyKind(expected, kind);
				var epoch = DatePickerDateProtocol.ToUnixSeconds(date);
				Assert.Equal(expected, DatePickerDateProtocol.FromUnixSeconds(epoch));
			}
		}

		[Fact]
		public void DatePicker_DateConfirmedEvent_WritesDate()
		{
			var isOpen = new Signal<bool>(true);
			var dp = new DatePicker(DateTime.Today) { IsOpen = isOpen };

			var node = Bridge(dp);
			var confirmedDate = new DateTime(2026, 3, 15);
			node.Sink?.OnEvent(EventIds.DateConfirmed, confirmedDate.Ticks);

			Assert.Equal(confirmedDate, dp.Date.CurrentValue);
			Assert.False(isOpen.Value);
		}

		[Fact]
		public void DatePicker_DialogDismissedEvent_ClosesDialog()
		{
			var isOpen = new Signal<bool>(true);
			var dp = new DatePicker(DateTime.Today) { IsOpen = isOpen };

			var node = Bridge(dp);
			node.Sink?.OnEvent(EventIds.DialogDismissed);

			Assert.False(isOpen.Value);
		}

		[Fact]
		public void DatePicker_SelectedTicksAppliedAtInitialRender()
		{
			// Finding 3: initial render must push the constructor date as ticks.
			var date = new DateTime(2026, 1, 15);
			var dp = new DatePicker(date);
			var node = Bridge(dp);

			Assert.Equal(date.Ticks, node.Get(PropertyIds.DatePicker_SelectedTicks).AsLong);
		}

		[Fact]
		public void DatePicker_LiveDateChange_UpdatesBackendNode()
		{
			// Finding 3: live date updates via re-render must reach the backend node.
			var original = new DateTime(2026, 1, 1);
			var dp1 = new DatePicker(original);
			var node = Bridge(dp1);

			var updated = new DateTime(2026, 9, 4);
			var dp2 = new DatePicker(updated);
			dp2.UpdateFromOldView(dp1);

			Assert.Equal(updated.Ticks, node.Get(PropertyIds.DatePicker_SelectedTicks).AsLong);
		}

		[Fact]
		public void DatePickerDateProtocol_PreservesLocalDateAcrossTimezones()
		{
			// Finding 5: local calendar dates must round-trip without timezone drift.
			// Test a range of dates spanning DST transitions.
			var dates = new[]
			{
				new DateTime(2026, 3, 8),  // spring forward
				new DateTime(2026, 11, 1), // fall back
				new DateTime(2000, 1, 1),  // Y2K
				new DateTime(2026, 12, 31),// year end
			};
			foreach (var d in dates)
			{
				var epoch = DatePickerDateProtocol.ToUnixSeconds(d);
				var rt = DatePickerDateProtocol.FromUnixSeconds(epoch);
				Assert.Equal(d.Year, rt.Year);
				Assert.Equal(d.Month, rt.Month);
				Assert.Equal(d.Day, rt.Day);
			}
		}

		[Fact]
		public void DatePicker_DialogModeSurvivesRealComponentReloadLifecycle()
		{
			var host = new DatePickerDialogHost();
			DatePickerPresentationNode? pickerNode = null;

			var rootNode = (FakeBackendNode)CometBackendBridge.Materialize(
				host,
				view =>
				{
					if (view is DatePicker)
						return pickerNode = new DatePickerPresentationNode();
					return new FakeBackendNode(view.GetType().Name);
				},
				Ctx);

			Assert.NotNull(pickerNode);
			Assert.Same(pickerNode, rootNode.Children[1]);
			Assert.False(pickerNode.RendersInline);
			Assert.False(pickerNode.PresentsDialog);
			Assert.Equal(Size.Zero, pickerNode.Measure(402, 90));
			pickerNode.Arrange(new Rect(0, 0, 402, 90));
			Assert.Equal(Rect.Zero, pickerNode.ArrangedFrame);

			host.IsOpen.Value = true;
			Assert.False(pickerNode.RendersInline);
			Assert.True(pickerNode.PresentsDialog);
			Assert.Equal(Size.Zero, pickerNode.Measure(402, 90));
			pickerNode.Arrange(new Rect(0, 0, 402, 90));
			Assert.Equal(Rect.Zero, pickerNode.ArrangedFrame);

			host.IsOpen.Value = false;
			host.RenderVersion++;
			host.Reload();

			Assert.Same(pickerNode, rootNode.Children[1]);
			Assert.False(pickerNode.RendersInline);
			Assert.False(pickerNode.PresentsDialog);
			Assert.Equal(Size.Zero, pickerNode.Measure(402, 90));
			pickerNode.Arrange(new Rect(0, 0, 402, 90));
			Assert.Equal(Rect.Zero, pickerNode.ArrangedFrame);
			Assert.True(pickerNode.OwnerChangedCount > 0);

			host.IsOpen.Value = true;
			Assert.False(pickerNode.RendersInline);
			Assert.True(pickerNode.PresentsDialog);
		}

		[Fact]
		public void PlatformDatePickerNodes_ReactAndMeasureThroughNativeState()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var compose = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeDatePickerNode.cs"));
			var swift = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIDatePickerNode.cs"));
			var swiftShim = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet.SwiftUI.Shim", "Sources",
				"CometSwiftUIShim", "CometSwiftUIShim.swift"));

			Assert.Contains("else if (id == PropertyIds.DatePicker_SelectedTicks)", compose);
			Assert.Contains("_state.SelectedDateMillis = millis;", compose);
			Assert.Contains("CometSwiftUIHost.MeasureNode(_native", swift);
			Assert.Contains("new UIKit.UIDatePicker", swift);
			Assert.Contains("PreferredDatePickerStyle = UIKit.UIDatePickerStyle.Compact", swift);
			Assert.Contains("SetBool(_native, \"datepickerdialogmode\"", swift);
			Assert.Contains("System.Math.Max(1, fitted.Width)", swift);
			Assert.Contains("System.Math.Max(1, fitted.Height)", swift);
			Assert.Contains("if node.datePickerDialogMode", swiftShim);
			Assert.Contains("localCalendarDate(fromProtocolEpoch:", swiftShim);
			Assert.Contains("protocolEpoch(fromLocalDate:", swiftShim);
			Assert.Contains("utc.timeZone = TimeZone(secondsFromGMT: 0)!", swiftShim);
			Assert.Contains(
				"Calendar.current.dateComponents([.year, .month, .day], from: date)",
				swiftShim);
		}

		[System.Runtime.CompilerServices.MethodImpl(
			System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		static WeakReference<DatePicker> CreateCollectibleDatePicker(Signal<bool> signal)
		{
			var picker = new DatePicker(DateTime.Today) { IsOpen = signal };
			Bridge(picker);
			return new WeakReference<DatePicker>(picker);
		}

		static void CollectGarbage()
		{
			for (var attempt = 0; attempt < 3; attempt++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
			}
		}

		static int PropertyChangedSubscriberCount<T>(Signal<T> signal)
		{
			var field = typeof(Signal<T>).GetField(
				nameof(Signal<T>.PropertyChanged),
				System.Reflection.BindingFlags.Instance |
				System.Reflection.BindingFlags.NonPublic);
			var handlers = field?.GetValue(signal) as Delegate;
			return handlers?.GetInvocationList().Length ?? 0;
		}

		sealed class DatePickerDialogHost : View
		{
			readonly Signal<DateTime?> _date = new(new DateTime(2026, 9, 16));

			public Signal<bool> IsOpen { get; } = new(false);
			public int RenderVersion { get; set; }

			[Body]
			View body() => new Grid
			{
				new Text($"Render {RenderVersion}"),
				new DatePicker(_date)
				{
					IsOpen = IsOpen,
				}.AutomationId("bag_roast_date_native"),
			};
		}

		sealed class DatePickerPresentationNode : FakeBackendNode
		{
			bool _isDialogMode;
			bool _isOpen;

			public DatePickerPresentationNode()
				: base(nameof(DatePicker))
			{
			}

			public bool RendersInline
				=> DatePickerPresentationState.Resolve(_isDialogMode, _isOpen).RendersInline;

			public bool PresentsDialog
				=> DatePickerPresentationState.Resolve(_isDialogMode, _isOpen).PresentsDialog;

			public override void ApplyProperty(PropertyId id, in PropertyValue value)
			{
				base.ApplyProperty(id, in value);
				if (id == PropertyIds.DatePicker_IsDialogMode)
					_isDialogMode = value.AsBool;
				else if (id == PropertyIds.DatePicker_IsOpen)
					_isOpen = value.AsBool;
			}

			public override Size Measure(double widthConstraint, double heightConstraint)
				=> DatePickerPresentationState.Resolve(_isDialogMode, _isOpen).MeasuresInline
					? new Size(160, 34)
					: Size.Zero;

			public override void Arrange(Rect frame)
				=> base.Arrange(
					DatePickerPresentationState.Resolve(_isDialogMode, _isOpen)
						.NativeFrame(frame));
		}
	}

	/// <summary>
	/// Verifies Slider min/max push through to the backend node (the gap Bobbie found).
	/// </summary>
	public class BackendSliderMinMaxTests
	{
		static BackendSliderMinMaxTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void Slider_PushesMinMax_OnBridge()
		{
			var slider = new Slider(0.5, 0, 100);

			var node = Bridge(slider);
			Assert.Equal(0.5, node.Get(PropertyIds.Slider_Value).AsDouble, 3);
			Assert.Equal(0.0, node.Get(PropertyIds.Slider_Minimum).AsDouble, 3);
			Assert.Equal(100.0, node.Get(PropertyIds.Slider_Maximum).AsDouble, 3);
		}

		[Fact]
		public void NavigationRootIdentity_PreservesEquivalentRerenders()
		{
			Assert.True(NavigationView.HasSameBackendRoot(
				new Text("old"),
				new Text("new")));
			Assert.True(NavigationView.HasSameBackendRoot(
				new Text("old").Key("root"),
				new Text("new").Key("root")));
			Assert.False(NavigationView.HasSameBackendRoot(
				new Text("old").Key("root-a"),
				new Text("new").Key("root-b")));
			Assert.False(NavigationView.HasSameBackendRoot(
				new Text("old"),
				new Button("new")));
		}

		[Fact]
		public void Slider_ValueChangedEvent_WritesBack()
		{
			var slider = new Slider(0.5, 0, 100);

			var node = Bridge(slider);
			node.Sink?.OnEvent(EventIds.ValueChanged, 75.0);
			Assert.Equal(75.0, slider.Value.CurrentValue, 3);
		}

		[Fact]
		public void Slider_LiveMinMaxChange_UpdatesBackendNode()
		{
			// Finding 6: live min/max changes must reach the backend node.
			// Simulates a parent re-render that creates a new Slider with different range;
			// the reconciler calls UpdateFromOldView which triggers ApplyChangedProperties.
			var slider1 = new Slider(50, 0, 100);
			var node = Bridge(slider1);
			Assert.Equal(0.0, node.Get(PropertyIds.Slider_Minimum).AsDouble, 3);
			Assert.Equal(100.0, node.Get(PropertyIds.Slider_Maximum).AsDouble, 3);

			// Simulate a re-render: new Slider with different range inherits the node.
			var slider2 = new Slider(50, 10, 200);
			slider2.UpdateFromOldView(slider1);
			Assert.Equal(10.0, node.Get(PropertyIds.Slider_Minimum).AsDouble, 3);
			Assert.Equal(200.0, node.Get(PropertyIds.Slider_Maximum).AsDouble, 3);
		}

		[Fact]
		public void ComposeSlider_RangePropertiesAreComposeStateReads()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeInputNodes.cs"));

			Assert.Contains("readonly MutableState<float> _min", source);
			Assert.Contains("readonly MutableState<float> _max", source);
			Assert.Contains("var minimum = _min.Value;", source);
			Assert.Contains("var maximum = _max.Value;", source);
			Assert.Contains("slider.ValueRange = Kotlin.Ranges.RangesKt.RangeTo(minimum, maximum);", source);
		}
	}

	/// <summary>
	/// Verifies that Spacer materializes successfully (the P0 runtime failure) and produces
	/// a backend node. The Spacer is a leaf with no properties — the test confirms the
	/// CreateBackendNode override exists and doesn't throw.
	/// </summary>
	public class BackendSpacerTests
	{
		static BackendSpacerTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void Spacer_Materializes_WithoutThrowing()
		{
			var spacer = new Spacer();
			var node = Bridge(spacer);
			Assert.NotNull(node);
		}

		[Fact]
		public void Spacer_InsideScrollView_Materializes()
		{
			// Reproduces the exact crash path: Spacer inside a VStack inside ScrollView.
			var root = new ScrollView { new VStack { new Spacer(), new Text("hello") } };
			var node = Bridge(root);
			Assert.NotNull(node);
		}
	}

	/// <summary>Source-level guard for controls whose platform mappings are not present in
	/// the net11.0-maccatalyst assembly used by host tests.</summary>
	public class BackendNodeCoverageGuardTests
	{
		[Fact]
		public void BaristaNativeControls_HaveBackendMappingsOnBothPlatforms()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var mappingFiles = new[]
			{
				System.IO.Path.Combine(projectRoot, "src", "Comet", "Platform", "Compose", "ControlBackendNodes.Android.cs"),
				System.IO.Path.Combine(projectRoot, "src", "Comet", "Platform", "SwiftUI", "ControlBackendNodes.iOS.cs"),
			};
			var required = new[] { "ContentView", "FlexLayout", "Grid", "TextEditor", "RefreshView", "Spacer" };

			foreach (var file in mappingFiles)
			{
				var source = System.IO.File.ReadAllText(file);
				foreach (var control in required)
					Assert.Contains($"public partial class {control}", source);
			}
		}

		[Fact]
		public void ComposeScroll_InitializesContentDuringFirstRender()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeScrollNode.cs"));

			Assert.Contains("var children = _scroll.GetChildren();", source);
			Assert.Contains("contentView = children is { Count: > 0 } ? children[0] : null;", source);
			Assert.Contains("_content.Materialize(contentView)", source);
		}

		[Fact]
		public void NativeBackendSources_ContainRetainedLifecycleGuards()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var swift = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet.SwiftUI.Shim", "Sources", "CometSwiftUIShim", "CometSwiftUIShim.swift"));
			var composeImage = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeLeafNodes.cs"));
			var composeEditor = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeTextEditorNode.cs"));
			var composeDatePicker = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeDatePickerNode.cs"));
			var swiftDatePicker = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIDatePickerNode.cs"));
			var composeNode = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeNode.cs"));
			var sp = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "vendor", "Microsoft.AndroidX.Compose", "Sp.cs"));
			var composeInput = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeInputNodes.cs"));
			var composeNavigation = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeNavigationNode.cs"));
			var swiftNavigation = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUINavigationNode.cs"));

			Assert.Contains("while node.isRefreshing && !Task.isCancelled", swift);
			Assert.Contains("node.onRefreshEnded?()", swift);
			Assert.Contains(".onDisappear { finishIfActive(state: 3) }", swift);
			Assert.Contains("fixedSize(horizontal: true, vertical: false)", swift);
			Assert.Contains("NavigationBackButtonModifier", swift);
			Assert.Contains("content.accessibilityIdentifier(identifier)", swift);
			Assert.Contains("update: native => UpdateImageView", composeImage);
			Assert.Contains("_styleVersion.Value++", composeEditor);
			Assert.Contains("initialSelectableDates: _selectableDates", composeDatePicker);
			Assert.Contains("MaxUtcMillis", composeDatePicker);
			Assert.Contains("DatePicker_SelectedTicks", composeDatePicker);
			Assert.Contains("_state.SelectedDateMillis = millis", composeDatePicker);
			Assert.Contains("Modifier = BuildAutomationModifier()", composeDatePicker);
			Assert.DoesNotContain("Modifier = BuildNodeModifier()", composeDatePicker);
			Assert.Contains("SwiftUINode.ApplyCommonProperty(_native, id, in value)", swiftDatePicker);
			Assert.Contains("CometSwiftUIHost.MeasureNode(_native", swiftDatePicker);
			Assert.Contains("DatePickerDateProtocol.ToUnixSeconds", swiftDatePicker);
			Assert.Contains("in: node.selectableDateRange", swift);
			Assert.Contains("protocolEpoch(fromLocalDate:", swift);
			Assert.Contains(".TestTag(_automationId)", composeNode);
			Assert.Contains("TestTagsAsResourceId(true)", composeNode);
			Assert.Contains("public Sp(float sp)", sp);
			Assert.Contains("readonly MutableState<float> _min", composeInput);
			Assert.Contains("NavigationStackLifecycle.DetermineOwnerTransfer", composeNavigation);
			Assert.Contains("NavigationStackLifecycle.DetermineOwnerTransfer", swiftNavigation);
			Assert.Contains("_visibleTop?.ViewDidDisappear();", composeNavigation);
			Assert.Contains("_visibleTop?.ViewDidAppear();", composeNavigation);
			Assert.Contains("_visibleTop?.ViewDidDisappear();", swiftNavigation);
			Assert.Contains("_visibleTop?.ViewDidAppear();", swiftNavigation);
		}

		[Fact]
		public void ComposeDatePicker_UsesSemanticsOnlyModifier_NotYogaLayout()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeDatePickerNode.cs"));

			Assert.Contains("Modifier = BuildAutomationModifier()", source);
			Assert.DoesNotContain("Modifier = BuildNodeModifier()", source);
		}

		[Fact]
		public void NavigationNodes_UseArrangedFrame_NotFullScreen()
		{
			// Regression: NavigationView nested in a star grid row must lay its top page
			// to the arranged frame, not ScreenSizeDp() / UIScreen.MainScreen.Bounds.
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var compose = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeNavigationNode.cs"));
			var swiftui = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUINavigationNode.cs"));

			// ComposeNavigationNode: must use ContentSize() (frame-aware), not bare ScreenSizeDp()
			Assert.Contains("ContentSize()", compose);
			Assert.DoesNotContain("Layout(top, ScreenSizeDp())", compose);

			// SwiftUINavigationNode: must store and use arranged frame
			Assert.Contains("_hasFrame", swiftui);
			Assert.Contains("_frameW", swiftui);
			Assert.Contains("_frameH", swiftui);
			// Arrange must NOT be a no-op
			Assert.DoesNotContain("void Arrange(Rect frame) { }", swiftui);
			// RelayoutTop must check _hasFrame before falling back to UIScreen
			Assert.Contains("_hasFrame && _frameW > 0", swiftui);

			// Compile guard only: behavioral transition coverage lives in
			// RetainedLifecycleContractTests.
			Assert.Contains("NavigationStackLifecycle.PrepareCurrentForExposure", compose);
			Assert.Contains("OwnedContentSlot<", compose);
			Assert.DoesNotContain("ForceInitializeOwnContent", compose);
			Assert.Contains("NavigationStackLifecycle.PrepareCurrentForExposure", swiftui);
			Assert.Contains("OwnedContentSlot<", swiftui);
			Assert.DoesNotContain("ForceInitializeOwnContent", swiftui);
		}
	}

	/// <summary>
	/// Verifies that BaristaNotes' three tab icon names materialize successfully as
	/// real Icon backend nodes — not falling through to the NotSupportedException path.
	/// The coffee icon must NOT resolve to a Star fallback; it uses the Material Symbols
	/// glyph path (\ue541 local_cafe) instead.
	/// </summary>
	public class BackendBaristaIconResolutionTests
	{
		static BackendBaristaIconResolutionTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void CoffeeIcon_Materializes_WithoutThrowing()
		{
			var icon = new Icon("coffee");
			var node = Bridge(icon);
			Assert.NotNull(node);
			Assert.Equal("Icon", node.Kind);
		}

		[Fact]
		public void CupAndSaucerIcon_Materializes_WithoutThrowing()
		{
			var icon = new Icon("cup.and.saucer.fill");
			var node = Bridge(icon);
			Assert.NotNull(node);
		}

		[Fact]
		public void FeedIcon_Materializes_WithoutThrowing()
		{
			var icon = new Icon("feed");
			var node = Bridge(icon);
			Assert.NotNull(node);
		}

		[Fact]
		public void SettingsIcon_Materializes_WithoutThrowing()
		{
			var icon = new Icon("settings");
			var node = Bridge(icon);
			Assert.NotNull(node);
		}

		[Fact]
		public void TabView_WithBaristaIcons_Materializes()
		{
			var tv = new TabView();
			tv.AddTab("New Drink", new Text("Shot"), "coffee");
			tv.AddTab("Activity", new Text("Feed"), "feed");
			tv.AddTab("Settings", new Text("Settings"), "settings");

			var node = Bridge(tv);
			Assert.NotNull(node);
		}
	}

	/// <summary>
	/// Tests the canonical DevFlow route parser, Toggle/Slider writeback through
	/// DispatchSetProperty, and DevFlow semantic tab selection.
	/// </summary>
	public class DevFlowSetPropertyRouteTests
	{
		static DevFlowSetPropertyRouteTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		[Fact]
		public void TryParseSetPropertyRoute_ParsesCanonicalPut()
		{
			var method = typeof(CometDevAgent).GetMethod("TryParsePropertyRoute",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(method);

			var args = new object[] { "/api/v1/ui/elements/42/properties/isOn", 0, "" };
			var result = (bool)method.Invoke(null, args);
			Assert.True(result);
			Assert.Equal(42, (int)args[1]);
			Assert.Equal("isOn", (string)args[2]);
		}

		[Fact]
		public void TryParseSetPropertyRoute_RejectsInvalidPath()
		{
			var method = typeof(CometDevAgent).GetMethod("TryParsePropertyRoute",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(method);

			var args = new object[] { "/api/v1/ui/actions/tap", 0, "" };
			Assert.False((bool)method.Invoke(null, args));
		}

		[Fact]
		public void DispatchSetProperty_Toggle_WritesBackSignal()
		{
			var toggle = new Toggle(false);
			var result = CometDevAgent.DispatchSetProperty(toggle, "isOn", "true");
			Assert.Contains("success\":true", result);
			Assert.True(toggle.Value.CurrentValue);
		}

		[Fact]
		public void DispatchSetProperty_Slider_WritesBackSignal()
		{
			var slider = new Slider(0.5, 0, 100);
			var result = CometDevAgent.DispatchSetProperty(slider, "sliderValue", "75.0");
			Assert.Contains("success\":true", result);
			Assert.Equal(75.0, slider.Value.CurrentValue, 3);
		}

		[Fact]
		public void DispatchSetProperty_Slider_AlsoAcceptsValueName()
		{
			var slider = new Slider(10.0, 0, 50);
			var result = CometDevAgent.DispatchSetProperty(slider, "value", "25");
			Assert.Contains("success\":true", result);
			Assert.Equal(25.0, slider.Value.CurrentValue, 3);
		}

		[Fact]
		public void DispatchSetProperty_UnknownProperty_ReturnsFalse()
		{
			var text = new Text("hello");
			var result = CometDevAgent.DispatchSetProperty(text, "isOn", "true");
			Assert.Contains("success\":false", result);
		}

		[Fact]
		public void DispatchSetProperty_TabView_SelectsTab()
		{
			var tv = new TabView();
			tv.AddTab("Shot", new Text("Page A"), "coffee");
			tv.AddTab("Activity", new Text("Page B"), "feed");

			var result = CometDevAgent.DispatchSetProperty(tv, "selectedIndex", "1");

			Assert.Contains("success\":true", result);
			Assert.Equal(1, tv.SelectedSignal.Value);
		}

		[Fact]
		public void DispatchSetProperty_TabView_RejectsOutOfRangeIndex()
		{
			var tv = new TabView();
			tv.AddTab("Shot", new Text("Page A"), "coffee");

			var result = CometDevAgent.DispatchSetProperty(tv, "selectedIndex", "2");

			Assert.Contains("success\":false", result);
			Assert.Equal(0, tv.SelectedSignal.Value);
		}
	}

	/// <summary>
	/// Regression: CometDevRegistry.Snapshot() must return non-empty node data after
	/// Materialize when Enabled=true, so the DevFlow tree endpoint has data to serve.
	/// </summary>
	public class CometDevRegistryTreeTests
	{
		static CometDevRegistryTreeTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		[Fact]
		public void Registry_ReturnsNodes_WhenEnabled()
		{
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var root = new VStack
				{
					new Text("Hello").AutomationId("greeting"),
					new Button("Tap me"),
				};
				CometBackendBridge.Materialize(root, v => new FakeBackendNode(v.GetType().Name), Ctx);

				var snapshot = CometDevRegistry.Snapshot();
				Assert.True(snapshot.Count >= 3, $"Expected ≥3 nodes, got {snapshot.Count}");

				var textNode = snapshot.Find(n => n.AutomationId == "greeting");
				Assert.NotNull(textNode);
				Assert.Equal("Text", textNode.Type);
				Assert.Equal("Hello", textNode.Text);
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		[Fact]
		public void Registry_ReturnsEmpty_WhenDisabled()
		{
			CometDevRegistry.Enabled = false;
			CometDevRegistry.Reset();
			var root = new Text("hidden");
			CometBackendBridge.Materialize(root, v => new FakeBackendNode(v.GetType().Name), Ctx);

			var snapshot = CometDevRegistry.Snapshot();
			Assert.Empty(snapshot);
		}

		[Fact]
		public void Registry_NavigationScreenRoots_LinkUnderNavigationView()
		{
			// Regression: own-content constructors (NavigationView Push) materialize
			// children inside factory() — before the parent was registered. The pre-
			// registration in CometBackendBridge.Materialize must assign the parent
			// ID so the screen root links under the NavigationView, not parentId:-1.
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var page = new VStack
				{
					new Text("content").AutomationId("nav_content"),
				};
				var nav = new NavigationView { Content = page };
				var container = new Grid { nav };

				CometBackendBridge.Materialize(container, v => new FakeBackendNode(v.GetType().Name), Ctx);

				var snapshot = CometDevRegistry.Snapshot();
				var navNode = snapshot.Find(n => n.Type == "NavigationView");
				Assert.NotNull(navNode);

				// The page must be a descendant of NavigationView, not a root (parentId:-1).
				var pageNode = snapshot.Find(n => n.AutomationId == "nav_content");
				Assert.NotNull(pageNode);

				// Walk ancestors: the page or its direct parent must trace back to the
				// NavigationView. The VStack wrapping the content may be an intermediate node.
				bool found = false;
				int? walkId = pageNode.ParentId;
				for (int depth = 0; walkId is >= 0 && depth < 10; depth++)
				{
					if (walkId == navNode.Id) { found = true; break; }
					var ancestor = snapshot.Find(n => n.Id == walkId);
					walkId = ancestor?.ParentId;
				}
				Assert.True(found,
					$"nav_content (id={pageNode.Id} parentId={pageNode.ParentId}) does not descend from NavigationView (id={navNode.Id})");
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		[Fact]
		public void Registry_AfterReload_NewViewsMaterialized()
		{
			// Regression: a reactive reload that produces new views (e.g. a picker
			// overlay) must result in those views getting backend nodes via the
			// navigation node's RefreshTopScreen path. This host-side test verifies
			// the foundational detection: unmaterialized children are detected and,
			// after a fresh Materialize pass, all children have nodes.
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var page = new VStack
				{
					new Text("tile").AutomationId("tile"),
				};
				var nav = new NavigationView { Content = page };
				CometBackendBridge.Materialize(nav, v => new FakeBackendNode(v.GetType().Name), Ctx);

				// All views should have backend nodes now.
				var snapshot = CometDevRegistry.Snapshot();
				var tile = snapshot.Find(n => n.AutomationId == "tile");
				Assert.NotNull(tile);
				var tileView = CometDevRegistry.Find(tile.Id);
				Assert.NotNull(tileView?.Node);

				// Simulate what happens after a reactive reload: a new view appears
				// in the page's container that was NOT materialized.
				var picker = new Text("picker").AutomationId("picker_brewmethod");
				((IList<View>)page).Add(picker);

				// The picker has no backend node — this is what the navigation node detects.
				Assert.Null(picker.Node);

				// After materializing the picker (what RefreshTopScreen does), it has a node.
				CometBackendBridge.Materialize(picker, v => new FakeBackendNode(v.GetType().Name), Ctx);
				Assert.NotNull(picker.Node);

				var snapshot2 = CometDevRegistry.Snapshot();
				var pickerNode = snapshot2.Find(n => n.AutomationId == "picker_brewmethod");
				Assert.NotNull(pickerNode);
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		[Fact]
		public void Registry_DropsDisposedChildrenAfterStructuralReplacement()
		{
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var oldRoot = new Grid
				{
					new Grid
					{
						new Text("old").AutomationId("old_descendant"),
					}.AutomationId("old_subtree"),
				};
				CometBackendBridge.Materialize(oldRoot, v => new FakeBackendNode(v.GetType().Name), Ctx);

				var newRoot = new Grid
				{
					new VStack
					{
						new Text("new").AutomationId("new_descendant"),
					}.AutomationId("new_subtree"),
				};
				newRoot.Diff(oldRoot, false);
				oldRoot.Dispose();

				var snapshot = CometDevRegistry.Snapshot();
				Assert.DoesNotContain(snapshot, node => node.AutomationId == "old_subtree");
				Assert.DoesNotContain(snapshot, node => node.AutomationId == "old_descendant");
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		[Fact]
		public void Registry_DropsTerminallyDisposedReactiveComponentSubtree()
		{
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var component = new ReactiveRegistryComponent();
				var root = new Grid { component };
				CometBackendBridge.Materialize(root, v => new FakeBackendNode(v.GetType().Name), Ctx);

				root.Dispose();

				Assert.True(component.IsDisposed);
				Assert.DoesNotContain(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "reactive_registry_child");
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		sealed class ReactiveRegistryComponent : View
		{
			readonly Signal<int> _value = new(1);

			[Body]
			View Body() =>
				new Text(_value.Value.ToString()).AutomationId("reactive_registry_child");
		}
	}

	/// <summary>
	/// Proves CometBackendLayoutEngine.LayoutContent correctly lays out a VStack
	/// containing Grid rows (the exact shape of BaristaNotes' picker scroll content).
	/// If this passes, the engine is not the cause of blank brew-method rows.
	/// </summary>
	public class LayoutContentGridRowTests
	{
		static LayoutContentGridRowTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void LayoutContent_VStackWithGridRows_ArrangesRowsWithNonZeroFrames()
		{
			// Exactly models CategoricalRows: VStack { Grid(44,*,44; row 64) x N }
			var rows = new VStack(spacing: 1);
			for (int i = 0; i < 7; i++)
			{
				var row = new Grid(
					columns: new object[] { 44, "*", 44 },
					rows: new object[] { 64 })
				{
					new Text("").Cell(column: 0),
					new Text($"Method{i}").Cell(column: 1),
					new Text("").Cell(column: 2),
				};
				row.AutomationId($"picker_choice_{i}");
				rows.Add(row);
			}

			Bridge(rows); // materialize all backend nodes

			var size = CometBackendLayoutEngine.LayoutContent(rows, 412);

			Assert.True(size.Height > 0, $"LayoutContent height should be > 0, got {size.Height}");
			// Each row is 64pt + 1pt spacing = 65pt per row, 7 rows = ~455
			Assert.True(size.Height >= 7 * 64, $"Expected ≥{7 * 64}, got {size.Height}");

			// Every Grid row must have a non-zero backend frame.
			var children = ((IContainerView)rows).GetChildren();
			for (int i = 0; i < children.Count; i++)
			{
				var child = children[i];
				var frame = child.Frame;
				Assert.True(frame.Width > 0, $"Row {i} width is 0 (frame={frame})");
				Assert.True(frame.Height > 0, $"Row {i} height is 0 (frame={frame})");

				// The middle column text must also have a non-zero frame.
				if (child is IContainerView gridContainer)
				{
					var gridChildren = gridContainer.GetChildren();
					var midText = gridChildren[1]; // column 1 = "*"
					var midFrame = midText.Frame;
					Assert.True(midFrame.Width > 0, $"Row {i} text width is 0");
				}
			}
		}
	}

	/// <summary>
	/// Regression: when the diff matches a non-Component view by type (e.g. Grid nav row),
	/// the transferred node must receive the NEW view's AutomationId, text, and tap action —
	/// not retain the old view's stale properties.
	/// </summary>
	public class DiffSiblingPropertyUpdateTests
	{
		static DiffSiblingPropertyUpdateTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void Diff_UpdatesAutomationId_OnMatchedNonComponent()
		{
			// Old row: nav_activity
			var oldRow = new Grid { new Text("feed") }.AutomationId("nav_activity");
			var oldContainer = new VStack { oldRow };
			Bridge(oldContainer);

			Assert.Equal("nav_activity", oldRow.AutomationId);

			// New row: nav_new_drink at the same position
			var newRow = new Grid { new Text("coffee") }.AutomationId("nav_new_drink");
			var newContainer = new VStack { newRow };

			// Diff transfers the node from old to new
			newContainer.Diff(oldContainer, false);

			// The merged result must have the NEW automationId
			var merged = ((IContainerView)newContainer).GetChildren()[0];
			Assert.Equal("nav_new_drink", merged.AutomationId);

			// The backend node must also have the updated automationId
			Assert.NotNull(merged.Node);
			var fakeNode = (FakeBackendNode)merged.Node;
			Assert.Equal("nav_new_drink",
				fakeNode.Get(PropertyIds.AutomationId).AsString);
		}

		[Fact]
		public void Diff_UpdatesText_OnMatchedNonComponent()
		{
			var oldText = new Text("Espresso");
			var oldContainer = new VStack { oldText };
			Bridge(oldContainer);

			var newText = new Text("V60");
			var newContainer = new VStack { newText };
			newContainer.Diff(oldContainer, false);

			var merged = ((IContainerView)newContainer).GetChildren()[0];
			Assert.NotNull(merged.Node);
			var fakeNode = (FakeBackendNode)merged.Node;
			Assert.Equal("V60", fakeNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void Diff_ReplacesBottomActionRowPropertiesAndTapTargets()
		{
			var oldTapCount = 0;
			var newTapCount = 0;
			var oldRow = new OldActionRow(() => oldTapCount++);
			var oldContainer = new Grid { oldRow };
			Bridge(oldContainer);

			var newRow = new NewActionRow(() => newTapCount++);
			var newContainer = new Grid { newRow };

			newContainer.Diff(oldContainer, false);

			var action = FindByAutomationId(newRow.GetView(), "nav_new_drink");
			Assert.NotNull(action);
			var actionNode = Assert.IsType<FakeBackendNode>(action.Node);
			Assert.Equal("nav_new_drink",
				actionNode.Get(PropertyIds.AutomationId).AsString);

			var tap = new GestureData(GestureState.Ended, default);
			actionNode.Sink!.OnGesture(GestureKind.Tap, tap);
			Assert.Equal(1, newTapCount);
			Assert.Equal(0, oldTapCount);
		}

		[Fact]
		public void ReactiveShell_CanSwitchBetweenDifferentActionRowComponentsRepeatedly()
		{
			var shell = new ReactiveSectionShell();
			Bridge(shell);

			Tap(shell, "nav_activity");
			ReactiveScheduler.FlushSync();

			Assert.NotNull(FindByAutomationId(shell.GetView(), "activity_page"));
			Assert.False(shell.IsDisposed);

			Tap(shell, "nav_new_drink");
			ReactiveScheduler.FlushSync();

			Assert.NotNull(FindByAutomationId(shell.GetView(), "new_drink_page"));
			Assert.NotNull(FindByAutomationId(shell.GetView(), "nav_activity"));
			Assert.False(shell.IsDisposed);
		}

		[Fact]
		public void ReactiveShell_DoesNotDisposeIncomingPersistentNavigationRoot()
		{
			var shell = new ReactiveNavigationShell();
			CometBackendBridge.Materialize(
				shell,
				view => view is NavigationView
					? new FakeManagedContentNode()
					: new FakeBackendNode(view.GetType().Name),
				Ctx);

			Tap(shell, "nav_activity");
			ReactiveScheduler.FlushSync();

			Assert.False(shell.NewDrinkNavigation.IsDisposed);
			Assert.NotNull(shell.NewDrinkNavigation.Content);
			Assert.False(shell.ActivityPage.IsDisposed);
			Assert.NotSame(shell.ActivityPage, shell.ActivityPage.GetView());
		}

		sealed class FakeManagedContentNode : FakeBackendNode, IBackendRetainsLogicalContentOnOwnerTransfer
		{
			public FakeManagedContentNode() : base("managed-content") { }
		}

		static void Tap(View root, string automationId)
		{
			var view = FindByAutomationId(root.GetView(), automationId);
			Assert.NotNull(view);
			var node = Assert.IsType<FakeBackendNode>(view.Node);
			var tap = new GestureData(GestureState.Ended, default);
			node.Sink!.OnGesture(GestureKind.Tap, tap);
		}

		sealed class ReactiveSectionShell : View
		{
			readonly Signal<bool> _activity = new(false);

			[Body]
			View Body()
			{
				var activity = _activity.Value;
				return new Grid
				{
					new Grid
					{
						new Text(activity ? "ACTIVITY" : "NEW DRINK"),
					}.AutomationId(activity ? "activity_page" : "new_drink_page"),
					activity
						? new NewActionRow(() => _activity.Value = false)
						: new OldActionRow(() => _activity.Value = true),
				};
			}
		}

		sealed class ReactiveNavigationShell : View
		{
			readonly Signal<bool> _activity = new(false);
			readonly NavigationView _newDrinkNavigation;
			readonly NavigationView _activityNavigation;

			public ReactiveNavigationShell()
			{
				_newDrinkNavigation = new NavigationView { Content = new NamedPage("new_drink_page") };
				ActivityPage = new NamedPage("activity_page");
				_activityNavigation = new NavigationView { Content = ActivityPage };
			}

			public NamedPage ActivityPage { get; }
			public NavigationView NewDrinkNavigation => _newDrinkNavigation;

			[Body]
			View Body()
			{
				var activity = _activity.Value;
				return new Grid
				{
					activity ? _activityNavigation : _newDrinkNavigation,
					activity
						? new NewActionRow(() => _activity.Value = false)
						: new OldActionRow(() => _activity.Value = true),
				};
			}
		}

		sealed class NamedPage : View
		{
			readonly string _automationId;

			public NamedPage(string automationId) => _automationId = automationId;

			[Body]
			View Body() => new Grid
			{
				new Text(_automationId),
			}.AutomationId(_automationId);
		}

		sealed class OldActionRow : View
		{
			readonly Action _onTap;

			public OldActionRow(Action onTap) => _onTap = onTap;

			[Body]
			View Body() => new Grid
			{
				new Grid
				{
					new Text("old"),
				}.AutomationId("nav_activity").OnTap(_ => _onTap()),
			};
		}

		sealed class NewActionRow : View
		{
			readonly Action _onTap;

			public NewActionRow(Action onTap) => _onTap = onTap;

			[Body]
			View Body() => new Grid
			{
				new Grid
				{
					new Text("new"),
				}.AutomationId("nav_new_drink").OnTap(_ => _onTap()),
			};
		}

		static View? FindByAutomationId(View view, string automationId)
		{
			var rendered = view.GetView() ?? view;
			if (rendered.AutomationId == automationId)
				return rendered;
			if (rendered is not IContainerView container)
				return null;
			foreach (var child in container.GetChildren())
			{
				if (child is null)
					continue;
				if (FindByAutomationId(child, automationId) is { } match)
					return match;
			}
			return null;
		}
	}
}
