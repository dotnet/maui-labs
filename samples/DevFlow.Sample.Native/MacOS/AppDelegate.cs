using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Native;

namespace DevFlow.Sample.Native.MacOS;

/// <summary>
/// A plain .NET for macOS app delegate — no MAUI — that starts the DevFlow agent and builds the
/// shared sample screen out of AppKit views.
///
/// Views are identified by <c>NSView.Identifier</c>, which is what the DevFlow AppKit backend
/// reads first when resolving an automation id.
/// </summary>
[Register(nameof(AppDelegate))]
public sealed class AppDelegate : NSApplicationDelegate
{
    private readonly SampleModel _model = new();

    private NSWindow _window = null!;
    private NSTextField _status = null!;
    private NSTextField _count = null!;
    private NSTextField _titleEntry = null!;
    private NSTextField _descriptionEntry = null!;
    private NSStackView _todoList = null!;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // Explicit bootstrap — the agent never starts itself.
        DevFlowAgent.Start(SampleAgentOptions.Create());

        _window = new NSWindow(
            new CGRect(0, 0, 520, 640),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            Title = "DevFlow Native Sample",
        };

        _window.ContentView = BuildContent();
        _window.Center();
        _window.MakeKeyAndOrderFront(null);

        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
#pragma warning disable CA1422 // Keep the sample frontmost on older and newer macOS SDKs.
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422

        RefreshTodos();
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    private NSView BuildContent()
    {
        var root = Column(spacing: 10);
        root.Identifier = "RootLayout";
        root.EdgeInsets = new NSEdgeInsets(16, 16, 16, 16);

        root.AddArrangedSubview(Label("HeaderLabel", SampleModel.HeaderText, NSFont.BoldSystemFontOfSize(22)!));
        _count = Label("CountLabel", _model.CountText, NSFont.SystemFontOfSize(13)!);
        root.AddArrangedSubview(_count);
        _status = Label("StatusLabel", _model.Status, NSFont.SystemFontOfSize(13)!);
        root.AddArrangedSubview(_status);

        _titleEntry = Entry("NewTodoEntry", "What needs doing?");
        root.AddArrangedSubview(_titleEntry);
        _descriptionEntry = Entry("NewDescriptionEntry", "Notes");
        root.AddArrangedSubview(_descriptionEntry);

        root.AddArrangedSubview(Button("AddButton", "Add", () =>
        {
            _model.Add(_titleEntry.StringValue, _descriptionEntry.StringValue);
            _titleEntry.StringValue = string.Empty;
            _descriptionEntry.StringValue = string.Empty;
            RefreshTodos();
        }));

        root.AddArrangedSubview(Button("TestButton", "Test Button", () => Record("TestButton clicked")));

        var toggle = new NSButton { Identifier = "TestSwitch", Title = "Test Switch" };
        toggle.SetButtonType(NSButtonType.Switch);
        toggle.Activated += (_, _) => Record($"TestSwitch {(toggle.State == NSCellStateValue.On ? "on" : "off")}");
        root.AddArrangedSubview(toggle);

        root.AddArrangedSubview(Button("GetPostsButton", "Get Posts", async () =>
        {
            Record("fetching posts");
            await _model.FetchPostsAsync();
            Record(null);
        }));

        _todoList = Column(spacing: 6);
        _todoList.Identifier = "TodoList";
        root.AddArrangedSubview(_todoList);

        var scroll = new NSScrollView
        {
            Identifier = "RootScroll",
            HasVerticalScroller = true,
            DrawsBackground = false,
            DocumentView = root,
        };

        return scroll;
    }

    private void RefreshTodos()
    {
        foreach (var existing in _todoList.ArrangedSubviews)
        {
            _todoList.RemoveArrangedSubview(existing);
            existing.RemoveFromSuperview();
        }

        foreach (var todo in _model.Todos.ToList())
        {
            var row = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Spacing = 8,
                Alignment = NSLayoutAttribute.CenterY,
            };

            var check = new NSButton
            {
                Identifier = "TodoCheckBox",
                Title = todo.Title,
                State = todo.IsDone ? NSCellStateValue.On : NSCellStateValue.Off,
            };
            check.SetButtonType(NSButtonType.Switch);
            check.Activated += (_, _) =>
            {
                _model.Toggle(todo);
                Record(null);
            };
            row.AddArrangedSubview(check);

            row.AddArrangedSubview(Button("DeleteButton", "Delete", () =>
            {
                _model.Remove(todo);
                RefreshTodos();
            }));

            _todoList.AddArrangedSubview(row);
        }

        Record(null);
    }

    /// <summary>Pushes model state into the status/count labels. Pass null to only re-read.</summary>
    private void Record(string? action)
    {
        if (action is not null)
            _model.RecordAction(action);

        _status.StringValue = _model.Status;
        _count.StringValue = _model.CountText;
    }

    private static NSStackView Column(int spacing) => new()
    {
        Orientation = NSUserInterfaceLayoutOrientation.Vertical,
        Spacing = spacing,
        Alignment = NSLayoutAttribute.Leading,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    private static NSTextField Label(string id, string text, NSFont font)
    {
        var label = NSTextField.CreateLabel(text);
        label.Identifier = id;
        label.Font = font;
        return label;
    }

    private static NSTextField Entry(string id, string placeholder)
    {
        var field = new NSTextField
        {
            Identifier = id,
            PlaceholderString = placeholder,
        };
        field.WidthAnchor.ConstraintGreaterThanOrEqualTo(320).Active = true;
        return field;
    }

    private static NSButton Button(string id, string title, Action handler)
    {
        var button = new NSButton { Identifier = id, Title = title, BezelStyle = NSBezelStyle.Rounded };
        button.SetButtonType(NSButtonType.MomentaryPushIn);
        button.Activated += (_, _) => handler();
        return button;
    }
}
