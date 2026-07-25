using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.DevFlow.Agent.Native;

namespace DevFlow.Sample.Native.Android;

/// <summary>
/// A plain .NET for Android activity — no MAUI anywhere — that starts the DevFlow agent and
/// builds the shared sample screen out of <c>Android.Widget</c> views.
///
/// Views are identified by <see cref="View.Tag"/>, which is what the DevFlow Android backend
/// reads first when resolving an automation id.
/// </summary>
[Activity(
    Label = "DevFlow Native Sample",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : Activity
{
    private readonly SampleModel _model = new();

    private TextView _status = null!;
    private TextView _count = null!;
    private EditText _titleEntry = null!;
    private EditText _descriptionEntry = null!;
    private LinearLayout _todoList = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Explicit bootstrap — the agent never starts itself.
        this.StartDevFlowAgent();

        SetContentView(BuildContent());
        RefreshTodos();
    }

    private View BuildContent()
    {
        var scroll = new ScrollView(this) { Tag = "RootScroll" };
        var root = Column(padding: Dp(16));
        root.Tag = "RootLayout";

        root.AddView(Label("HeaderLabel", SampleModel.HeaderText, sizeSp: 24, bold: true));
        _count = Label("CountLabel", _model.CountText, sizeSp: 14);
        root.AddView(_count);
        _status = Label("StatusLabel", _model.Status, sizeSp: 14);
        root.AddView(_status);

        _titleEntry = Entry("NewTodoEntry", "What needs doing?");
        root.AddView(_titleEntry);
        _descriptionEntry = Entry("NewDescriptionEntry", "Notes");
        root.AddView(_descriptionEntry);

        root.AddView(Button("AddButton", "Add", (_, _) =>
        {
            _model.Add(_titleEntry.Text ?? string.Empty, _descriptionEntry.Text ?? string.Empty);
            _titleEntry.Text = string.Empty;
            _descriptionEntry.Text = string.Empty;
            RefreshTodos();
        }));

        root.AddView(Button("TestButton", "Test Button",
            (_, _) => Record("TestButton clicked")));

        var toggle = new Switch(this) { Tag = "TestSwitch", Text = "Test Switch" };
        toggle.CheckedChange += (_, e) => Record($"TestSwitch {(e.IsChecked ? "on" : "off")}");
        root.AddView(toggle);

        root.AddView(Button("GetPostsButton", "Get Posts", async (_, _) =>
        {
            Record("fetching posts");
            await _model.FetchPostsAsync();
            Record(null);
        }));

        _todoList = Column(padding: 0);
        _todoList.Tag = "TodoList";
        root.AddView(_todoList);

        var spacer = new Space(this) { Tag = "BottomSpacer" };
        spacer.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(600));
        root.AddView(spacer);

        scroll.AddView(root);
        return scroll;
    }

    private void RefreshTodos()
    {
        _todoList.RemoveAllViews();

        foreach (var todo in _model.Todos.ToList())
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, Dp(6), 0, Dp(6));

            var check = new CheckBox(this)
            {
                Tag = "TodoCheckBox",
                Text = todo.Title,
                Checked = todo.IsDone,
            };
            check.CheckedChange += (_, _) =>
            {
                if (check.Checked != todo.IsDone)
                {
                    _model.Toggle(todo);
                    Record(null);
                }
            };
            check.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            row.AddView(check);

            row.AddView(Button("DeleteButton", "Delete", (_, _) =>
            {
                _model.Remove(todo);
                RefreshTodos();
            }));

            _todoList.AddView(row);
        }

        Record(null);
    }

    /// <summary>Pushes model state into the status/count labels. Pass null to only re-read.</summary>
    private void Record(string? action)
    {
        if (action is not null)
            _model.RecordAction(action);

        _status.Text = _model.Status;
        _count.Text = _model.CountText;
    }

    private LinearLayout Column(int padding)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(padding, padding, padding, padding);
        return layout;
    }

    private TextView Label(string id, string text, int sizeSp, bool bold = false)
    {
        var label = new TextView(this) { Tag = id, Text = text };
        label.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sizeSp);
        label.SetPadding(0, Dp(4), 0, Dp(4));

        if (bold)
            label.SetTypeface(label.Typeface, TypefaceStyle.Bold);

        return label;
    }

    private EditText Entry(string id, string hint) =>
        new(this) { Tag = id, Hint = hint };

    private Button Button(string id, string text, EventHandler handler)
    {
        var button = new Button(this) { Tag = id, Text = text };
        button.Click += handler;
        return button;
    }

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1f));
}
