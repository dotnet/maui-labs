using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.HotReload;

namespace Comet.Internal
{
	public static class Extensions
	{
		public static T GetValueOfType<T>(this object obj)
		{
			if (obj is null)
				return default;

			if (obj is T t)
				return t;
			try
			{
				return (T)Convert.ChangeType(obj, typeof(T));
			}
			catch
			{
				return default;
			}
		}

		public static View FindViewById(this View view, string id)
		{
			if(view is null)
				return MauiHotReloadHelper.ActiveViews.OfType<View>().Select(x=> x.FindViewById(id)).FirstOrDefault();
			if (view.Id == id)
				return view;
			if(view is IContainerView ic)
				return ic.GetChildren().Select(x => x.FindViewById(id)).FirstOrDefault();
			return null;
		}

		public static Func<View> GetBody(this View view)
		{
			// Match [Body] attribute by name, not by type identity, because the user's
			// dynamically-loaded assembly may reference a different Comet assembly (NuGet)
			// than the companion app (project reference).
			var bodyMethod = view.GetType().GetMethods(
					System.Reflection.BindingFlags.NonPublic |
					System.Reflection.BindingFlags.Public |
					System.Reflection.BindingFlags.Instance)
				.FirstOrDefault(m => m.GetCustomAttributes(false)
					.Any(a => a.GetType().FullName == "Comet.BodyAttribute"));

			if (bodyMethod is null)
			{
				// Fall back to type-based matching (same-assembly case)
				bodyMethod = view.GetType().GetDeepMethodInfo(typeof(BodyAttribute));
			}

			if (bodyMethod is not null)
				return (Func<View>)Delegate.CreateDelegate(typeof(Func<View>), view, bodyMethod.Name);
			return null;
		}
		public static void ResetGlobalEnvironment(this View view) => View.Environment.Clear();

		//public static void DisposeAllViews(this View view) => View.ActiveViews.Clear();

		public static View GetView(this View view) => view.GetView();

		//public static Dictionary<Type, Type> GetAllRenderers(this Registrar<IFrameworkElement, IViewHandler> registar) => registar._handler;

		public static T SetParent<T>(this T view, View parent) where T : View
		{
			if (view is not null)
				view.Parent = parent;
			return view;
		}

		public static T FindParentOfType<T>(this View view)
		{
			if (view is null)
				return default;
			if (view.BuiltView is T bt)
			{
				return bt;
			}
			if (view is T t)
				return t;
			return view.Parent.FindParentOfType<T>() ?? default;
		}
		public static NavigationView FindNavigation (this View view)
		{
			if (view is null)
				return default;
			var v = view.GetView();
			if(v.Navigation is not null)
				return v.Navigation;

			if (v is ContentView cv)
				return cv.Content?.FindNavigation();

			return null;
		}
	}
}
