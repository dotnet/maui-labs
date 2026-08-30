using System.ComponentModel;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace DevFlow.Sample;

public class TodoItem : INotifyPropertyChanged, IDevFlowStableItemKey
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private bool _isCompleted;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    string? IDevFlowStableItemKey.DevFlowStableItemKey => Id;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(nameof(Title)); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(nameof(Description)); }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
