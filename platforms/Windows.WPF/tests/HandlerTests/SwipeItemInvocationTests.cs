using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers.WPF;

namespace HandlerTests;

public class SwipeItemInvocationTests
{
	[Fact]
	public void InvokeSwipeItem_ExecutesCommandAndRaisesEventsOnce()
	{
		var executed = 0;
		var invoked = 0;
		var clicked = 0;
		var item = new SwipeItem { Command = new Command(() => executed++) };
		item.Invoked += (_, _) => invoked++;
		item.Clicked += (_, _) => clicked++;

		SwipeViewHandler.InvokeSwipeItem(item);

		Assert.Equal(1, executed);
		Assert.Equal(1, invoked);
		Assert.Equal(1, clicked);
	}

	[Fact]
	public void InvokeSwipeItem_DoesNotExecuteACommandThatCannotExecute()
	{
		var executed = 0;
		var invoked = 0;
		var item = new SwipeItem { Command = new Command(() => executed++, () => false) };
		item.Invoked += (_, _) => invoked++;

		SwipeViewHandler.InvokeSwipeItem(item);

		Assert.Equal(0, executed);
		Assert.Equal(1, invoked);
	}
}
