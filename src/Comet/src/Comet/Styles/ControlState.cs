using System;
namespace Comet
{
	[Flags]
	public enum ControlState
	{
		Default = 0,
		Pressed = 1,
		Hovered = 2,
		Focused = 4,
		Disabled = 8,
		Dragging = 16,
	}
}
