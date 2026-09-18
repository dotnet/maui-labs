#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	public class BackendBaristaPrimitiveTests
	{
		static BackendBaristaPrimitiveTests()
			=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		static readonly BackendContext Context = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, view => new FakeBackendNode(view.GetType().Name), Context);

		static FakeBackendNode Node(View view) => (FakeBackendNode)view.Node!;

		[Fact]
		public void Grid_UsesFixedAutoStarSpansSpacing_AndRelayouts()
		{
			var fixedCell = new Text("fixed").Cell(row: 0, column: 0);
			var autoCell = new Text("auto").Cell(row: 0, column: 1);
			var starCell = new Text("star").Cell(row: 0, column: 2);
			var spanning = new Text("span").Cell(row: 1, column: 1, colSpan: 2);
			var grid = new Grid(
				columns: new object[] { 50, "Auto", "*" },
				rows: new object[] { "Auto", 40 },
				columnSpacing: 5,
				rowSpacing: 6)
			{
				fixedCell,
				autoCell,
				starCell,
				spanning,
			};
			Bridge(grid);
			Node(fixedCell).MeasureResult = new Size(20, 20);
			Node(autoCell).MeasureResult = new Size(30, 20);
			Node(starCell).MeasureResult = new Size(10, 20);
			Node(spanning).MeasureResult = new Size(100, 20);

			CometBackendLayoutEngine.Layout(grid, new Size(300, 100));

			Assert.Equal(50, Node(fixedCell).ArrangedFrame!.Value.Width, 3);
			Assert.Equal(30, Node(autoCell).ArrangedFrame!.Value.Width, 3);
			Assert.Equal(210, Node(starCell).ArrangedFrame!.Value.Width, 3);
			Assert.Equal(245, Node(spanning).ArrangedFrame!.Value.Width, 3);
			Assert.Equal(26, Node(spanning).ArrangedFrame!.Value.Y, 3);

			Node(autoCell).MeasureResult = new Size(60, 20);
			CometBackendLayoutEngine.Layout(grid, new Size(300, 100));

			Assert.Equal(60, Node(autoCell).ArrangedFrame!.Value.Width, 3);
			Assert.Equal(180, Node(starCell).ArrangedFrame!.Value.Width, 3);
		}

		[Fact]
		public void Grid_ArrangesRenderedBodyInsteadOfLogicalWrapper()
		{
			var component = new BodyTile().Cell(row: 0, column: 0);
			var grid = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { 80 })
			{
				component,
			};
			Bridge(grid);

			CometBackendLayoutEngine.Layout(grid, new Size(240, 80));

			var rendered = component.BuiltView;
			Assert.NotNull(rendered);
			var renderedConstraints = Assert.IsType<Comet.Layout.GridConstraints>(
				rendered.GetLayoutConstraints());
			Assert.Equal(0, renderedConstraints.Row);
			Assert.Equal(0, renderedConstraints.Column);
			Assert.Equal(new Rect(0, 0, 240, 80), Node(rendered).ArrangedFrame);
		}

		[Fact]
		public void Grid_ArrangesChildrenInsidePadding()
		{
			var child = new Text("cell");
			var grid = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "*" })
			{
				child,
			}.Padding(new Thickness(10, 20, 30, 40));
			Bridge(grid);

			CometBackendLayoutEngine.Layout(grid, new Size(200, 160));

			Assert.Equal(new Rect(10, 20, 160, 100), Node(child).ArrangedFrame);
		}

		[Fact]
		public void TextEditor_EmitsPlaceholder_AndWritesBackMultilineText()
		{
			var text = new Signal<string>("first");
			var editor = new TextEditor(text).Placeholder("notes").OnTextChanged(value =>
				Assert.Contains("\n", value));
			var node = Bridge(editor);

			Assert.Equal("first", node.Get(PropertyIds.TextField_Text).AsString);
			Assert.Equal("notes", node.Get(PropertyIds.TextField_Placeholder).AsString);

			node.Sink!.OnEvent(EventIds.TextChanged, "first\nsecond");

			Assert.Equal("first\nsecond", text.Value);
			Assert.Equal("first\nsecond", node.Get(PropertyIds.TextField_Text).AsString);
		}

		[Fact]
		public void BorderlessTextInputs_EmitExplicitChromeProperty_AndPreserveWriteBack()
		{
			var entryText = new Signal<string>("entry");
			var editorText = new Signal<string>("editor");
			var entry = new TextField(entryText).Borderless();
			var editor = new TextEditor(editorText).Borderless();
			var entryNode = Bridge(entry);
			var editorNode = Bridge(editor);

			Assert.True(entryNode.Get(PropertyIds.TextField_Borderless).AsBool);
			Assert.True(editorNode.Get(PropertyIds.TextField_Borderless).AsBool);

			entryNode.Sink!.OnEvent(EventIds.TextChanged, "entry updated");
			editorNode.Sink!.OnEvent(EventIds.TextChanged, "editor\nupdated");

			Assert.Equal("entry updated", entryText.Value);
			Assert.Equal("editor\nupdated", editorText.Value);
		}

		[Fact]
		public void RefreshView_RequestWritesBack_AndInvokesCallback()
		{
			var refreshing = new Signal<bool>(false);
			var calls = 0;
			var refresh = new RefreshView(refreshing).OnRefresh(() => calls++);
			refresh.Add(new Text("content"));
			var node = Bridge(refresh);

			Assert.False(node.Get(PropertyIds.Refresh_IsRefreshing).AsBool);
			node.Sink!.OnEvent(EventIds.RefreshRequested);

			Assert.True(refreshing.Value);
			Assert.True(node.Get(PropertyIds.Refresh_IsRefreshing).AsBool);
			Assert.Equal(1, calls);
		}

		[Fact]
		public async Task RefreshView_AsyncCallbackClearsRefreshing()
		{
			var refreshing = new Signal<bool>(false);
			var refresh = new RefreshView(refreshing).OnRefresh(async () =>
			{
				await Task.Yield();
			});
			refresh.Add(new Text("content"));
			var node = Bridge(refresh);

			node.Sink!.OnEvent(EventIds.RefreshRequested);

			await WaitUntilAsync(() => !refreshing.Value);
			Assert.False(node.Get(PropertyIds.Refresh_IsRefreshing).AsBool);
		}

		[Fact]
		public void RefreshView_NativeEndClearsRefreshing()
		{
			var refreshing = new Signal<bool>(true);
			var refresh = new RefreshView(refreshing);
			refresh.Add(new Text("content"));
			var node = Bridge(refresh);

			node.Sink!.OnEvent(EventIds.RefreshEnded);

			Assert.False(refreshing.Value);
			Assert.False(node.Get(PropertyIds.Refresh_IsRefreshing).AsBool);
		}

		[Fact]
		public void Text_EmitsCharacterSpacingLineBreakModeAndMaxLines()
		{
			var node = Bridge(new Text("coffee")
				.CharacterSpacing(1.5)
				.LineBreakMode(LineBreakMode.MiddleTruncation)
				.MaxLines(2));

			Assert.Equal(1.5, node.Get(PropertyIds.Text_CharacterSpacing).AsDouble, 3);
			Assert.Equal(5, node.Get(PropertyIds.Text_LineBreakMode).AsInt);
			Assert.Equal(2, node.Get(PropertyIds.Text_MaxLines).AsInt);
		}

		[Fact]
		public void Button_EmitsSourceTypographyAndSingleLineContract()
		{
			var node = Bridge(new Button("MARK COMPLETE", () => { })
				.FontFamily("ManropeSemibold")
				.FontSize(11)
				.CharacterSpacing(1.5)
				.MaxLines(1)
				.LineBreakMode(LineBreakMode.NoWrap));

			Assert.Equal("MARK COMPLETE", node.Get(PropertyIds.Button_Text).AsString);
			Assert.Equal(11, node.Get(PropertyIds.Text_FontSize).AsDouble);
			Assert.Equal("ManropeSemibold", node.Get(PropertyIds.Text_FontFamily).AsString);
			Assert.Equal(1.5, node.Get(PropertyIds.Text_CharacterSpacing).AsDouble, 3);
			Assert.Equal(1, node.Get(PropertyIds.Text_MaxLines).AsInt);
			Assert.Equal(2, node.Get(PropertyIds.Text_LineBreakMode).AsInt);
		}

		[Fact]
		public void AutomationId_EmitsAndClearsOnRetainedNode()
		{
			var identified = new Text("Settings").AutomationId("settings_page");
			var node = Bridge(identified);

			Assert.Equal("settings_page", node.Get(PropertyIds.AutomationId).AsString);

			var replacement = new Text("Settings");
			replacement.UpdateFromOldView(identified);

			Assert.Same(node, Node(replacement));
			Assert.Equal(string.Empty, node.Get(PropertyIds.AutomationId).AsString);
		}

		[Fact]
		public void AutomationId_UnsetValueDoesNotCrossInitialBridge()
		{
			var node = Bridge(new Text("Settings"));

			Assert.False(node.Properties.ContainsKey(PropertyIds.AutomationId.Value));
		}

		[Fact]
		public void RecordGesture_RoutesFullLifecycle()
		{
			var statuses = new List<GestureStatus>();
			var view = new Text("record").OnRecord(gesture => statuses.Add(gesture.Status));
			var node = Bridge(view);

			Assert.True(node.Get(PropertyIds.HasRecordGesture).AsBool);
			node.Sink!.OnGesture(GestureKind.Pan, new GestureData(GestureState.Began, default));
			node.Sink.OnGesture(GestureKind.Pan,
				new GestureData(GestureState.Changed, default, new Point(-20, 5)));
			node.Sink.OnGesture(GestureKind.Pan, new GestureData(GestureState.Ended, default));

			Assert.Equal(
				new[] { GestureStatus.Started, GestureStatus.Running, GestureStatus.Completed },
				statuses);

			node.Sink.OnGesture(GestureKind.Pan, new GestureData(GestureState.Began, default));
			node.Sink.OnGesture(GestureKind.Pan, new GestureData(GestureState.Cancelled, default));
			Assert.Equal(GestureStatus.Canceled, statuses[^1]);
		}

		[Fact]
		public void BackButtonBehavior_CommandInterceptsUntilItPops()
		{
			var nav = new NavigationView();
			nav.Add(new Text("root"));
			var pops = 0;
			nav.SetPerformNavigate(_ => { });
			nav.SetPerformPop(() => pops++);

			var detail = new Text("detail");
			var commandCalls = 0;
			detail.BackButtonBehavior(new BackButtonBehavior
			{
				Command = new TestCommand(() => commandCalls++),
			});
			nav.Navigate(detail);

			nav.RequestBack();
			Assert.Equal(1, commandCalls);
			Assert.Equal(0, pops);

			detail.BackButtonBehavior(new BackButtonBehavior
			{
				Command = new TestCommand(() => nav.Pop()),
			});
			nav.RequestBack();
			Assert.Equal(1, pops);
		}

		[Fact]
		public void BackButtonBehavior_DisabledConsumesBack()
		{
			var nav = new NavigationView();
			var pops = 0;
			nav.SetPerformNavigate(_ => { });
			nav.SetPerformPop(() => pops++);
			nav.Navigate(new Text("blocked").BackButtonBehavior(new BackButtonBehavior
			{
				IsEnabled = false,
			}));

			nav.RequestBack();

			Assert.Equal(0, pops);
		}

		[Fact]
		public void BackButtonBehavior_UnavailableCommandFallsThroughToPop()
		{
			var nav = new NavigationView();
			var pops = 0;
			var commandCalls = 0;
			nav.SetPerformNavigate(_ => { });
			nav.SetPerformPop(() => pops++);
			nav.Navigate(new Text("detail").BackButtonBehavior(new BackButtonBehavior
			{
				Command = new TestCommand(() => commandCalls++, canExecute: false),
			}));

			nav.RequestBack();

			Assert.Equal(1, pops);
			Assert.Equal(0, commandCalls);
		}

		[Fact]
		public void NavigationView_ProgrammaticPopBypassesBackButtonBehavior()
		{
			var nav = new NavigationView();
			var pops = 0;
			var commandCalls = 0;
			nav.SetPerformNavigate(_ => { });
			nav.SetPerformPop(() => pops++);
			nav.Navigate(new Text("detail").BackButtonBehavior(new BackButtonBehavior
			{
				Command = new TestCommand(() => commandCalls++),
			}));

			nav.Pop();

			Assert.Equal(1, pops);
			Assert.Equal(0, commandCalls);
		}

		[Fact]
		public void NavigationView_PopToRoot_InvokesContentReset()
		{
			var root = new Text("root");
			var nav = new NavigationView { root };
			View? resetRoot = null;
			nav.SetPerformNavigate(_ => { });
			nav.SetPerformContentReset(view => resetRoot = view);
			nav.Navigate(new Text("detail"));

			nav.PopToRoot();

			Assert.Same(root, resetRoot);
		}

		[Fact]
		public async Task Image_EmitsLocalFileStreamAndCircularClip()
		{
			var fileNode = Bridge(new Image(new FileImageSource { File = "/data/avatar.png" })
				.ClipShape(new Circle()));
			Assert.Equal("/data/avatar.png", fileNode.Get(PropertyIds.Image_Source).AsString);
			Assert.True(fileNode.Get(PropertyIds.ClipShape).AsBool);

			var streamNode = Bridge(new Image(new TestStreamImageSource(new byte[] { 1, 2, 3 })));
			await WaitUntilAsync(() => streamNode.Get(PropertyIds.Image_Data).AsObject is byte[]);
			Assert.Equal(new byte[] { 1, 2, 3 },
				(byte[])streamNode.Get(PropertyIds.Image_Data).AsObject!);
		}

		[Fact]
		public void Image_ChangingClipShapeClearsMutuallyExclusiveState()
		{
			var circular = new Image("avatar").ClipShape(new Circle());
			var node = Bridge(circular);

			var rounded = new Image("avatar").ClipShape(new RoundedRectangle(8));
			rounded.UpdateFromOldView(circular);
			Assert.False(node.Get(PropertyIds.ClipShape).AsBool);
			Assert.Equal(new CornerRadii(8), node.Get(PropertyIds.CornerRadius).AsObject);

			var square = new Image("avatar");
			square.UpdateFromOldView(rounded);
			Assert.False(node.Get(PropertyIds.ClipShape).AsBool);
			Assert.Equal(default(CornerRadii), node.Get(PropertyIds.CornerRadius).AsObject);
		}

		[Fact]
		public async Task Image_StaleStreamCompletionCannotReplaceNewSource()
		{
			var delayed = new DelayedStreamImageSource();
			var current = new Signal<IImageSource>(delayed);
			var image = new Image(() => current.Value);
			var node = Bridge(image);

			current.Value = new FileImageSource { File = "/data/new-avatar.png" };
			ReactiveScheduler.FlushSync();
			await WaitUntilAsync(() => node.Get(PropertyIds.Image_Source).AsString == "/data/new-avatar.png");

			delayed.Complete(new byte[] { 1, 2, 3 });
			await Task.Delay(25);

			Assert.Equal("/data/new-avatar.png", node.Get(PropertyIds.Image_Source).AsString);
			Assert.Empty((byte[])node.Get(PropertyIds.Image_Data).AsObject!);
		}

		[Fact]
		public async Task Image_TransferredNodeRejectsPreviousOwnersStreamCompletion()
		{
			var delayed = new DelayedStreamImageSource();
			var original = new Image(delayed);
			var node = Bridge(original);

			var replacement = new Image(new FileImageSource { File = "/data/replacement.png" });
			replacement.UpdateFromOldView(original);
			Assert.Same(node, Node(replacement));
			Assert.Equal("/data/replacement.png", node.Get(PropertyIds.Image_Source).AsString);

			delayed.Complete(new byte[] { 9, 8, 7 });
			await Task.Delay(25);

			Assert.Equal("/data/replacement.png", node.Get(PropertyIds.Image_Source).AsString);
			Assert.Empty((byte[])node.Get(PropertyIds.Image_Data).AsObject!);
		}

		[Fact]
		public void ThemeManager_HostNotificationRaisesContract()
		{
			var calls = 0;
			void Handler() => calls++;
			ThemeManager.SystemThemeChanged += Handler;
			try
			{
				ThemeManager.NotifySystemThemeChanged();
				Assert.Equal(1, calls);
			}
			finally
			{
				ThemeManager.SystemThemeChanged -= Handler;
			}
		}

		static async Task WaitUntilAsync(Func<bool> condition)
		{
			for (var i = 0; i < 100 && !condition(); i++)
				await Task.Delay(10);
			Assert.True(condition());
		}

		sealed class TestCommand : ICommand
		{
			readonly Action execute;
			readonly bool canExecute;
			public TestCommand(Action execute, bool canExecute = true)
			{
				this.execute = execute;
				this.canExecute = canExecute;
			}
			public bool CanExecute(object? parameter) => canExecute;
			public void Execute(object? parameter) => execute();
			public event EventHandler? CanExecuteChanged { add { } remove { } }
		}

		sealed class TestStreamImageSource : Comet.ImageSource, IStreamImageSource
		{
			readonly byte[] data;
			public TestStreamImageSource(byte[] data) => this.data = data;
			public override bool IsEmpty => data.Length == 0;
			public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
				=> Task.FromResult<Stream>(new MemoryStream(data, writable: false));
		}

		sealed class DelayedStreamImageSource : Comet.ImageSource, IStreamImageSource
		{
			readonly TaskCompletionSource<Stream> completion =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public override bool IsEmpty => false;

			public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
			{
				cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
				return completion.Task;
			}

			public void Complete(byte[] data)
				=> completion.TrySetResult(new MemoryStream(data, writable: false));
		}

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}

		sealed class BodyTile : View
		{
			[Body]
			View Body() => new Text("body");
		}
	}
}
