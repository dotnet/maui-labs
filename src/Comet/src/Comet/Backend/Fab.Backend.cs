#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend event routing for Fab. The node raises Clicked when the FAB is tapped.
	public partial class Fab
	{
		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			if (id == Backend.EventIds.Clicked)
				Clicked?.Invoke();
		}
	}
}
