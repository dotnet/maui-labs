#nullable enable
using System;
#if !NETSTANDARD
using Microsoft.Maui.HotReload;
#endif

namespace Microsoft.Maui.Labs.HotReload
{
	/// <summary>
	/// Handles .NET Hot Reload metadata update notifications, forwarding them to registered
	/// <see cref="IHotReloadAware"/> instances and the MAUI view-level hot reload infrastructure.
	/// </summary>
	public static class MauiMetadataUpdateHandler
	{
		/// <summary>
		/// Called by the .NET Hot Reload host after metadata has been applied.
		/// Notifies all registered <see cref="IHotReloadAware"/> instances whose types were updated,
		/// then forwards to MAUI's <c>MauiHotReloadHelper</c> for view-level reload.
		/// </summary>
		/// <remarks>
		/// This method is invoked only during hot reload sessions (debug/development builds).
		/// Trimming and hot reload are mutually exclusive, so trim-compatibility warnings are suppressed.
		/// </remarks>
#if !NETSTANDARD
		[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Hot Reload is not compatible with trimming.")]
		[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Hot Reload is not compatible with AOT.")]
#endif
		public static void UpdateApplication(Type[] types)
		{
			HotReloadRegistry.NotifyInstances(types);
#if !NETSTANDARD
			MauiHotReloadHelper.UpdateApplication(types);
#endif
		}

		/// <summary>
		/// Called by the .NET Hot Reload host before metadata is applied, to clear any caches.
		/// </summary>
		public static void ClearCache(Type[] types)
		{
#if !NETSTANDARD
			MauiHotReloadHelper.ClearCache(types);
#endif
		}
	}
}
