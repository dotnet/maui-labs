using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.HotReload;

namespace Comet.HotReload;

/// <summary>
/// Comet-side hot reload registry that complements MAUI's helper so synthetic
/// replacement types used by tests and Comet's component pipeline can be
/// resolved consistently.
/// </summary>
public static class CometHotReloadHelper
{
	static readonly object Sync = new();
	static readonly Dictionary<string, Type> ReplacedViews = new();
	static readonly FieldInfo MauiCurrentViewsField = typeof(MauiHotReloadHelper).GetField("currentViews", BindingFlags.NonPublic | BindingFlags.Static);

	public static void RegisterReplacedView(string originalTypeName, Type replacementType)
	{
		if (string.IsNullOrWhiteSpace(originalTypeName) || replacementType is null)
			return;

		lock (Sync)
			ReplacedViews[originalTypeName] = replacementType;

		MauiHotReloadHelper.RegisterReplacedView(originalTypeName, replacementType);
	}

	internal static void Reset()
	{
		lock (Sync)
			ReplacedViews.Clear();
	}

	internal static bool IsReplacedView(IHotReloadableView currentView, IView compareView)
	{
		var currentType = currentView?.GetType();
		var compareType = compareView?.GetType();
		if (currentType is null || compareType is null)
			return false;

		lock (Sync)
		{
			if (ReplacedViews.TryGetValue(currentType.FullName, out var replacementType) &&
				replacementType == compareType)
			{
				return true;
			}

			if (ReplacedViews.TryGetValue(compareType.FullName, out replacementType) &&
				replacementType == currentType)
			{
				return true;
			}
		}

		return false;
	}

	internal static View CreateReplacement(View currentView)
	{
		if (currentView is null || !MauiHotReloadHelper.IsEnabled)
			return null;

		Type replacementType;
		lock (Sync)
		{
			if (!ReplacedViews.TryGetValue(currentView.GetType().FullName, out replacementType))
				return null;
		}

		var args = TryGetRegisteredArguments(currentView);
		return CreateReplacementInstance(replacementType, args);
	}

	static object[] TryGetRegisteredArguments(View currentView)
	{
		var currentViews = MauiCurrentViewsField?.GetValue(null) as IDictionary;
		return currentViews?[currentView] as object[] ?? Array.Empty<object>();
	}

	static View CreateReplacementInstance(Type replacementType, object[] args)
	{
		var constructors = replacementType?.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (constructors is null)
			return null;

		foreach (var constructor in constructors.OrderBy(c => c.GetParameters().Length))
		{
			var parameters = constructor.GetParameters();
			if (parameters.Length != (args?.Length ?? 0))
				continue;

			var isMatch = true;
			for (var i = 0; i < parameters.Length; i++)
			{
				var argument = args[i];
				if (argument is null)
				{
					if (parameters[i].ParameterType.IsValueType &&
						Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
					{
						isMatch = false;
						break;
					}

					continue;
				}

				if (!parameters[i].ParameterType.IsInstanceOfType(argument))
				{
					isMatch = false;
					break;
				}
			}

			if (isMatch)
				return constructor.Invoke(args) as View;
		}

		return null;
	}
}
