#nullable enable
using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Labs.HotReload
{
	/// <summary>
	/// Registry that tracks live <see cref="IHotReloadAware"/> instances so they can be notified
	/// when their type is updated via .NET Hot Reload.
	/// </summary>
	/// <remarks>
	/// Instances are held via <see cref="WeakReference{T}"/> so registration does not prevent garbage collection.
	/// </remarks>
	public static class HotReloadRegistry
	{
		static readonly object _lock = new();
		static readonly Dictionary<Type, List<WeakReference<IHotReloadAware>>> _registry = new();

		/// <summary>
		/// Registers <paramref name="instance"/> so it receives <see cref="IHotReloadAware.OnHotReload"/>
		/// callbacks when its type (or any base type) is updated at runtime.
		/// </summary>
		public static void Register(IHotReloadAware instance)
		{
			if (instance is null)
				return;

			var type = instance.GetType();
			lock (_lock)
			{
				if (!_registry.TryGetValue(type, out var list))
					_registry[type] = list = new List<WeakReference<IHotReloadAware>>();

				// Compact dead references before adding to keep the list small.
				list.RemoveAll(static w => !w.TryGetTarget(out _));
				list.Add(new WeakReference<IHotReloadAware>(instance));
			}
		}

		/// <summary>
		/// Removes <paramref name="instance"/> from the registry.
		/// Safe to call even if the instance was never registered.
		/// </summary>
		public static void Unregister(IHotReloadAware instance)
		{
			if (instance is null)
				return;

			var type = instance.GetType();
			lock (_lock)
			{
				if (!_registry.TryGetValue(type, out var list))
					return;

				for (int i = list.Count - 1; i >= 0; i--)
				{
					if (!list[i].TryGetTarget(out var target))
					{
						// Remove dead references opportunistically.
						list.RemoveAt(i);
					}
					else if (ReferenceEquals(target, instance))
					{
						list.RemoveAt(i);
						break;
					}
				}
			}
		}

		/// <summary>
		/// Notifies all registered instances whose runtime type is, or derives from, any of the
		/// <paramref name="updatedTypes"/>. Called by <see cref="MauiMetadataUpdateHandler"/>.
		/// </summary>
		internal static void NotifyInstances(Type[] updatedTypes)
		{
			List<IHotReloadAware>? toNotify = null;

			lock (_lock)
			{
				foreach (var updatedType in updatedTypes)
				{
					foreach (var kvp in _registry)
					{
						// Notify instances whose concrete type is updatedType or a subtype of it.
						if (!updatedType.IsAssignableFrom(kvp.Key))
							continue;

						foreach (var weakRef in kvp.Value)
						{
							if (!weakRef.TryGetTarget(out var instance))
								continue;

							// Deduplicate by reference to avoid double-notifying when both a base
							// type and derived type appear in updatedTypes simultaneously.
							toNotify ??= new List<IHotReloadAware>();
							bool alreadyQueued = false;
							for (int i = 0; i < toNotify.Count; i++)
							{
								if (ReferenceEquals(toNotify[i], instance))
								{
									alreadyQueued = true;
									break;
								}
							}
							if (!alreadyQueued)
								toNotify.Add(instance);
						}
					}
				}
			}

			if (toNotify is null)
				return;

			foreach (var instance in toNotify)
			{
				try
				{
					instance.OnHotReload(updatedTypes);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[MauiHotReload] Error in OnHotReload for {instance.GetType()}: {ex}");
				}
			}
		}
	}
}
