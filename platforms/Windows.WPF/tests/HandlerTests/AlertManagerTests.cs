using System.Diagnostics;
using System.Reflection;
using System.Windows.Automation;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Platforms.Windows.WPF;
using PropertyCondition = System.Windows.Automation.PropertyCondition;

namespace HandlerTests;

public class AlertManagerTests
{
	[Theory]
	[InlineData(null, "OK", "OK", false)]
	[InlineData(null, "Got_it", "Got_it", false)]
	[InlineData(null, "OK", null, false)]
	[InlineData("Remove_item", "Keep_item", "Remove_item", true)]
	[InlineData("Remove_item", "Keep_item", "Keep_item", false)]
	[InlineData("Remove_item", "Keep_item", null, false)]
	[InlineData(null, "OK", "OK", false, true)]
	public async Task OnAlertRequested_PreservesButtonsAndCompletesResult(
		string? accept, string cancel, string? clickedButton, bool expectedResult, bool longMessage = false)
	{
		var title = $"Alert regression {Guid.NewGuid():N}";
		var message = longMessage
			? string.Join("\n", Enumerable.Repeat("Exported 1 of 1 item(s).", 200))
			: "Exported 1 of 1 item(s).";
		var arguments = new AlertArguments(title, message, accept, cancel);
		var expectedButtons = accept == null ? new[] { cancel } : new[] { accept, cancel };

		// Exercise the production request handler, independently of MAUI's internal
		// subscription interface (whose version compatibility is tested separately).
		var handler = typeof(WPFAlertManagerSubscription).GetMethod(
			"OnAlertRequested", BindingFlags.Static | BindingFlags.NonPublic)!;
		Exception? requestFailure = null;
		var thread = new Thread(() =>
		{
			try
			{
				handler.Invoke(null, new object?[] { null, arguments });
			}
			catch (Exception ex)
			{
				requestFailure = ex;
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		var condition = new AndCondition(
			new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId),
			new PropertyCondition(AutomationElement.NameProperty, title));
		AutomationElement? dialog = null;
		try
		{
			var timeout = Stopwatch.StartNew();
			while (dialog == null && thread.IsAlive && timeout.Elapsed < TimeSpan.FromSeconds(30))
			{
				dialog = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
				if (dialog == null)
					Thread.Sleep(25);
			}

			Assert.Null(requestFailure);
			Assert.NotNull(dialog);
			var buttons = GetDialogButtons(dialog);
			Assert.Equal(expectedButtons, buttons.Select(b => b.Current.Name).ToArray());
			Assert.NotNull(dialog.FindFirst(TreeScope.Descendants,
				new PropertyCondition(AutomationElement.NameProperty, arguments.Message)));
			if (longMessage)
			{
				var scrollable = dialog.FindAll(TreeScope.Descendants,
						new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true))
					.Cast<AutomationElement>().Select(element =>
						(ScrollPattern)element.GetCurrentPattern(ScrollPattern.Pattern));
				Assert.Contains(scrollable, pattern => pattern.Current.VerticallyScrollable);
				Assert.All(buttons, button => Assert.False(button.Current.IsOffscreen));
				Assert.True(dialog.Current.BoundingRectangle.Height <= System.Windows.SystemParameters.WorkArea.Height);
			}

			if (clickedButton == null)
				((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
			else
				((InvokePattern)buttons.Single(b => b.Current.Name == clickedButton)
					.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

			Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Alert handler did not return.");
			Assert.Null(requestFailure);
			Assert.True(arguments.Result.Task.IsCompletedSuccessfully);
			Assert.Equal(expectedResult, await arguments.Result.Task);
		}
		finally
		{
			if (thread.IsAlive)
			{
				dialog ??= AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
				// Also dismiss the old Yes/No MessageBox when a regression assertion
				// fails; it disables the title-bar close button.
				var buttons = dialog == null ? [] : GetDialogButtons(dialog);
				var button = buttons.FirstOrDefault(b => b.Current.Name == cancel)
					?? buttons.FirstOrDefault(b => b.Current.AutomationId == "7")
					?? buttons.FirstOrDefault();
				if (button != null)
					((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
			}
			Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Alert test left a dialog open.");
		}
	}

	[Fact]
	public async Task OnAlertRequested_OnNonStaThread_FaultsTaskInsteadOfLeavingItPending()
	{
		var arguments = new AlertArguments("Thread error", "Message", null, "OK");
		var handler = typeof(WPFAlertManagerSubscription).GetMethod(
			"OnAlertRequested", BindingFlags.Static | BindingFlags.NonPublic)!;

		await Task.Run(() => handler.Invoke(null, new object?[] { null, arguments }));

		Assert.True(arguments.Result.Task.IsFaulted);
		await Assert.ThrowsAsync<InvalidOperationException>(() => arguments.Result.Task);
	}

	static AutomationElement[] GetDialogButtons(AutomationElement dialog)
	{
		return dialog.FindAll(TreeScope.Descendants,
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
			.Cast<AutomationElement>()
			.Where(button =>
			{
				for (var parent = TreeWalker.ControlViewWalker.GetParent(button);
					parent != null && !parent.Equals(dialog);
					parent = TreeWalker.ControlViewWalker.GetParent(parent))
				{
					if (parent.Current.ControlType == ControlType.TitleBar)
						return false;
				}
				return true;
			}).ToArray();
	}
}
