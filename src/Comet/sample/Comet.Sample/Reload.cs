#nullable enable
#if DEBUG
using System;
using Microsoft.Maui.Hosting;

namespace Comet
{
	/// <summary>
	/// Hot reload is now built into .NET MAUI.
	/// This stub remains for API compatibility.
	/// Use MAUI's built-in Hot Reload instead.
	/// </summary>
	public static partial class Reload
	{
		public static MauiAppBuilder EnableHotReload(this MauiAppBuilder builder, string? ideIp = null, int idePort = 9988)
		{
			// MAUI's built-in Hot Reload handles this automatically.
			return builder;
		}
	}
}
#endif