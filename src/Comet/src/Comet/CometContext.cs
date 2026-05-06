using Microsoft.Maui;

namespace Comet;

/// <summary>
/// Provides ambient access to the current <see cref="IMauiContext"/>.
/// </summary>
public static class CometContext
{
	public static IMauiContext Current { get; internal set; }
}
