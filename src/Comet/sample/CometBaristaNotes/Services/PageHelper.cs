namespace CometBaristaNotes.Services;

/// <summary>
/// Provides access to the active Comet-hosted page for displaying platform alerts
/// without reaching into the app's page hierarchy from individual views.
/// </summary>
public static class PageHelper
{
	public static async Task DisplayAlertAsync(string title, string message, string cancel)
	{
		var page = GetCurrentPage();
		if (page is not null)
			await page.DisplayAlertAsync(title, message, cancel);
	}

	public static async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
	{
		var page = GetCurrentPage();
		if (page is null)
			return false;

		return await page.DisplayAlertAsync(title, message, accept, cancel);
	}

	static dynamic? GetCurrentPage()
	{
		var app = IPlatformApplication.Current;
		if (app != null)
		{
			var application = app.Services.GetService<Microsoft.Maui.IApplication>();
			var page = GetPageFromWindows(application?.Windows);
			if (page is not null)
				return page;
		}

		return null;
	}

	static dynamic? GetPageFromWindows(IEnumerable<Microsoft.Maui.IWindow>? windows)
	{
		if (windows == null)
			return null;

		foreach (var window in windows)
		{
			var page = window?.GetType().GetProperty("Page")?.GetValue(window);
			if (page is not null)
				return page;
		}

		return null;
	}

	/// <summary>
	/// Dispatch an action on the main/UI thread.
	/// </summary>
	public static void DispatchOnMainThread(Action action)
	{
		Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(action);
	}
}
