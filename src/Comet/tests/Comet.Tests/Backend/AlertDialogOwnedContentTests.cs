#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comet;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend
{
	public class AlertDialogOwnedContentTests
	{
		static AlertDialogOwnedContentTests()
			=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		sealed class OwnContentNode : FakeBackendNode, IBackendManagesOwnContent
		{
			public OwnContentNode() : base("dialog") { }

			public int DisposeCount { get; private set; }

			public override void Dispose()
			{
				DisposeCount++;
				base.Dispose();
			}
		}

		sealed class TrackingSlotNode : FakeBackendNode
		{
			public TrackingSlotNode(View owner) : base(owner.GetType().Name)
				=> Owner = owner;

			public View Owner { get; }
		}

		sealed class RetainedAlertDialogNode : FakeBackendNode, IBackendManagesOwnContent, ICometBackendNode
		{
			AlertDialog _dialog;
			readonly CometNodeFactory _factory;
			OwnedContentGeneration? _generation;

			public RetainedAlertDialogNode(
				AlertDialog dialog,
				CometNodeFactory factory)
				: base("dialog")
			{
				_dialog = dialog;
				_factory = factory;
				RefreshFlattenedContent();
			}

			public bool IsOpen { get; private set; }
			public string Message { get; private set; } = string.Empty;
			public string ConfirmLabel { get; private set; } = string.Empty;
			public int MaterializedGenerationCount { get; private set; }
			public int ReleasedGenerationCount { get; private set; }
			public IReadOnlyList<TrackingSlotNode> CurrentSlotNodes { get; private set; }
				= Array.Empty<TrackingSlotNode>();

			public override void ApplyProperty(PropertyId id, in PropertyValue value)
			{
				base.ApplyProperty(id, in value);
				if (id != PropertyIds.Dialog_IsOpen)
					return;

				IsOpen = value.AsBool;
				if (IsOpen)
					EnsureContent();
				else
					ReleaseContent();
			}

			void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
			{
				if (newView is not AlertDialog dialog)
					return;

				ReleaseContent();
				_dialog = dialog;
				RefreshFlattenedContent();
				if (IsOpen)
					EnsureContent();
			}

			void RefreshFlattenedContent()
			{
				Message = (_dialog.Text as Text)?.Value?.CurrentValue ?? string.Empty;
				ConfirmLabel = (_dialog.ConfirmButton as Button)?.Text?.CurrentValue ?? "OK";
			}

			void EnsureContent()
			{
				if (_generation is not null)
					return;

				var generation = new OwnedContentGeneration(_dialog, _factory, Ctx);
				var nodes = _dialog.GetChildren()
					.Select(slot => (TrackingSlotNode)generation.Materialize(slot))
					.ToArray();
				_generation = generation;
				CurrentSlotNodes = nodes;
				MaterializedGenerationCount++;
			}

			void ReleaseContent()
			{
				if (_generation is null)
					return;

				_generation.Dispose();
				_generation = null;
				CurrentSlotNodes = Array.Empty<TrackingSlotNode>();
				ReleasedGenerationCount++;
			}

			public override void Dispose()
			{
				ReleaseContent();
				base.Dispose();
			}
		}

		sealed class TrackingAlertDialog : AlertDialog
		{
			public TrackingAlertDialog(Signal<bool> isOpen)
				: base(
					isOpen,
					new Text("Archive equipment?"),
					new Button("ARCHIVE", () => { }))
			{
			}

			public int DisposeCount { get; private set; }

			protected override void Dispose(bool disposing)
			{
				if (disposing)
					DisposeCount++;
				base.Dispose(disposing);
			}
		}

		sealed class TrackingSlotView : View
		{
			readonly Signal<int> _value;

			public TrackingSlotView(Signal<int> value)
				=> _value = value;

			public int DisposeCount { get; private set; }

			[Body]
			View Body() => new Text(_value.Value.ToString());

			protected override void Dispose(bool disposing)
			{
				if (disposing)
					DisposeCount++;
				base.Dispose(disposing);
			}
		}

		sealed class ReactiveDialogScreen : View
		{
			public Signal<int> UnrelatedValue { get; } = new(0);
			public Signal<bool> IsOpen { get; } = new(false);
			public List<TrackingAlertDialog> Dialogs { get; } = new();

			[Body]
			View Body()
			{
				var value = UnrelatedValue.Value;
				var dialog = new TrackingAlertDialog(IsOpen);
				Dialogs.Add(dialog);
				return new VStack
				{
					new Text($"Unrelated: {value}"),
					dialog,
				};
			}
		}

		static AlertDialog CreateDialog() => new AlertDialog(
			new Signal<bool>(false),
			new Text("Archive equipment?").AutomationId("dialog_message"),
			new Button("ARCHIVE", () => { }).AutomationId("dialog_confirm"),
			new Text("Archive").AutomationId("dialog_title"),
			new Button("CANCEL", () => { }).AutomationId("dialog_cancel"))
			.AutomationId("archive_dialog");

		static AlertDialog CreateDialog(
			Signal<bool> isOpen,
			string message,
			string confirmLabel,
			Action? confirm = null,
			string? dismissLabel = null,
			Action? dismiss = null) => new AlertDialog(
				isOpen,
				new Text(message).AutomationId("dialog_message"),
				new Button(confirmLabel, confirm ?? (() => { })).AutomationId("dialog_confirm"),
				dismissButton: dismissLabel is null
					? null
					: new Button(dismissLabel, dismiss ?? (() => { })).AutomationId("dialog_cancel"))
			.AutomationId("archive_dialog");

		[Fact]
		public void DialogSlots_CloseAndReopen_AreOwnedAndGenerationCleaned()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var dialog = CreateDialog();
				ICometBackendNode Factory(View view) => view is AlertDialog
					? new OwnContentNode()
					: new FakeBackendNode(view.GetType().Name);
				CometBackendBridge.Materialize(dialog, Factory, Ctx);

				var first = MaterializeSlots(dialog, Factory);
				AssertSlotsHaveDialogParent(dialog);
				var firstNodes = dialog.GetChildren().Select(view => view.Node).ToArray();
				Assert.All(firstNodes, Assert.NotNull);

				// Close: the whole slot generation disappears, nodes are disposed, and
				// persistent logical slot views no longer point at inactive backend nodes.
				first.Dispose();
				var closed = CometDevRegistry.Snapshot();
				Assert.Single(closed);
				Assert.Equal("archive_dialog", closed[0].AutomationId);
				Assert.All(firstNodes, node => Assert.True(((FakeBackendNode)node!).Disposed));
				Assert.All(dialog.GetChildren(), view => Assert.Null(view.Node));

				// Reopen: the same logical slots rematerialize as one fresh generation,
				// all parented under the still-live dialog root.
				using var reopened = MaterializeSlots(dialog, Factory);
				AssertSlotsHaveDialogParent(dialog);
				var reopenedNodes = dialog.GetChildren().Select(view => view.Node).ToArray();
				Assert.All(reopenedNodes, Assert.NotNull);
				for (int i = 0; i < firstNodes.Length; i++)
					Assert.NotSame(firstNodes[i], reopenedNodes[i]);
				Assert.Equal(5, CometDevRegistry.Snapshot().Count);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void PlatformAlertDialogs_ObserveOwnerReplacementAndNativeContentUpdates()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var compose = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeAlertDialogNode.cs"));

			Assert.Contains("new OwnedContentGeneration(_dialog, _context)", compose);
			Assert.Contains("if (!value.AsBool)", compose);
			Assert.Contains("ReleaseContent();", compose);
			Assert.DoesNotContain("CometDevRegistry.UnregisterSubtree", compose);
			Assert.DoesNotContain("CometBackendBridge.ClearMaterializedNodes", compose);
			Assert.Contains("readonly AndroidX.Compose.MutableState<int> _contentVersion", compose);
			Assert.Contains("_contentVersion.Value++;", compose);
			Assert.Contains("_ = _contentVersion.Value;", compose);
			Assert.Contains("if (!_open.Value)", compose);
			Assert.DoesNotContain("Materialize(_dialog.Text, _context);", compose);

			var swift = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIAlertDialogNode.cs"));
			Assert.Contains("void UpdateContent(AlertDialog dialog)", swift);
			Assert.Contains("public void OnOwnerViewChanged(View newView, bool isHotReload)", swift);
			Assert.Equal(2, CountOccurrences(swift, "UpdateContent(dialog);"));
			Assert.Contains("_semanticActions.UpdateOwner(dialog);", swift);
			Assert.Contains("SetString(_native, \"dialogmessage\", _actions.Message)", swift);
			Assert.Contains("SetString(_native, \"dialogbutton\", _actions.ConfirmLabel)", swift);
			Assert.Contains("SetString(_native, \"dialogdismissbutton\", _actions.DismissLabel ?? string.Empty)", swift);
			Assert.Contains("SetBool(_native, \"dialoghasdismissbutton\", _actions.DismissLabel is not null)", swift);
			Assert.Contains("SetDialogConfirmHandler(_native, _actions.InvokeConfirm)", swift);
			Assert.Contains("SetDialogDismissActionHandler(_native, _actions.InvokeDismiss)", swift);
			Assert.Contains("_semanticActions.UpdateOpenState(value.AsBool);", swift);
			Assert.Contains("_semanticActions.OnNativePresentationDismissed", swift);
			Assert.Contains("public void Dispose() => _semanticActions.Dispose();", swift);
			Assert.Contains("SetBool(_native, \"dialogopen\", value.AsBool)", swift);

			var shim = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet.SwiftUI.Shim", "Sources", "CometSwiftUIShim", "CometSwiftUIShim.swift"));
			Assert.Contains("@Published var dialogOpen: Bool", shim);
			Assert.Contains("@Published var dialogMessage: String", shim);
			Assert.Contains("@Published var dialogButton: String", shim);
			Assert.Contains("@Published var dialogDismissButton: String", shim);
			Assert.Contains("@Published var dialogHasDismissButton: Bool", shim);
			Assert.Contains("case \"dialogmessage\": node.dialogMessage = value", shim);
			Assert.Contains("case \"dialogbutton\": node.dialogButton = value", shim);
			Assert.Contains("case \"dialogdismissbutton\": node.dialogDismissButton = value", shim);
			Assert.Contains("Button(node.dialogButton) {", shim);
			Assert.Contains("node.onDialogConfirm?()", shim);
			Assert.Contains("Button(node.dialogDismissButton, role: .cancel)", shim);
			Assert.Contains("node.onDialogDismissAction?()", shim);
			Assert.Equal(2, CountOccurrences(shim, "node.dismissDialog(presentation: presentation)"));
			Assert.Contains("guard presentation == dialogPresentation, dialogOpen", shim);
			Assert.Contains(".id(presentation)", shim);
			Assert.DoesNotContain("Button(node.dialogButton, role: .cancel)", shim);

			var binding = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet.SwiftUI.Binding", "ApiDefinition.cs"));
			Assert.Contains("setDialogConfirmHandler:handler:", binding);
			Assert.Contains("setDialogDismissActionHandler:handler:", binding);

			var backend = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Backend", "AlertDialog.Backend.cs"));
			Assert.Contains("IsOpen.PropertyChanged -= OnOpenChanged;", backend);
		}

		[Fact]
		public void ConfirmAction_InvokesSourceButtonOnceAndSystemDismissCloses()
		{
			var confirmCount = 0;
			var dismissCount = 0;
			var isOpen = new Signal<bool>(true);
			var dialog = CreateDialog(
				isOpen,
				"Delete the item?",
				"DELETE",
				() => confirmCount++,
				"CANCEL",
				() => dismissCount++);
			var actions = new AlertDialogActionDispatcher(dialog);

			actions.UpdateOpenState(true);
			actions.InvokeConfirm();
			actions.InvokeConfirm();
			dialog.OnBackendEvent(EventIds.DialogDismissed);

			Assert.Equal(1, confirmCount);
			Assert.Equal(0, dismissCount);
			Assert.False(isOpen.Value);
		}

		[Fact]
		public void DismissAction_InvokesSourceButtonOnceAndSystemDismissCloses()
		{
			var confirmCount = 0;
			var dismissCount = 0;
			var isOpen = new Signal<bool>(true);
			var dialog = CreateDialog(
				isOpen,
				"Delete the item?",
				"DELETE",
				() => confirmCount++,
				"CANCEL",
				() => dismissCount++);
			var actions = new AlertDialogActionDispatcher(dialog);

			actions.UpdateOpenState(true);
			actions.InvokeDismiss();
			actions.InvokeDismiss();
			dialog.OnBackendEvent(EventIds.DialogDismissed);

			Assert.Equal(0, confirmCount);
			Assert.Equal(1, dismissCount);
			Assert.False(isOpen.Value);
		}

		[Fact]
		public void SystemDismiss_DoesNotInvokeEitherSourceButton()
		{
			var confirmCount = 0;
			var dismissCount = 0;
			var isOpen = new Signal<bool>(true);
			var dialog = CreateDialog(
				isOpen,
				"Delete the item?",
				"DELETE",
				() => confirmCount++,
				"CANCEL",
				() => dismissCount++);

			dialog.OnBackendEvent(EventIds.DialogDismissed);

			Assert.Equal(0, confirmCount);
			Assert.Equal(0, dismissCount);
			Assert.False(isOpen.Value);
		}

		[Fact]
		public void OpenOwnerTransfer_UsesReplacementLabelsAndActions()
		{
			var oldConfirmCount = 0;
			var oldDismissCount = 0;
			var newConfirmCount = 0;
			var newDismissCount = 0;
			var oldDialog = CreateDialog(
				new Signal<bool>(true),
				"Old message",
				"OLD CONFIRM",
				() => oldConfirmCount++,
				"OLD DISMISS",
				() => oldDismissCount++);
			var replacement = CreateDialog(
				new Signal<bool>(true),
				"New message",
				"NEW CONFIRM",
				() => newConfirmCount++,
				"NEW DISMISS",
				() => newDismissCount++);
			var actions = new AlertDialogActionDispatcher(oldDialog);

			actions.UpdateOpenState(true);
			actions.UpdateOwner(replacement);

			Assert.Equal("New message", actions.Message);
			Assert.Equal("NEW CONFIRM", actions.ConfirmLabel);
			Assert.Equal("NEW DISMISS", actions.DismissLabel);

			actions.InvokeConfirm();
			actions.UpdateOpenState(false);
			actions.UpdateOpenState(true);
			actions.InvokeDismiss();

			Assert.Equal(0, oldConfirmCount);
			Assert.Equal(0, oldDismissCount);
			Assert.Equal(1, newConfirmCount);
			Assert.Equal(1, newDismissCount);
		}

		[Fact]
		public void ReopenedDialog_AllowsOneActionForTheNewPresentation()
		{
			var confirmCount = 0;
			var isOpen = new Signal<bool>(true);
			var dialog = CreateDialog(
				isOpen,
				"Archive the item?",
				"ARCHIVE",
				() => confirmCount++);
			var actions = new AlertDialogActionDispatcher(dialog);

			actions.UpdateOpenState(true);
			actions.InvokeConfirm();
			actions.InvokeConfirm();
			actions.UpdateOpenState(false);
			actions.UpdateOpenState(true);
			actions.InvokeConfirm();
			actions.InvokeConfirm();

			Assert.Equal(2, confirmCount);
		}

		[Fact]
		public void OpenDialog_OwnerTransfer_ReplacesOneOwnedGenerationAndCurrentContent()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var oldOpen = new Signal<bool>(true);
				var replacementOpen = new Signal<bool>(true);
				var oldDialog = CreateDialog(oldOpen, "Old message", "OLD");
				var replacement = CreateDialog(replacementOpen, "Replacement message", "REPLACE");
				RetainedAlertDialogNode? retained = null;
				var createdSlots = new List<TrackingSlotNode>();
				CometNodeFactory? factory = null;
				factory = view =>
				{
					if (view is AlertDialog dialog)
						return retained ??= new RetainedAlertDialogNode(dialog, factory!);

					var slot = new TrackingSlotNode(view);
					createdSlots.Add(slot);
					return slot;
				};

				CometBackendBridge.Materialize(oldDialog, factory, Ctx);
				var node = Assert.IsType<RetainedAlertDialogNode>(oldDialog.Node);
				var oldNodes = node.CurrentSlotNodes.ToArray();
				Assert.True(node.IsOpen);
				Assert.Equal("Old message", node.Message);
				Assert.Equal("OLD", node.ConfirmLabel);
				Assert.Equal(1, node.MaterializedGenerationCount);
				Assert.Equal(2, oldNodes.Length);

				replacement.TransferBackendNodeFrom(oldDialog, isHotReload: false);
				oldDialog.Dispose();

				Assert.Same(node, replacement.Node);
				Assert.True(node.IsOpen);
				Assert.Equal("Replacement message", node.Message);
				Assert.Equal("REPLACE", node.ConfirmLabel);
				Assert.Equal(2, node.MaterializedGenerationCount);
				Assert.Equal(1, node.ReleasedGenerationCount);
				Assert.Equal(4, createdSlots.Count);
				Assert.Equal(2, node.CurrentSlotNodes.Count);
				Assert.All(oldNodes, oldNode => Assert.True(oldNode.Disposed));
				Assert.All(node.CurrentSlotNodes, current => Assert.False(current.Disposed));
				Assert.DoesNotContain(node.CurrentSlotNodes, current => oldNodes.Contains(current));
				Assert.Equal(0, PropertyChangedSubscriberCount(oldOpen));
				Assert.Equal(1, PropertyChangedSubscriberCount(replacementOpen));

				var snapshot = CometDevRegistry.Snapshot();
				var owner = Assert.Single(snapshot, entry => entry.AutomationId == "archive_dialog");
				var slots = snapshot.Where(entry =>
					entry.AutomationId is "dialog_message" or "dialog_confirm").ToArray();
				Assert.Equal(2, slots.Length);
				Assert.All(slots, slot => Assert.Equal(owner.Id, slot.ParentId));

				replacement.Dispose();
				Assert.Equal(2, node.ReleasedGenerationCount);
				Assert.All(createdSlots, slot => Assert.True(slot.Disposed));
				Assert.Equal(0, PropertyChangedSubscriberCount(replacementOpen));
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void ClosedDialog_OwnerTransfer_RemainsHiddenAndUnsubscribesDisposedOwner()
		{
			var oldOpen = new Signal<bool>(false);
			var replacementOpen = new Signal<bool>(false);
			var oldDialog = CreateDialog(oldOpen, "Old message", "OLD");
			var replacement = CreateDialog(replacementOpen, "Replacement message", "REPLACE");
			RetainedAlertDialogNode? retained = null;
			CometNodeFactory? factory = null;
			factory = view => view is AlertDialog dialog
				? retained ??= new RetainedAlertDialogNode(dialog, factory!)
				: new TrackingSlotNode(view);

			CometBackendBridge.Materialize(oldDialog, factory, Ctx);
			var node = Assert.IsType<RetainedAlertDialogNode>(oldDialog.Node);
			Assert.False(node.IsOpen);
			Assert.Equal(0, node.MaterializedGenerationCount);

			replacement.TransferBackendNodeFrom(oldDialog, isHotReload: false);
			oldDialog.Dispose();

			Assert.Same(node, replacement.Node);
			Assert.False(node.IsOpen);
			Assert.Empty(node.CurrentSlotNodes);
			Assert.Equal(0, node.MaterializedGenerationCount);
			Assert.Equal(0, node.ReleasedGenerationCount);
			Assert.Equal("Replacement message", node.Message);
			Assert.Equal("REPLACE", node.ConfirmLabel);
			Assert.True(oldDialog.IsDisposed);
			Assert.Equal(0, PropertyChangedSubscriberCount(oldOpen));
			Assert.Equal(1, PropertyChangedSubscriberCount(replacementOpen));

			replacement.Dispose();
			Assert.Equal(0, PropertyChangedSubscriberCount(replacementOpen));
		}

		[Fact]
		public void ReactiveBodyReplacement_DisposesOutgoingDialogsAndKeepsOneSignalSubscriber()
		{
			var screen = new ReactiveDialogScreen();
			OwnContentNode? retainedDialogNode = null;

			ICometBackendNode Factory(View view)
			{
				if (view is AlertDialog)
					return retainedDialogNode ??= new OwnContentNode();
				return new FakeBackendNode(view.GetType().Name);
			}

			CometBackendBridge.Materialize(screen, Factory, Ctx);
			Assert.Single(screen.Dialogs);
			Assert.Equal(1, PropertyChangedSubscriberCount(screen.IsOpen));
			Assert.NotNull(retainedDialogNode);

			for (var value = 1; value <= 5; value++)
			{
				var outgoing = screen.Dialogs[^1];
				screen.UnrelatedValue.Value = value;
				ReactiveScheduler.FlushSync();

				Assert.Equal(1, outgoing.DisposeCount);
				Assert.Equal(value + 1, screen.Dialogs.Count);
				Assert.All(screen.Dialogs.Take(screen.Dialogs.Count - 1),
					dialog => Assert.Equal(1, dialog.DisposeCount));
				Assert.Equal(0, screen.Dialogs[^1].DisposeCount);
				Assert.Equal(1, PropertyChangedSubscriberCount(screen.IsOpen));
				Assert.Same(retainedDialogNode, screen.Dialogs[^1].Node);
				Assert.Equal(0, retainedDialogNode!.DisposeCount);
			}

			screen.IsOpen.Value = true;
			Assert.True(retainedDialogNode!.Get(PropertyIds.Dialog_IsOpen).AsBool);

			screen.Dispose();

			Assert.All(screen.Dialogs, dialog => Assert.Equal(1, dialog.DisposeCount));
			Assert.Equal(0, PropertyChangedSubscriberCount(screen.IsOpen));
			Assert.Equal(1, retainedDialogNode.DisposeCount);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void TerminalDispose_DisposesEveryOwnedLogicalSlotExactlyOnce(bool isOpen)
		{
			var open = new Signal<bool>(isOpen);
			var slotSignal = new Signal<int>(0);
			var text = new TrackingSlotView(slotSignal);
			var confirm = new TrackingSlotView(slotSignal);
			var title = new TrackingSlotView(slotSignal);
			var dismiss = new TrackingSlotView(slotSignal);
			var dialog = new AlertDialog(open, text, confirm, title, dismiss);
			ICometBackendNode Factory(View view) => view is AlertDialog
				? new OwnContentNode()
				: new FakeBackendNode(view.GetType().Name);

			CometBackendBridge.Materialize(dialog, Factory, Ctx);
			_ = text.GetView();
			_ = confirm.GetView();
			_ = title.GetView();
			_ = dismiss.GetView();
			Assert.All(new[] { text, confirm, title, dismiss },
				slot => Assert.True(slot.HasActiveBodySubscriptions));
			Assert.Equal(1, PropertyChangedSubscriberCount(open));

			dialog.Dispose();
			dialog.Dispose();

			Assert.True(dialog.IsDisposed);
			Assert.Equal(0, PropertyChangedSubscriberCount(open));
			Assert.All(new[] { text, confirm, title, dismiss }, slot =>
			{
				Assert.True(slot.IsDisposed);
				Assert.Equal(1, slot.DisposeCount);
				Assert.False(slot.HasActiveBodySubscriptions);
			});
		}

		[Fact]
		public void ReplacementOwnerTransfer_DoesNotDisposeSharedSlotsWithOutgoingDialog()
		{
			var oldOpen = new Signal<bool>(true);
			var replacementOpen = new Signal<bool>(true);
			var slotSignal = new Signal<int>(0);
			var sharedText = new TrackingSlotView(slotSignal);
			var sharedConfirm = new TrackingSlotView(slotSignal);
			var oldTitle = new TrackingSlotView(slotSignal);
			var replacementDismiss = new TrackingSlotView(slotSignal);
			var oldDialog = new AlertDialog(
				oldOpen,
				sharedText,
				sharedConfirm,
				title: oldTitle);
			var replacement = new AlertDialog(
				replacementOpen,
				sharedText,
				sharedConfirm,
				dismissButton: replacementDismiss);
			ICometBackendNode Factory(View view) => view is AlertDialog
				? new OwnContentNode()
				: new FakeBackendNode(view.GetType().Name);

			CometBackendBridge.Materialize(oldDialog, Factory, Ctx);
			replacement.TransferBackendNodeFrom(oldDialog, isHotReload: false);
			oldDialog.Dispose();

			Assert.True(oldDialog.IsDisposed);
			Assert.True(oldTitle.IsDisposed);
			Assert.Equal(1, oldTitle.DisposeCount);
			Assert.False(sharedText.IsDisposed);
			Assert.False(sharedConfirm.IsDisposed);
			Assert.Same(replacement, sharedText.Parent);
			Assert.Same(replacement, sharedConfirm.Parent);
			Assert.Equal(0, PropertyChangedSubscriberCount(oldOpen));
			Assert.Equal(1, PropertyChangedSubscriberCount(replacementOpen));

			replacement.Dispose();

			Assert.Equal(1, sharedText.DisposeCount);
			Assert.Equal(1, sharedConfirm.DisposeCount);
			Assert.Equal(1, replacementDismiss.DisposeCount);
			Assert.Equal(0, PropertyChangedSubscriberCount(replacementOpen));
		}

		[Fact]
		public void TerminalDispose_DuplicateSlotReference_IsDisposedOnce()
		{
			var open = new Signal<bool>(false);
			var slot = new TrackingSlotView(new Signal<int>(0));
			var dialog = new AlertDialog(open, slot, slot, title: slot, dismissButton: slot);

			dialog.Dispose();

			Assert.True(slot.IsDisposed);
			Assert.Equal(1, slot.DisposeCount);
		}

		static int PropertyChangedSubscriberCount<T>(Signal<T> signal)
		{
			var field = typeof(Signal<T>).GetField(
				nameof(Signal<T>.PropertyChanged),
				BindingFlags.Instance | BindingFlags.NonPublic);
			var handlers = field?.GetValue(signal) as Delegate;
			return handlers?.GetInvocationList().Length ?? 0;
		}

		static int CountOccurrences(string source, string value)
		{
			var count = 0;
			var start = 0;
			while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
			{
				count++;
				start += value.Length;
			}
			return count;
		}

		static OwnedContentGeneration MaterializeSlots(
			AlertDialog dialog,
			CometNodeFactory factory)
		{
			var generation = new OwnedContentGeneration(dialog, factory, Ctx);
			foreach (var slot in dialog.GetChildren())
				generation.Materialize(slot);
			return generation;
		}

		static void AssertSlotsHaveDialogParent(AlertDialog dialog)
		{
			var snapshot = CometDevRegistry.Snapshot();
			var owner = Assert.Single(snapshot, node => node.AutomationId == "archive_dialog");
			var slots = snapshot.Where(node => node.AutomationId?.StartsWith("dialog_") == true).ToArray();
			Assert.Equal(4, slots.Length);
			Assert.All(slots, slot => Assert.Equal(owner.Id, slot.ParentId));
			Assert.DoesNotContain(slots, slot => slot.ParentId == -1);
		}
	}
}
