using System.Diagnostics;

namespace ControlGallery.Pages.Features;

public class TodoDataStore
{
    private readonly List<TodoTask> _taskData = new List<TodoTask>()
    {
        new(){ Id="todo-0", Name="Eat", Completed=true  },
        new(){ Id="todo-1", Name="Sleep", Completed=false  },
        new(){ Id="todo-2", Name="Repeat", Completed=false  },
    };

    public event EventHandler TaskDataChanged;

    private void OnTaskDataChanged()
    {
        TaskDataChanged?.Invoke(this, EventArgs.Empty);
    }

    public List<TodoTask> GetData() => _taskData;

    public void AddTask(TodoTask newTask)
    {
        Debug.WriteLine($"AddTask: {newTask.Id}: {newTask.Name} ({(newTask.Completed ? "" : "not ")}completed)");
        _taskData.Add(newTask);
        OnTaskDataChanged();
    }

    public void EditTask(string id, string newName)
    {
        Debug.WriteLine($"EditTask: {id}: {newName}");
        _taskData.Single(t => t.Id == id).Name = newName;
        OnTaskDataChanged();
    }

    public void DeleteTask(string id)
    {
        Debug.WriteLine($"DeleteTask: {id}");
        _taskData.Remove(_taskData.Single(t => t.Id == id));
        OnTaskDataChanged();
    }

    public void ToggleCompletedTask(string id)
    {
        Debug.WriteLine($"ToggleCompletedTask: {id}");
        var task = _taskData.Single(t => t.Id == id);
        task.Completed = !task.Completed;
        OnTaskDataChanged();
    }
}