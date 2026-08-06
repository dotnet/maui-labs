using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Canvas;

/// <summary>
/// Singleton client state for the generative canvas: the currently rendered view, busy/empty flags,
/// the confirm overlay, and the <b>persistent</b> form tree (which survives re-inflation until a new
/// chat / <c>clear_ui</c>). The <see cref="GenerativeCanvasView"/> binds to it; the UI tools mutate it
/// on the main thread. See <c>docs/GenerativeUI/spec/overview.md §9</c>.
/// </summary>
public sealed class CanvasState : INotifyPropertyChanged
{
    private View? _currentView;
    private bool _isBusy;
    private bool _isConfirmVisible;
    private string _confirmTitle = "";
    private string _confirmMessage = "";
    private string _confirmLabel = "Yes";
    private string _cancelLabel = "Cancel";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The persistent, editable form tree bound two-way by <c>Field</c>/<c>Entry</c> nodes.</summary>
    public UiObject FormRoot { get; private set; } = new();

    /// <summary>The current root view rendered in the canvas, or <c>null</c> for the empty state.</summary>
    public View? CurrentView
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>True while a turn is running (drives a busy indicator).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
    }

    /// <summary>True when nothing is rendered (drives the welcome/empty placeholder).</summary>
    public bool IsEmpty => _currentView is null;

    public bool IsConfirmVisible
    {
        get => _isConfirmVisible;
        private set { if (_isConfirmVisible != value) { _isConfirmVisible = value; OnPropertyChanged(); } }
    }

    public string ConfirmTitle { get => _confirmTitle; private set { _confirmTitle = value; OnPropertyChanged(); } }
    public string ConfirmMessage { get => _confirmMessage; private set { _confirmMessage = value; OnPropertyChanged(); } }
    public string ConfirmLabel { get => _confirmLabel; private set { _confirmLabel = value; OnPropertyChanged(); } }
    public string CancelLabel { get => _cancelLabel; private set { _cancelLabel = value; OnPropertyChanged(); } }

    /// <summary>Replaces the rendered view.</summary>
    public void SetView(View view) => CurrentView = view;

    /// <summary>Shows the confirm overlay.</summary>
    public void ShowConfirm(string title, string message, string? confirmLabel, string? cancelLabel)
    {
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmLabel = string.IsNullOrWhiteSpace(confirmLabel) ? "Yes" : confirmLabel!;
        CancelLabel = string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel!;
        IsConfirmVisible = true;
    }

    /// <summary>Hides the confirm overlay.</summary>
    public void HideConfirm() => IsConfirmVisible = false;

    /// <summary>Resets the canvas and form for a new conversation.</summary>
    public void Reset()
    {
        HideConfirm();
        CurrentView = null;
        FormRoot = new UiObject();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
