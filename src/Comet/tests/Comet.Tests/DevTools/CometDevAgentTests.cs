using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Comet.Backend;
using Comet.DevTools;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class CometDevAgentTests
	{
		static CometDevAgentTests() =>
			ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		[Theory]
		[InlineData("\"300\"")]
		[InlineData("null")]
		[InlineData("true")]
		[InlineData("1.5")]
		[InlineData("0")]
		[InlineData("10001")]
		public void DragAction_InvalidDuration_DoesNotInvokeDrag(string duration)
		{
			var calls = 0;
			CometDevRegistry.DragInjector = (_, _, _, _, _) =>
			{
				calls++;
				return true;
			};

			try
			{
				var result = CometDevAgent.DragAction(
					$"{{\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4,\"durationMs\":{duration}}}");

				Assert.Contains("\"success\":false", result);
				Assert.Equal(0, calls);
			}
			finally
			{
				CometDevRegistry.DragInjector = null;
			}
		}

		[Theory]
		[InlineData("", 300)]
		[InlineData(",\"durationMs\":1", 1)]
		[InlineData(",\"durationMs\":10000", 10000)]
		public void DragAction_ValidDuration_InvokesDrag(string durationProperty, int expectedDuration)
		{
			var actualDuration = 0;
			CometDevRegistry.DragInjector = (_, _, _, _, duration) =>
			{
				actualDuration = duration;
				return true;
			};

			try
			{
				var result = CometDevAgent.DragAction(
					$"{{\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4{durationProperty}}}");

				Assert.Contains("\"success\":true", result);
				Assert.Equal(expectedDuration, actualDuration);
			}
			finally
			{
				CometDevRegistry.DragInjector = null;
			}
		}

		[Fact]
		public void InspectionRoute_ReturnsPublishedWindowMetricsAndEveryLiveFrame()
		{
			var originalSize = CometWindowMetrics.Shared.SizeDp.Peek();
			var originalSafeArea = CometWindowMetrics.Shared.SafeAreaDp.Peek();
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;

			try
			{
				CometWindowMetrics.Shared.Update(new Size(411.5, 914.25));
				CometWindowMetrics.Shared.UpdateSafeArea(new Microsoft.Maui.Thickness(1, 24, 2, 34));
				CometDevRegistry.NativeWindowMetricsProvider = () =>
					new CometDevRegistry.NativeWindowMetrics
					{
						Size = new Size(1080, 2400),
						SafeAreaInsets = new Microsoft.Maui.Thickness(0, 63, 0, 126),
						SafeAreaLayoutFrame = new Rect(0, 63, 1080, 2211),
						Units = "physicalPixels",
						Scale = 2.625,
						Source = "test-native-window",
					};

				var root = new Grid { AccessibilityId = "root" };
				root.Frame = new Rect(0, 0, 411.5, 914.25);
				var child = new Text("Child") { AccessibilityId = "child" };
				child.Frame = new Rect(12, 30, 100, 40);
				var secondRoot = new VStack { AccessibilityId = "overlay" };
				secondRoot.Frame = new Rect(4, 8, 80, 60);
				CometDevRegistry.Register(root, null!, null);
				CometDevRegistry.Register(child, null!, root);
				CometDevRegistry.Register(secondRoot, null!, null);

				var dispatches = 0;
				var agent = new CometDevAgent(9223, action =>
				{
					dispatches++;
					action();
				});
				var json = agent.RouteForTest("GET", "/api/v1/ui/inspection", "");

				using var document = JsonDocument.Parse(json);
				var response = document.RootElement;
				Assert.True(response.GetProperty("success").GetBoolean());
				Assert.Equal(1, dispatches);
				Assert.Equal(411.5, response.GetProperty("logicalWindow").GetProperty("size").GetProperty("width").GetDouble());
				Assert.Equal("CometWindowMetrics.Shared.SizeDp",
					response.GetProperty("logicalWindow").GetProperty("source").GetString());
				Assert.Equal(24, response.GetProperty("safeAreaDp").GetProperty("insets").GetProperty("top").GetDouble());
				Assert.Equal("CometWindowMetrics.Shared.SafeAreaDp",
					response.GetProperty("safeAreaDp").GetProperty("source").GetString());

				var native = response.GetProperty("nativeWindow");
				Assert.True(native.GetProperty("available").GetBoolean());
				Assert.Equal("physicalPixels", native.GetProperty("units").GetString());
				Assert.Equal(1080, native.GetProperty("size").GetProperty("width").GetDouble());
				Assert.Equal(63, native.GetProperty("safeAreaInsets").GetProperty("value").GetProperty("top").GetDouble());
				Assert.True(native.GetProperty("safeAreaLayoutFrame").GetProperty("available").GetBoolean());
				Assert.Equal(2211, native.GetProperty("safeAreaLayoutFrame").GetProperty("value").GetProperty("height").GetDouble());

				var geometry = response.GetProperty("elementGeometry");
				Assert.Equal("parent", geometry.GetProperty("coordinateSpace").GetString());
				Assert.Equal("logicalPixels", geometry.GetProperty("units").GetString());
				Assert.Equal("layoutAllocation", geometry.GetProperty("kind").GetString());
				Assert.False(geometry.GetProperty("nativeElementBounds").GetProperty("available").GetBoolean());

				var elements = response.GetProperty("elements").EnumerateArray().ToArray();
				Assert.Equal(3, elements.Length);
				Assert.Equal(3, elements.Select(element => element.GetProperty("id").GetString()).Distinct().Count());
				var rootJson = Assert.Single(elements, element =>
					element.TryGetProperty("automationId", out var id) && id.GetString() == "root");
				Assert.Equal(JsonValueKind.Null, rootJson.GetProperty("parentId").ValueKind);
				var childJson = Assert.Single(elements, element =>
					element.TryGetProperty("automationId", out var id) && id.GetString() == "child");
				Assert.Equal(rootJson.GetProperty("id").GetString(), childJson.GetProperty("parentId").GetString());
				Assert.Equal(12, childJson.GetProperty("frame").GetProperty("x").GetDouble());
				Assert.Equal(30, childJson.GetProperty("frame").GetProperty("y").GetDouble());
			}
			finally
			{
				CometDevRegistry.NativeWindowMetricsProvider = null;
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
				CometWindowMetrics.Shared.Update(originalSize);
				CometWindowMetrics.Shared.UpdateSafeArea(originalSafeArea);
			}
		}

		[Fact]
		public void CanonicalTree_ReportsParentRelativeAllocationWithoutInventingWindowBounds()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;

			try
			{
				var firstRoot = new Grid { AccessibilityId = "first-root" };
				firstRoot.Frame = new Rect(0, 0, 400, 800);
				var child = new Text("Child") { AccessibilityId = "nested" };
				child.Frame = new Rect(10, 20, 120, 30);
				var secondRoot = new VStack { AccessibilityId = "second-root" };
				secondRoot.Frame = new Rect(5, 6, 70, 80);
				CometDevRegistry.Register(firstRoot, null!, null);
				CometDevRegistry.Register(child, null!, firstRoot);
				CometDevRegistry.Register(secondRoot, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var json = agent.RouteForTest("GET", "/api/v1/ui/tree", "");

				using var document = JsonDocument.Parse(json);
				var roots = document.RootElement.EnumerateArray().ToArray();
				Assert.Equal(2, roots.Length);
				Assert.Equal(new[] { "first-root", "second-root" },
					roots.Select(root => root.GetProperty("automationId").GetString()).ToArray());

				var nested = Assert.Single(roots[0].GetProperty("children").EnumerateArray());
				Assert.Equal(10, nested.GetProperty("bounds").GetProperty("x").GetDouble());
				Assert.Equal(20, nested.GetProperty("bounds").GetProperty("y").GetDouble());
				Assert.Equal("comet-parent-relative-layout-allocation",
					nested.GetProperty("boundsQuality").GetString());
				var semantics = nested.GetProperty("frameworkProperties");
				Assert.Equal("parent", semantics.GetProperty("boundsCoordinateSpace").GetString());
				Assert.Equal("logicalPixels", semantics.GetProperty("boundsUnits").GetString());
				Assert.Equal("false", semantics.GetProperty("nativeBoundsAvailable").GetString());
				Assert.False(nested.TryGetProperty("windowBounds", out _));

				using var legacyDocument = JsonDocument.Parse(
					agent.RouteForTest("GET", "/tree", ""));
				var legacyNodes = legacyDocument.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
				var legacyChild = Assert.Single(legacyNodes, element =>
					element.TryGetProperty("automationId", out var id) && id.GetString() == "nested");
				Assert.Equal(120, legacyChild.GetProperty("frame").GetProperty("width").GetDouble());
				Assert.Equal("parent", legacyChild.GetProperty("frameCoordinateSpace").GetString());
				Assert.Equal("layoutAllocation", legacyChild.GetProperty("frameKind").GetString());
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void InspectionRoute_WithoutNativeProvider_ReportsNativeMetricsUnavailable()
		{
			CometDevRegistry.NativeWindowMetricsProvider = null;
			var agent = new CometDevAgent(9223, action => action());

			var json = agent.RouteForTest("GET", "/api/v1/ui/inspection", "");

			using var document = JsonDocument.Parse(json);
			var native = document.RootElement.GetProperty("nativeWindow");
			Assert.False(native.GetProperty("available").GetBoolean());
			Assert.Contains("not registered", native.GetProperty("reason").GetString());
		}

		[Fact]
		public void ElementAndPropertyRoutes_ReturnTheCanonicalFrameContract()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;

			try
			{
				var view = new Grid { AccessibilityId = "measured" };
				view.Frame = new Rect(7, 11, 101, 43);
				CometDevRegistry.Register(view, null!, null);
				var id = Assert.Single(CometDevRegistry.Snapshot()).Id;
				var agent = new CometDevAgent(9223, action => action());

				using var elementDocument = JsonDocument.Parse(
					agent.RouteForTest("GET", $"/api/v1/ui/elements/{id}", ""));
				Assert.Equal("measured", elementDocument.RootElement.GetProperty("automationId").GetString());
				Assert.Equal(7, elementDocument.RootElement.GetProperty("bounds").GetProperty("x").GetDouble());
				Assert.False(elementDocument.RootElement.TryGetProperty("windowBounds", out _));

				using var propertyDocument = JsonDocument.Parse(
					agent.RouteForTest("GET", $"/api/v1/ui/elements/{id}/properties/Frame", ""));
				var frameValue = propertyDocument.RootElement.GetProperty("value").GetString();
				using var frameDocument = JsonDocument.Parse(frameValue!);
				Assert.Equal(11, frameDocument.RootElement.GetProperty("y").GetDouble());
				Assert.Equal(101, frameDocument.RootElement.GetProperty("width").GetDouble());

				using var semanticsDocument = JsonDocument.Parse(
					agent.RouteForTest("GET", $"/api/v1/ui/elements/{id}/properties/FrameCoordinateSpace", ""));
				Assert.Equal("parent", semanticsDocument.RootElement.GetProperty("value").GetString());
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void InspectionGetRoutes_MissingElementsAndProperties_ReturnNotFound()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var view = new Grid();
				CometDevRegistry.Register(view, null!, null);
				var id = Assert.Single(CometDevRegistry.Snapshot()).Id;
				var agent = new CometDevAgent(9223, action => action());
				var paths = new[]
				{
					"/api/v1/ui/elements/2147483647",
					"/api/v1/ui/elements/2147483647/properties/Frame",
					$"/api/v1/ui/elements/{id}/properties/MissingProperty",
				};
				foreach (var path in paths)
				{
					var response = agent.RouteResponse("GET", path, "");
					Assert.Equal(404, response.StatusCode);
					using var json = JsonDocument.Parse(response.Json);
					Assert.False(json.RootElement.GetProperty("success").GetBoolean());
				}
				Assert.Equal(200, agent.RouteResponse("GET",
					$"/api/v1/ui/elements/{id}/properties/Frame", "").StatusCode);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void InspectionGetRoutes_UnregisteredParent_ReturnNullParent()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var parent = new Grid();
				var child = new Text("Child");
				CometDevRegistry.Register(parent, null!, null);
				CometDevRegistry.Register(child, null!, parent);
				var id = CometDevRegistry.Snapshot().Single(n => n.Type == "Text").Id;
				CometDevRegistry.Unregister(parent);
				var agent = new CometDevAgent(9223, action => action());
				using var element = JsonDocument.Parse(
					agent.RouteForTest("GET", $"/api/v1/ui/elements/{id}", ""));
				Assert.Equal(JsonValueKind.Null, element.RootElement.GetProperty("parentId").ValueKind);
				var response = agent.RouteResponse("GET",
					$"/api/v1/ui/elements/{id}/properties/ParentId", "");
				Assert.Equal(200, response.StatusCode);
				using var property = JsonDocument.Parse(response.Json);
				Assert.True(property.RootElement.GetProperty("success").GetBoolean());
				Assert.Equal(JsonValueKind.Null, property.RootElement.GetProperty("value").ValueKind);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void NativeAlertActions_QueryTapCloseReopenAndRejectStaleIds()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var isOpen = new Comet.Reactive.Signal<bool>(true);
				var discardCount = 0;
				var keepEditingCount = 0;
				var dialog = new AlertDialog(
					isOpen,
					new Text("Your range changes have not been saved."),
					new Button("DISCARD", () => discardCount++)
						.AutomationId("RangeDiscardConfirm"),
					new Text("Discard changes?"),
					new Button("KEEP EDITING", () => keepEditingCount++)
						.AutomationId("RangeDiscardKeepEditing"));
				CometDevRegistry.Register(dialog, null!, null);
				var dispatcher = new AlertDialogActionDispatcher(dialog);
				using var actions = new NativeAlertSemanticActions(
					dialog,
					dispatcher,
					() => isOpen.Value = false);
				actions.UpdateOpenState(true);
				var agent = new CometDevAgent(9223, action => action());

				var confirmId = QuerySingleId(agent, "automationId=RangeDiscardConfirm");
				var keepEditingId = QuerySingleId(agent, "text=KEEP%20EDITING");
				Assert.NotEqual(confirmId, keepEditingId);

				using (var tree = JsonDocument.Parse(
					agent.RouteForTest("GET", "/api/v1/ui/tree", "")))
				{
					var dialogJson = Assert.Single(tree.RootElement.EnumerateArray());
					var proxies = dialogJson.GetProperty("children").EnumerateArray().ToArray();
					Assert.Equal(2, proxies.Length);
					var confirm = Assert.Single(proxies, action =>
						action.GetProperty("automationId").GetString() == "RangeDiscardConfirm");
					Assert.Equal("DISCARD", confirm.GetProperty("text").GetString());
					Assert.Equal("button", confirm.GetProperty("role").GetString());
					Assert.False(confirm.TryGetProperty("bounds", out _));
					Assert.Equal(
						"semanticActionProxy",
						confirm.GetProperty("frameworkProperties").GetProperty("boundsKind").GetString());
				}
				using (var boundsKind = JsonDocument.Parse(agent.RouteForTest(
					"GET",
					$"/api/v1/ui/elements/{confirmId}/properties/BoundsKind",
					"")))
				{
					Assert.Equal(
						"semanticActionProxy",
						boundsKind.RootElement.GetProperty("value").GetString());
				}
				Assert.Equal(
					404,
					agent.RouteResponse(
						"GET",
						$"/api/v1/ui/elements/{confirmId}/properties/Bounds",
						"").StatusCode);

				var tap = agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{confirmId}\"}}");
				Assert.Equal(200, tap.StatusCode);
				Assert.Equal(1, discardCount);
				Assert.False(isOpen.Value);
				Assert.Empty(QueryElements(agent, "automationId=RangeDiscardConfirm"));

				var staleTap = agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{confirmId}\"}}");
				Assert.Equal(200, staleTap.StatusCode);
				using (var staleJson = JsonDocument.Parse(staleTap.Json))
					Assert.False(staleJson.RootElement.GetProperty("ok").GetBoolean());
				Assert.Equal(1, discardCount);

				isOpen.Value = true;
				actions.UpdateOpenState(true);
				var reopenedKeepEditingId = QuerySingleId(
					agent, "automationId=RangeDiscardKeepEditing");
				Assert.NotEqual(keepEditingId, reopenedKeepEditingId);

				tap = agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{reopenedKeepEditingId}\"}}");
				Assert.Equal(200, tap.StatusCode);
				Assert.Equal(1, keepEditingCount);
				Assert.False(isOpen.Value);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void NativeAlertAction_ReopeningInCallback_DoesNotDismissNewPresentation()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var isOpen = new Comet.Reactive.Signal<bool>(true);
				var calls = 0;
				NativeAlertSemanticActions? current = null;
				var dialog = new AlertDialog(isOpen, new Text("Message"),
					new Button("NEXT", () =>
					{
						calls++;
						isOpen.Value = false;
						current!.UpdateOpenState(false);
						isOpen.Value = true;
						current.UpdateOpenState(true);
					}).AutomationId("NextPresentation"));
				CometDevRegistry.Register(dialog, null!, null);
				using var actions = new NativeAlertSemanticActions(
					dialog, new AlertDialogActionDispatcher(dialog), () => isOpen.Value = false);
				current = actions;
				actions.UpdateOpenState(true);
				var agent = new CometDevAgent(9223, action => action());
				var first = QuerySingleId(agent, "automationId=NextPresentation");

				agent.RouteResponse("POST", "/api/v1/ui/actions/tap", $"{{\"elementId\":\"{first}\"}}");

				Assert.True(isOpen.Value);
				Assert.Equal(1, calls);
				Assert.NotEqual(first, QuerySingleId(agent, "automationId=NextPresentation"));
				agent.RouteResponse("POST", "/api/v1/ui/actions/tap", $"{{\"elementId\":\"{first}\"}}");
				Assert.Equal(1, calls);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void NativeAlertActions_OwnerReplacementInvalidatesOldIdsWithoutDuplicates()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var firstCount = 0;
				var replacementCount = 0;
				var first = CreateDiscardDialog(() => firstCount++);
				CometDevRegistry.Register(first, null!, null);
				var dispatcher = new AlertDialogActionDispatcher(first);
				using var actions = new NativeAlertSemanticActions(first, dispatcher, () => { });
				actions.UpdateOpenState(true);
				var agent = new CometDevAgent(9223, action => action());
				var staleId = QuerySingleId(agent, "automationId=RangeDiscardConfirm");

				var replacement = CreateDiscardDialog(() => replacementCount++);
				CometDevRegistry.TransferIdentity(first, replacement);
				actions.UpdateOwner(replacement);

				var proxies = QueryElements(agent, "type=AlertDialogAction");
				Assert.Equal(2, proxies.Length);
				var currentId = QuerySingleId(agent, "automationId=RangeDiscardConfirm");
				Assert.NotEqual(staleId, currentId);
				var staleTap = agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{staleId}\"}}");
				Assert.Equal(200, staleTap.StatusCode);
				using (var staleJson = JsonDocument.Parse(staleTap.Json))
					Assert.False(staleJson.RootElement.GetProperty("ok").GetBoolean());

				Assert.Equal(200, agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{currentId}\"}}").StatusCode);
				Assert.Equal(0, firstCount);
				Assert.Equal(1, replacementCount);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void NativeAlertActions_ReactiveChildCleanupPreservesActionsAndReopenCreatesFreshIds()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var dialog = CreateDiscardDialog(() => { });
				CometDevRegistry.Register(dialog, null!, null);
				var dispatcher = new AlertDialogActionDispatcher(dialog);
				using var actions = new NativeAlertSemanticActions(dialog, dispatcher, () => { });
				actions.UpdateOpenState(true);
				var agent = new CometDevAgent(9223, action => action());
				var firstId = QuerySingleId(agent, "automationId=RangeDiscardKeepEditing");

				// Reactive generation cleanup removes materialized descendants only. Native
				// alert actions belong to the retained dialog presentation and must survive.
				CometDevRegistry.UnregisterSubtree(dialog, includeRoot: false);
				Assert.Equal(2, QueryElements(agent, "type=AlertDialogAction").Length);
				Assert.Equal(
					firstId,
					QuerySingleId(agent, "automationId=RangeDiscardKeepEditing"));

				actions.UpdateOpenState(false);
				Assert.Empty(QueryElements(agent, "type=AlertDialogAction"));
				actions.UpdateOpenState(true);

				var reopened = QueryElements(agent, "type=AlertDialogAction");
				Assert.Equal(2, reopened.Length);
				var reopenedId = QuerySingleId(
					agent, "automationId=RangeDiscardKeepEditing");
				Assert.NotEqual(firstId, reopenedId);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void AlertDispatcher_ExposesDistinctTitleAndMessage()
		{
			var dialog = new AlertDialog(
				new Comet.Reactive.Signal<bool>(true),
				new Text("Your range changes have not been saved."),
				new Button("Discard", () => { }),
				new Text("Discard changes?"),
				new Button("Keep Editing", () => { }));
			var dispatcher = new AlertDialogActionDispatcher(dialog);

			Assert.Equal("Discard changes?", dispatcher.Title);
			Assert.Equal("Your range changes have not been saved.", dispatcher.Message);
		}

		[Fact]
		public void AlertDispatcher_NativeDismissalBeforeActionDoesNotDropAction()
		{
			var calls = 0;
			var dialog = CreateDiscardDialog(() => calls++);
			var dispatcher = new AlertDialogActionDispatcher(dialog);

			dispatcher.UpdateOpenState(false);
			dispatcher.InvokeConfirm();
			dispatcher.InvokeConfirm();

			Assert.Equal(1, calls);
		}

		[Fact]
		public void NativeAlert_ConfirmDismissesBeforeAction_SoNavigationDoesNotOrphanAlert()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			// Install a queued dispatcher that captures the deferred action
			Action? deferredAction = null;
			CometDevRegistry.MainThreadEnqueue = a => deferredAction = a;
			try
			{
				bool alertDismissedBeforeAction = false;
				bool actionRan = false;
				var isOpen = new Comet.Reactive.Signal<bool>(true);
				var dialog = new AlertDialog(
					isOpen,
					new Text("Discard changes?"),
					new Button("DISCARD", () =>
					{
						alertDismissedBeforeAction = !isOpen.Value;
						actionRan = true;
					}).AutomationId("ConfirmDiscard"),
					new Text("Unsaved changes"),
					new Button("KEEP EDITING", () => { }).AutomationId("KeepEditing"));
				CometDevRegistry.Register(dialog, null!, null);
				var dispatcher = new AlertDialogActionDispatcher(dialog);
				using var actions = new NativeAlertSemanticActions(
					dialog, dispatcher, () => isOpen.Value = false);
				actions.UpdateOpenState(true);

				var agent = new CometDevAgent(9223, action => action());
				var confirmId = QuerySingleId(agent, "automationId=ConfirmDiscard");

				// Tap the confirm button via semantic action
				var result = agent.RouteResponse(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{confirmId}\"}}");
				Assert.Equal(200, result.StatusCode);

				// After the tap returns: alert is dismissed but action has NOT run yet
				// (it was deferred to the next main-queue turn)
				Assert.False(actionRan, "action must NOT run synchronously — it is deferred");
				Assert.False(isOpen.Value, "alert must be closed immediately on the current turn");
				Assert.NotNull(deferredAction);

				// Simulate the next main-queue turn: run the deferred action
				deferredAction!();
				Assert.True(actionRan, "deferred action must have run");
				Assert.True(alertDismissedBeforeAction,
					"isOpen must have been false before the action — " +
					"SwiftUI had a full run-loop boundary to process the dismiss");
			}
			finally
			{
				CometDevRegistry.MainThreadEnqueue = null;
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		static AlertDialog CreateDiscardDialog(System.Action confirm) =>
			new(
				new Comet.Reactive.Signal<bool>(true),
				new Text("Your range changes have not been saved."),
				new Button("DISCARD", confirm).AutomationId("RangeDiscardConfirm"),
				new Text("Discard changes?"),
				new Button("KEEP EDITING", () => { })
					.AutomationId("RangeDiscardKeepEditing"));

		static JsonElement[] QueryElements(CometDevAgent agent, string query)
		{
			using var document = JsonDocument.Parse(
				agent.RouteForTest("GET", $"/api/v1/ui/elements?{query}", ""));
			return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
		}

		static int QuerySingleId(CometDevAgent agent, string query)
		{
			var element = Assert.Single(QueryElements(agent, query));
			return int.Parse(element.GetProperty("id").GetString()!);
		}

		[Fact]
		public void Fill_SetsTextOnTextField()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var signal = new Comet.Reactive.Signal<string>("original");
				var changedValue = "";
				var field = new TextField(signal, "placeholder");
				field.OnTextChanged(s => changedValue = s);
				field.AutomationId("TestField");
				CometDevRegistry.Register(field, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=TestField");
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/fill",
					$"{{\"elementId\":\"{id}\",\"text\":\"15.5\"}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Equal("15.5", signal.Value);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Clear_EmptiesTextField()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var signal = new Comet.Reactive.Signal<string>("existing");
				var field = new TextField(signal, "placeholder");
				field.AutomationId("ClearField");
				CometDevRegistry.Register(field, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=ClearField");
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/clear",
					$"{{\"elementId\":\"{id}\"}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Equal("", signal.Value);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Tap_OnTextField_DrivesFocusNotClick()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var signal = new Comet.Reactive.Signal<string>("");
				var recorder = new RecordingNode();
				var field = new TextField(signal, "placeholder");
				field.AutomationId("TapFocusField");
				field.Node = recorder;
				CometDevRegistry.Register(field, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=TapFocusField");
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/tap",
					$"{{\"elementId\":\"{id}\"}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Equal("", signal.Value);
				// The recording node must have received TextField_FocusRequested=true
				Assert.Contains(recorder.Applied,
					p => p.Id == PropertyIds.TextField_FocusRequested && p.Value);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Focus_OnTextField_PushesFocusRequestedProperty()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var signal = new Comet.Reactive.Signal<string>("");
				var recorder = new RecordingNode();
				var field = new TextField(signal, "placeholder");
				field.AutomationId("FocusRecField");
				field.Node = recorder;
				CometDevRegistry.Register(field, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=FocusRecField");
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/focus",
					$"{{\"elementId\":\"{id}\"}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Contains(recorder.Applied,
					p => p.Id == PropertyIds.TextField_FocusRequested && p.Value);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Scroll_WithoutInjector_ReturnsUnsupported()
		{
			// On non-iOS without a ScrollInjector, scroll must return success:false
			var prev = CometDevRegistry.ScrollInjector;
			CometDevRegistry.ScrollInjector = null;
			try
			{
				var agent = new CometDevAgent(9223, action => action());
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/scroll",
					"{\"elementId\":\"999\",\"dy\":-300}");

				using var json = JsonDocument.Parse(result);
				Assert.False(json.RootElement.GetProperty("success").GetBoolean());
			}
			finally
			{
				CometDevRegistry.ScrollInjector = prev;
			}
		}

		[Fact]
		public void Scroll_WithInjector_NegativeCliDyBecomesPositiveNative()
		{
			double capturedNativeDy = 0;
			double capturedNativeDx = 0;
			CometDevRegistry.ScrollInjector = (id, dx, dy) =>
			{
				capturedNativeDx = dx;
				capturedNativeDy = dy;
				return true;
			};
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var sv = new Comet.ScrollView();
				sv.AutomationId("ScrollTarget");
				CometDevRegistry.Register(sv, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=ScrollTarget");
				// AgentClient.ScrollAsync sends canonical "deltaY"/"deltaX"
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/scroll",
					$"{{\"elementId\":\"{id}\",\"deltaY\":-400,\"deltaX\":50}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				// Vertical: injector must receive +400 (native positive = down)
				Assert.Equal(400, capturedNativeDy);
				// Horizontal: injector must receive +50 unchanged (no inversion)
				Assert.Equal(50, capturedNativeDx);
			}
			finally
			{
				CometDevRegistry.ScrollInjector = null;
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Scroll_ShortAliases_WorkIdentically()
		{
			double capturedDy = 0;
			CometDevRegistry.ScrollInjector = (id, dx, dy) =>
			{
				capturedDy = dy;
				return true;
			};
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var sv = new Comet.ScrollView();
				sv.AutomationId("ScrollAlias");
				CometDevRegistry.Register(sv, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var id = QuerySingleId(agent, "automationId=ScrollAlias");
				// Short aliases "dy"/"dx" (curl / direct calls)
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/scroll",
					$"{{\"elementId\":\"{id}\",\"dy\":-200}}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Equal(200, capturedDy);
			}
			finally
			{
				CometDevRegistry.ScrollInjector = null;
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Back_RoutesRequestBack_NotPop()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				bool backCommandCalled = false;
				var page = new Text("Editor page");
				page.BackButtonBehavior(new BackButtonBehavior
				{
					IsVisible = false,
					Command = new ActionCommand(() => backCommandCalled = true),
				});

				var nav = new NavigationView();
				CometDevRegistry.Register(nav, null!, null);
				CometDevRegistry.Register(page, null!, null);
				nav.Navigate(page);

				var agent = new CometDevAgent(9223, action => action());
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/back",
					"{}");

				using var json = JsonDocument.Parse(result);
				Assert.True(json.RootElement.GetProperty("success").GetBoolean());
				// RequestBack should have honoured BackButtonBehavior.Command
				Assert.True(backCommandCalled);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Back_WithoutNavigationView_ReturnsFalse()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				// Register a plain view, not a NavigationView
				var page = new Text("Orphan");
				CometDevRegistry.Register(page, null!, null);

				var agent = new CometDevAgent(9223, action => action());
				var result = agent.RouteForTest(
					"POST",
					"/api/v1/ui/actions/back",
					"{}");

				using var json = JsonDocument.Parse(result);
				Assert.False(json.RootElement.GetProperty("success").GetBoolean());
				Assert.Contains("no NavigationView", json.RootElement.GetProperty("error").GetString());
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void Keyboard_Numeric_EmitsCorrectPropertyCode()
		{
			var signal = new Comet.Reactive.Signal<string>("");
			var recorder = new KeyboardRecordingNode();
			var field = new TextField(signal, "enter value");
			field.Keyboard(Microsoft.Maui.Keyboard.Numeric);
			field.Node = recorder;

			// ApplyAllSetProperties pushes all environment-backed properties to the node
			field.ApplyAllSetProperties(recorder);

			// Numeric keyboard must emit PropertyIds.TextField_Keyboard with code 1
			Assert.Contains(recorder.IntProperties,
				p => p.Id == PropertyIds.TextField_Keyboard && p.Value == 1);
		}

		[Theory]
		[InlineData(0)] // Default
		[InlineData(2)] // Email
		[InlineData(3)] // Url
		[InlineData(4)] // Telephone
		[InlineData(6)] // Plain
		public void Keyboard_AllTypes_EmitCorrectCodes(int expectedCode)
		{
			var keyboards = new (Microsoft.Maui.Keyboard kb, int code)[]
			{
				(Microsoft.Maui.Keyboard.Default, 0),
				(Microsoft.Maui.Keyboard.Email, 2),
				(Microsoft.Maui.Keyboard.Url, 3),
				(Microsoft.Maui.Keyboard.Telephone, 4),
				(Microsoft.Maui.Keyboard.Plain, 6),
			};
			var (keyboard, code) = keyboards.First(k => k.code == expectedCode);

			var signal = new Comet.Reactive.Signal<string>("");
			var recorder = new KeyboardRecordingNode();
			var field = new TextField(signal, "test");
			field.Keyboard(keyboard);
			field.Node = recorder;
			field.ApplyAllSetProperties(recorder);

			if (expectedCode == 0)
			{
				// Default keyboard should not emit the property (it's the default)
				Assert.DoesNotContain(recorder.IntProperties,
					p => p.Id == PropertyIds.TextField_Keyboard);
			}
			else
			{
				Assert.Contains(recorder.IntProperties,
					p => p.Id == PropertyIds.TextField_Keyboard && p.Value == expectedCode);
			}
		}

		/// <summary>Records both bool and int ApplyProperty calls.</summary>
		sealed class KeyboardRecordingNode : ICometBackendNode
		{
			public List<(PropertyId Id, int Value)> IntProperties { get; } = new();
			public void ApplyProperty(PropertyId id, in PropertyValue value)
				=> IntProperties.Add((id, value.AsInt));
			public void InsertChild(int index, ICometBackendNode child) { }
			public void RemoveChildAt(int index) { }
			public void MoveChild(int fromIndex, int toIndex) { }
			public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
			public void Arrange(Rect frame) { }
			public void SetEventSink(ICometEventSink? sink) { }
			public void Dispose() { }
		}

		/// <summary>
		/// A minimal backend node that records ApplyProperty calls for assertion.
		/// </summary>
		sealed class RecordingNode : ICometBackendNode
		{
			public List<(PropertyId Id, bool Value)> Applied { get; } = new();
			public void ApplyProperty(PropertyId id, in PropertyValue value)
				=> Applied.Add((id, value.AsBool));
			public void InsertChild(int index, ICometBackendNode child) { }
			public void RemoveChildAt(int index) { }
			public void MoveChild(int fromIndex, int toIndex) { }
			public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
			public void Arrange(Rect frame) { }
			public void SetEventSink(ICometEventSink? sink) { }
			public void Dispose() { }
		}

		sealed class ActionCommand : ICommand
		{
			readonly Action _execute;
			public ActionCommand(Action execute) => _execute = execute;
			public event EventHandler? CanExecuteChanged { add { } remove { } }
			public bool CanExecute(object? parameter) => true;
			public void Execute(object? parameter) => _execute();
		}
	}
}
