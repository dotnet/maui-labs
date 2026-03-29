using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxAppActions : IAppActions
{
	readonly List<AppAction> _actions = new();
	EventHandler<AppActionEventArgs>? _appActionActivated;

	public bool IsSupported => true;

	public Task<IEnumerable<AppAction>> GetAsync() =>
		Task.FromResult<IEnumerable<AppAction>>(_actions.ToList());

	public Task SetAsync(IEnumerable<AppAction> actions)
	{
		_actions.Clear();
		_actions.AddRange(actions);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Invoke an app action by ID. Call this from command-line argument handling
	/// or D-Bus activation to trigger the registered action.
	/// </summary>
	public void InvokeAction(string actionId)
	{
		var action = _actions.FirstOrDefault(a => a.Id == actionId);
		if (action != null)
			_appActionActivated?.Invoke(this, new AppActionEventArgs(action));
	}

	public event EventHandler<AppActionEventArgs>? AppActionActivated
	{
		add => _appActionActivated += value;
		remove => _appActionActivated -= value;
	}
}
