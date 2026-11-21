using Microsoft.Maui.ApplicationModel;
using Windows.UI.StartScreen;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFAppActions : IAppActions
{
	const string ActivationArgument = "--maui-app-action";
	const string ActivationTitleArgument = "--maui-app-action-title";
	readonly IWpfJumpListProvider _provider;
	readonly AppAction? _pendingAction;
	EventHandler<AppActionEventArgs>? _appActionActivated;
	IReadOnlyList<AppAction> _actions = [];
	bool _pendingDispatched;

	public WPFAppActions()
		: this(new WindowsJumpListProvider(), Environment.GetCommandLineArgs())
	{
	}

	internal WPFAppActions(IWpfJumpListProvider provider, IReadOnlyList<string> arguments)
	{
		_provider = provider;
		var actionId = ParseArgument(arguments, ActivationArgument);
		if (actionId is not null)
		{
			var title = ParseArgument(arguments, ActivationTitleArgument) ?? actionId;
			_pendingAction = new AppAction(actionId, title);
		}
	}

	public bool IsSupported => _provider.IsSupported;

	public async Task<IEnumerable<AppAction>> GetAsync()
	{
		if (!IsSupported)
			return [];

		_actions = await _provider.GetAsync();
		return _actions;
	}

	public async Task SetAsync(IEnumerable<AppAction> actions)
	{
		if (!IsSupported)
			throw new FeatureNotSupportedException(
				"WPF app actions require an MSIX-packaged application with Windows Jump List support.");

		var values = actions.ToList();
		await _provider.SetAsync(values);
		_actions = values;
		TryDispatchPendingAction();
	}

	public event EventHandler<AppActionEventArgs>? AppActionActivated
	{
		add
		{
			_appActionActivated += value;
			TryDispatchPendingAction();
		}
		remove => _appActionActivated -= value;
	}

	void TryDispatchPendingAction()
	{
		if (!IsSupported || _pendingDispatched || _pendingAction is null || _appActionActivated is null)
			return;

		var action = _actions.FirstOrDefault(value =>
			string.Equals(value.Id, _pendingAction.Id, StringComparison.Ordinal)) ?? _pendingAction;
		_pendingDispatched = true;
		_appActionActivated.Invoke(this, new AppActionEventArgs(action));
	}

	internal static string? ParseActionId(IReadOnlyList<string> arguments) =>
		ParseArgument(arguments, ActivationArgument);

	static string? ParseArgument(IReadOnlyList<string> arguments, string name)
	{
		for (var index = 0; index < arguments.Count - 1; index++)
		{
			if (string.Equals(arguments[index], name, StringComparison.Ordinal))
				return Uri.UnescapeDataString(arguments[index + 1]);
		}

		return null;
	}

	internal static string CreateArguments(AppAction action) =>
		$"{ActivationArgument} {Uri.EscapeDataString(action.Id)} " +
		$"{ActivationTitleArgument} {Uri.EscapeDataString(action.Title)}";

	internal static AppAction? ParseArguments(string arguments)
	{
		var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var id = ParseArgument(tokens, ActivationArgument);
		if (id is null)
			return null;

		return new AppAction(
			id,
			ParseArgument(tokens, ActivationTitleArgument) ?? id);
	}
}

internal interface IWpfJumpListProvider
{
	bool IsSupported { get; }
	Task<IReadOnlyList<AppAction>> GetAsync();
	Task SetAsync(IReadOnlyList<AppAction> actions);
}

internal sealed class WindowsJumpListProvider : IWpfJumpListProvider
{
	public bool IsSupported => JumpList.IsSupported();

	public async Task<IReadOnlyList<AppAction>> GetAsync()
	{
		var jumpList = await JumpList.LoadCurrentAsync();
		return jumpList.Items
			.Select(item => new AppAction(
				ParseActionId(item.Arguments) ?? item.Arguments,
				item.DisplayName,
				item.Description,
				null))
			.ToList();
	}

	public async Task SetAsync(IReadOnlyList<AppAction> actions)
	{
		var jumpList = await JumpList.LoadCurrentAsync();
		jumpList.Items.Clear();
		foreach (var action in actions)
		{
			var item = JumpListItem.CreateWithArguments(
				WPFAppActions.CreateArguments(action),
				action.Title);
			item.Description = action.Subtitle ?? string.Empty;
			jumpList.Items.Add(item);
		}

		await jumpList.SaveAsync();
	}

	static string? ParseActionId(string arguments)
		=> WPFAppActions.ParseArguments(arguments)?.Id;
}
