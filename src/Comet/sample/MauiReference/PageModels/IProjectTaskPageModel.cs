using CommunityToolkit.Mvvm.Input;
using MauiReference.Models;

namespace MauiReference.PageModels;

public interface IProjectTaskPageModel
{
	IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
	bool IsBusy { get; }
}