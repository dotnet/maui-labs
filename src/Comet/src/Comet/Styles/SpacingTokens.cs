using System;

namespace Comet.Styles
{
	/// <summary>
	/// Strongly-typed spacing token identifiers.
	/// Each field resolves a double value from the active Theme's SpacingTokenSet.
	/// </summary>
	public static class SpacingTokens
	{
		public static readonly Token<double> None =
			new("theme.spacing.none", theme => theme.Spacing?.None ?? 0, "None", 0);
		public static readonly Token<double> ExtraSmall =
			new("theme.spacing.xs", theme => theme.Spacing?.ExtraSmall ?? 4, "Extra Small", 4);
		public static readonly Token<double> Small =
			new("theme.spacing.sm", theme => theme.Spacing?.Small ?? 8, "Small", 8);
		public static readonly Token<double> Medium =
			new("theme.spacing.md", theme => theme.Spacing?.Medium ?? 16, "Medium", 16);
		public static readonly Token<double> Large =
			new("theme.spacing.lg", theme => theme.Spacing?.Large ?? 24, "Large", 24);
		public static readonly Token<double> ExtraLarge =
			new("theme.spacing.xl", theme => theme.Spacing?.ExtraLarge ?? 32, "Extra Large", 32);
	}
}
