using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.HotReload;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Comet.HotReload.CometMetadataUpdateHandler))]

namespace Comet.HotReload;

#nullable enable

/// <summary>
/// Bridges .NET Hot Reload notifications into Comet's view update pipeline
/// via MauiHotReloadHelper.
/// </summary>
internal static class CometMetadataUpdateHandler
{
	/// <summary>
	/// Called by the runtime when metadata for a type is updated.
	/// Registers the updated type with MAUI's hot reload system so
	/// Comet's diff algorithm recognizes it as a replacement.
	/// </summary>
	internal static void UpdateType(Type[]? updatedTypes)
	{
		if (updatedTypes is null)
			return;

		foreach (var type in updatedTypes)
		{
			// Only register types that could be Comet views
			if (type.FullName is not null)
			{
				CometHotReloadHelper.RegisterReplacedView(type.FullName, type);
			}
		}
	}

	/// <summary>
	/// Called after all type updates in a batch have been applied.
	/// Triggers Comet's view tree rebuild + diff cycle.
	/// </summary>
	internal static void UpdateApplication(Type[]? updatedTypes)
	{
		MauiHotReloadHelper.TriggerReload();
	}

	/// <summary>
	/// Called when cached data may be stale. No Comet-specific cache to clear.
	/// </summary>
	internal static void ClearCache(Type[]? updatedTypes)
	{
		// No Comet-specific caches to clear.
		// MauiHotReloadHelper manages its own state.
	}
}
