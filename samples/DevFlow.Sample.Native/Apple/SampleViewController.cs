using CoreGraphics;
using Foundation;
using UIKit;

namespace DevFlow.Sample.Native.Apple;

/// <summary>
/// The shared UIKit screen for the iOS and Mac Catalyst samples — plain .NET for iOS, no MAUI.
///
/// Views are identified by <c>AccessibilityIdentifier</c>, which is what the DevFlow UIKit
/// backend reads first when resolving an automation id.
/// </summary>
public sealed class SampleViewController : UIViewController
{
    private readonly SampleModel _model = new();

    private UILabel _status = null!;
    private UILabel _count = null!;
    private UITextField _titleEntry = null!;
    private UITextField _descriptionEntry = null!;
    private UIStackView _todoList = null!;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = "RootLayout";

        var root = Column(spacing: 12);
        root.AccessibilityIdentifier = "ContentLayout";

        root.AddArrangedSubview(Label("HeaderLabel", SampleModel.HeaderText, UIFont.BoldSystemFontOfSize(24)));
        _count = Label("CountLabel", _model.CountText, UIFont.SystemFontOfSize(14));
        root.AddArrangedSubview(_count);
        _status = Label("StatusLabel", _model.Status, UIFont.SystemFontOfSize(14));
        root.AddArrangedSubview(_status);

        _titleEntry = Entry("NewTodoEntry", "What needs doing?");
        root.AddArrangedSubview(_titleEntry);
        _descriptionEntry = Entry("NewDescriptionEntry", "Notes");
        root.AddArrangedSubview(_descriptionEntry);

        root.AddArrangedSubview(Button("AddButton", "Add", () =>
        {
            _model.Add(_titleEntry.Text ?? string.Empty, _descriptionEntry.Text ?? string.Empty);
            _titleEntry.Text = string.Empty;
            _descriptionEntry.Text = string.Empty;
            RefreshTodos();
        }));

        root.AddArrangedSubview(Button("TestButton", "Test Button", () => Record("TestButton clicked")));

        var toggle = new UISwitch { AccessibilityIdentifier = "TestSwitch" };
        toggle.ValueChanged += (_, _) => Record($"TestSwitch {(toggle.On ? "on" : "off")}");
        root.AddArrangedSubview(toggle);

        root.AddArrangedSubview(Button("GetPostsButton", "Get Posts", async () =>
        {
            Record("fetching posts");
            await _model.FetchPostsAsync();
            Record(null);
        }));

        _todoList = Column(spacing: 6);
        _todoList.AccessibilityIdentifier = "TodoList";
        root.AddArrangedSubview(_todoList);

        var scroll = new UIScrollView
        {
            AccessibilityIdentifier = "RootScroll",
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.AddSubview(root);
        View.AddSubview(scroll);

        var guide = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(
        [
            scroll.TopAnchor.ConstraintEqualTo(guide.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(guide.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor),

            root.TopAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TopAnchor, 16),
            root.BottomAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.BottomAnchor, -16),
            root.LeadingAnchor.ConstraintEqualTo(scroll.FrameLayoutGuide.LeadingAnchor, 16),
            root.TrailingAnchor.ConstraintEqualTo(scroll.FrameLayoutGuide.TrailingAnchor, -16),
        ]);

        RefreshTodos();
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
            var row = new UIStackView
            {
                Axis = UILayoutConstraintAxis.Horizontal,
                Spacing = 8,
                Alignment = UIStackViewAlignment.Center,
            };

            var check = new UIButton(UIButtonType.System) { AccessibilityIdentifier = "TodoCheckBox" };
            check.SetTitle(TodoTitle(todo), UIControlState.Normal);
            check.HorizontalAlignment = UIControlContentHorizontalAlignment.Left;
            check.TouchUpInside += (_, _) =>
            {
                _model.Toggle(todo);
                check.SetTitle(TodoTitle(todo), UIControlState.Normal);
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

    private static string TodoTitle(SampleTodo todo) => todo.IsDone ? $"[x] {todo.Title}" : $"[ ] {todo.Title}";

    /// <summary>Pushes model state into the status/count labels. Pass null to only re-read.</summary>
    private void Record(string? action)
    {
        if (action is not null)
            _model.RecordAction(action);

        _status.Text = _model.Status;
        _count.Text = _model.CountText;
    }

    private static UIStackView Column(int spacing) => new()
    {
        Axis = UILayoutConstraintAxis.Vertical,
        Spacing = spacing,
        Alignment = UIStackViewAlignment.Fill,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    // The font factories are annotated as returning UIFont? in newer Apple SDKs, so accept a
    // nullable font and leave UILabel's default in place when one is not supplied.
    private static UILabel Label(string id, string text, UIFont? font)
    {
        var label = new UILabel
        {
            AccessibilityIdentifier = id,
            Text = text,
            Lines = 0,
        };

        if (font is not null)
            label.Font = font;

        return label;
    }

    private static UITextField Entry(string id, string placeholder) => new()
    {
        AccessibilityIdentifier = id,
        Placeholder = placeholder,
        BorderStyle = UITextBorderStyle.RoundedRect,
    };

    private static UIButton Button(string id, string title, Action handler)
    {
        var button = new UIButton(UIButtonType.System) { AccessibilityIdentifier = id };
        button.SetTitle(title, UIControlState.Normal);
        button.TouchUpInside += (_, _) => handler();
        return button;
    }
}
