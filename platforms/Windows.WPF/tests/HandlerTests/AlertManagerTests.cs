using System.Diagnostics;
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

		Exception? requestFailure = null;
		var request = Task.Run(() =>
		{
			try
			{
				WpfApplicationHost.Run(_ => RequestAlert(arguments));
			}
			catch (Exception ex)
			{
				requestFailure = ex;
			}
		});

		var condition = new AndCondition(
			new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId),
			new PropertyCondition(AutomationElement.NameProperty, title));
		AutomationElement? dialog = null;
		try
		{
			var timeout = Stopwatch.StartNew();
			while (dialog == null && !request.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(30))
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

			await request.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.Null(requestFailure);
			Assert.True(arguments.Result.Task.IsCompletedSuccessfully);
			Assert.Equal(expectedResult, await arguments.Result.Task);
		}
		finally
		{
			dialog = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
			if (dialog != null)
			{
				// Also dismiss the old Yes/No MessageBox when a regression assertion
				// fails; it disables the title-bar close button.
				var buttons = GetDialogButtons(dialog);
				var button = buttons.FirstOrDefault(b => b.Current.Name == cancel)
					?? buttons.FirstOrDefault(b => b.Current.AutomationId == "7")
					?? buttons.FirstOrDefault();
				if (button != null)
					((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
			}
			await request.WaitAsync(TimeSpan.FromSeconds(10));
		}
	}

	[Fact]
	public async Task OnAlertRequested_OnNonStaThread_FaultsTaskInsteadOfLeavingItPending()
	{
		var arguments = new AlertArguments("Thread error", "Message", null, "OK");
#if NET10_0
		await Task.Run(() => RequestAlert(arguments));

		Assert.True(arguments.Result.Task.IsFaulted);
		await Assert.ThrowsAsync<InvalidOperationException>(() => arguments.Result.Task);
#else
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Task.Run(() => RequestAlert(arguments)));
#endif
	}

	static void RequestAlert(AlertArguments arguments)
	{
#if NET10_0
		var subscriptionType = typeof(Microsoft.Maui.Controls.Window).Assembly
			.GetType("Microsoft.Maui.Controls.Platform.IAlertManagerSubscription")
			?? throw new InvalidOperationException("MAUI 10 dialog subscription contract was not found.");
		var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		WPFAlertManagerSubscription.Register(services);
		using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
			.BuildServiceProvider(services);
		var subscription = provider.GetService(subscriptionType)
			?? throw new InvalidOperationException("The WPF dialog subscription was not registered.");
		subscriptionType.GetMethod("OnAlertRequested")!.Invoke(subscription, [null, arguments]);
#else
		var result = WPFDialogService.ShowAlertAsync(null, arguments).GetAwaiter().GetResult();
		arguments.SetResult(result);
#endif
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
					if (parent.Current.ControlType == ControlType.TitleBar
						|| parent.Current.ControlType == ControlType.ScrollBar)
						return false;
				}
				return true;
			}).ToArray();
	}
}
