using System;

namespace Comet.Styles
{
	/// <summary>
	/// Strongly-typed shape (corner radius) token identifiers.
	/// Each field resolves a double value from the active Theme's ShapeTokenSet.
	/// </summary>
	public static class ShapeTokens
	{
		public static readonly Token<double> None =
			new("theme.shape.none", theme => theme.Shapes?.None ?? 0, "None", 0);
		public static readonly Token<double> ExtraSmall =
			new("theme.shape.xs", theme => theme.Shapes?.ExtraSmall ?? 4, "Extra Small", 4);
		public static readonly Token<double> Small =
			new("theme.shape.sm", theme => theme.Shapes?.Small ?? 8, "Small", 8);
		public static readonly Token<double> Medium =
			new("theme.shape.md", theme => theme.Shapes?.Medium ?? 12, "Medium", 12);
		public static readonly Token<double> Large =
			new("theme.shape.lg", theme => theme.Shapes?.Large ?? 16, "Large", 16);
		public static readonly Token<double> ExtraLarge =
			new("theme.shape.xl", theme => theme.Shapes?.ExtraLarge ?? 28, "Extra Large", 28);
		public static readonly Token<double> Full =
			new("theme.shape.full", theme => theme.Shapes?.Full ?? 9999, "Full (Pill)", 9999);
	}
}
