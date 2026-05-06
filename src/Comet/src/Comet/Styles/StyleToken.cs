using System;

namespace Comet.Styles
{
	/// <summary>
	/// Provides a unique environment key for per-control-type style registration.
	/// </summary>
	public static class StyleToken<TControl> where TControl : View
	{
		public static readonly string Key = $"Comet.ControlStyle.{typeof(TControl).Name}";
	}
}
