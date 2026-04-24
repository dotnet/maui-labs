namespace CometTodoApp;

public class MainPageState
{
	public string NewTaskText { get; set; } = "";
}

public class MainPage : Component<MainPageState>
{
	static int _nextId = 1;
	readonly SignalList<TodoItem> _todos = new();

	public override View Render()
	{
		return VStack(
			// Row 0: New task input row
			HStack(
				TextField(State.NewTaskText, "")
					.OnTextChanged(t => SetState(s => s.NewTaskText = t))
					.FillHorizontal(),
				Button("Create", () =>
				{
					var text = State.NewTaskText?.Trim();
					if (!string.IsNullOrEmpty(text))
					{
						_todos.Add(new TodoItem
						{
							Id = _nextId++,
							Task = text,
							Done = false
						});
					}
					else
					{
						_todos.Add(new TodoItem
						{
							Id = _nextId++,
							Task = "New Task",
							Done = false
						});
					}
					SetState(s => s.NewTaskText = "");
				})
			),

			// Row 1: Todo list (takes remaining space)
			ScrollView(Orientation.Vertical,
				VStack(TodoRows())
			).FillVertical(),

			// Row 2: Clear list button at bottom
			Button("Clear List", () =>
			{
				_todos.Batch(list =>
				{
					list.Clear();
				});
			})
		);
	}

	View[] TodoRows()
	{
		var views = new View[_todos.Count];
		for (var i = 0; i < _todos.Count; i++)
		{
			views[i] = TodoRow(_todos[i]);
		}
		return views;
	}

	View TodoRow(TodoItem item)
	{
		return HStack(
			CheckBox(item.Done)
				.OnCheckedChanged(isChecked =>
				{
					item.Done = isChecked;
					_todos.Batch(list => { });
				}),
			Text(item.Task)
				.TextDecorations(item.Done ? Microsoft.Maui.TextDecorations.Strikethrough : Microsoft.Maui.TextDecorations.None)
				.VerticalTextAlignment(TextAlignment.Center)
				.FillHorizontal()
		).Frame(height: 54);
	}
}
