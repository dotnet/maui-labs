using System;
using System.Collections.Generic;
using System.Linq;

namespace Comet
{
	public static class VisualStateManager
	{
		public const string CommonStates = "CommonStates";
		public static readonly string NormalState = "Normal";
		public static readonly string DisabledState = "Disabled";
		public static readonly string FocusedState = "Focused";
		public static readonly string PointerOverState = "PointerOver";

		static readonly System.Runtime.CompilerServices.ConditionalWeakTable<View, List<VisualStateGroup>> _visualStateGroups = new();

		public static bool GoToState(View view, string stateName)
		{
			if (view is null || string.IsNullOrEmpty(stateName))
				return false;

			if (!_visualStateGroups.TryGetValue(view, out var groups))
				return false;

			foreach (var group in groups)
			{
				var state = group.States.FirstOrDefault(s => s.Name == stateName);
				if (state is not null)
				{
					foreach (var setter in state.Setters)
					{
						view.SetEnvironment(setter.Property, setter.Value, false);
					}
					return true;
				}
			}

			return false;
		}

		public static void SetVisualStateGroups(View view, List<VisualStateGroup> groups)
		{
			if (view is null)
				return;

			_visualStateGroups.AddOrUpdate(view, groups);
		}

		public static List<VisualStateGroup> GetVisualStateGroups(View view)
		{
			if (view is null)
				return null;

			_visualStateGroups.TryGetValue(view, out var groups);
			return groups;
		}

		public static void ClearVisualStateGroups(View view)
		{
			if (view is not null)
				_visualStateGroups.Remove(view);
		}
	}

	public class VisualState
	{
		public string Name { get; set; }
		public List<Setter> Setters { get; } = new List<Setter>();
	}

	public class VisualStateGroup
	{
		public string Name { get; set; }
		public List<VisualState> States { get; } = new List<VisualState>();
	}

	public class Setter
	{
		public string Property { get; set; }
		public object Value { get; set; }
	}
}
