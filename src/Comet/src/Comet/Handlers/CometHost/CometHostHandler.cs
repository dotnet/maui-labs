using Microsoft.Maui.Handlers;

namespace Comet.Handlers;

/// <summary>
/// Cross-platform handler registration for CometHost.
/// Platform-specific implementations create a container view and
/// render the Comet View's platform representation inside it.
/// </summary>
public partial class CometHostHandler
{
	public static IPropertyMapper<CometHost, CometHostHandler> CometHostMapper =
		new PropertyMapper<CometHost, CometHostHandler>(ViewHandler.ViewMapper);
}
