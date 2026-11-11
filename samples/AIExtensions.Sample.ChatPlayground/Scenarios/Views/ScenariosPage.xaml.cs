using System.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>The Agentic tab: a scenario list beside the selected scenario's workspace.</summary>
public partial class ScenariosPage : ContentPage
{
    private readonly ScenariosViewModel _viewModel;
    private readonly Dictionary<ScenarioSessionViewModel, ScenarioView> _views = [];

    /// <summary>Initializes the page.</summary>
    public ScenariosPage(ScenariosViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        ShowSelected();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenariosViewModel.Selected))
            ShowSelected();
    }

    private void ScenarioTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ScenarioItem item)
        {
            _viewModel.SelectedItem = item;
            ScenariosLayout.CloseSidebar();
        }
    }

    private async void ShowSelected()
    {
        if (_viewModel.Selected is not { } session)
            return;

        // Each scenario keeps its view, so its chat, scroll position and panel stay as they were.
        if (!_views.TryGetValue(session, out var view))
        {
            view = new ScenarioView(session, _viewModel.Accent);
            _views.Add(session, view);
        }

        ScenarioHost.Content = view;
        await session.OpenAsync();
    }
}
