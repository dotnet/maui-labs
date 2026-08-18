using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers.WPF;

namespace HandlerTests;

public class ShellHandlerTests
{
	[Fact]
	public void ToggleFlyout_WhenStateChanges_NotifiesShellPropertyBridge()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;

			container.ToggleFlyout(true);
			container.ToggleFlyout(true);
			container.ToggleFlyout(false);

			Assert.Equal([true, false], changes);
		});
	}

	[Fact]
	public void ToggleFlyout_WhenLocked_ReportsForcedOpenAndDoesNotClose()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;
			container.SetFlyoutBehavior(FlyoutBehavior.Locked);

			container.ToggleFlyout(false);

			Assert.Equal([true], changes);
		});
	}

	[Fact]
	public void ToggleFlyout_WhenDisabled_DoesNotOpen()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;
			container.SetFlyoutBehavior(FlyoutBehavior.Disabled);

			container.ToggleFlyout(true);

			Assert.Empty(changes);
		});
	}
}
