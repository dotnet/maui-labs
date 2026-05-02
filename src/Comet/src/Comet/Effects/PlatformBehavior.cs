using System;

namespace Comet
{
	public abstract class PlatformBehavior : Behavior
	{
		protected PlatformBehavior()
		{
		}
	}

	public abstract class PlatformBehavior<TView> : Behavior<TView> where TView : View
	{
		protected PlatformBehavior()
		{
		}
	}
}
