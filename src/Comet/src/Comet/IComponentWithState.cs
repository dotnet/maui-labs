using System;

namespace Comet
{
	/// <summary>
	/// Implemented by Component subclasses that carry state objects.
	/// Enables hot reload to transfer state between old and new component instances.
	/// </summary>
	public interface IComponentWithState
	{
		/// <summary>
		/// Returns the component's state object for serialization or transfer.
		/// </summary>
		object GetStateObject();

		/// <summary>
		/// Transfers state from a source component during hot reload.
		/// The source should be the same component type being replaced.
		/// </summary>
		void TransferStateFrom(IComponentWithState source);
	}
}
