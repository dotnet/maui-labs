namespace CometTaskApp;

public static class ViewAutomationExtensions
{
	public static T AutomationId<T>(this T view, string automationId)
		where T : View
	{
		view.SetAutomationId(automationId);
		return view;
	}
}
