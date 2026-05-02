using System;
using System.Collections.Generic;

namespace Comet
{
	public interface IContainerView : IView, IVisualTreeElement
	{
		IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren()
		{
			var children = GetChildren();
			if (children is null || children.Count == 0)
				return Array.Empty<IVisualTreeElement>();

			var visualChildren = new List<IVisualTreeElement>(children.Count);
			foreach (var child in children)
			{
				if (child is IVisualTreeElement visualChild)
					visualChildren.Add(visualChild);
			}

			return visualChildren.Count == 0 ? Array.Empty<IVisualTreeElement>() : visualChildren;
		}
		IVisualTreeElement IVisualTreeElement.GetVisualParent() => this.Parent as IVisualTreeElement; 
		IReadOnlyList<View> GetChildren();
	}
}
