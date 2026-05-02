using System;

namespace Comet.Styles
{
	/// <summary>
	/// Base interface for all control style protocols.
	/// Each control type defines a concrete TConfiguration struct
	/// that carries the control's interactive state.
	/// </summary>
	public interface IControlStyle<TControl, TConfiguration>
		where TControl : View
		where TConfiguration : struct
	{
		/// <summary>
		/// Given the control's current state, return the appearance modifier.
		/// Called during the view's render cycle.
		/// </summary>
		ViewModifier Resolve(TConfiguration configuration);
	}
}
