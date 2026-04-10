using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet
{
	/// <summary>
	/// Additional automation/accessibility properties for Comet views.
	/// Extends the existing SemanticDescription/SemanticHint/SemanticHeadingLevel in ViewExtensions.
	/// Maps to MAUI's AutomationProperties attached properties.
	/// </summary>
	public static class AutomationExtensions
	{
		const string AutomationNameKey = "Comet.Automation.Name";
		const string AutomationHelpTextKey = "Comet.Automation.HelpText";
		const string AutomationIsInAccessibleTreeKey = "Comet.Automation.IsInAccessibleTree";

		/// <summary>
		/// Sets the automation name (accessible name for screen readers).
		/// Maps to AutomationProperties.Name.
		/// </summary>
		public static T AutomationName<T>(this T view, string name) where T : View
			=> view.SetEnvironment(AutomationNameKey, (object)name, false);

		/// <summary>
		/// Sets automation help text.
		/// Maps to AutomationProperties.HelpText.
		/// </summary>
		public static T AutomationHelpText<T>(this T view, string helpText) where T : View
			=> view.SetEnvironment(AutomationHelpTextKey, (object)helpText, false);

		/// <summary>
		/// Sets whether the view is in the accessible tree.
		/// Maps to AutomationProperties.IsInAccessibleTree.
		/// </summary>
		public static T IsInAccessibleTree<T>(this T view, bool isAccessible) where T : View
			=> view.SetEnvironment(AutomationIsInAccessibleTreeKey, (object)isAccessible, false);

		// Getter methods for use by handlers
		public static string GetAutomationName(this View view)
			=> view.GetEnvironment<string>(AutomationNameKey);

		public static string GetAutomationHelpText(this View view)
			=> view.GetEnvironment<string>(AutomationHelpTextKey);

		public static bool? GetIsInAccessibleTree(this View view)
			=> view.GetEnvironment<bool?>(AutomationIsInAccessibleTreeKey);

		/// <summary>
		/// Applies automation properties to a MAUI platform view.
		/// Called by handlers when connecting views.
		/// </summary>
		public static void ApplyAutomationProperties(View cometView, Microsoft.Maui.Controls.View mauiView)
		{
			if (cometView is null || mauiView is null) return;

			var automationName = cometView.GetAutomationName();
			if (automationName is not null)
				Microsoft.Maui.Controls.AutomationProperties.SetName(mauiView, automationName);

			var helpText = cometView.GetAutomationHelpText();
			if (helpText is not null)
				Microsoft.Maui.Controls.AutomationProperties.SetHelpText(mauiView, helpText);

			var isInTree = cometView.GetIsInAccessibleTree();
			if (isInTree.HasValue)
				Microsoft.Maui.Controls.AutomationProperties.SetIsInAccessibleTree(mauiView, isInTree.Value);

			if (cometView.AccessibilityId is not null)
				mauiView.AutomationId = cometView.AccessibilityId;
		}
	}
}
