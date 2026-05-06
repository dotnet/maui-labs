using System;
using Microsoft.Maui;

namespace Comet.Styles
{
	/// <summary>
	/// Strongly-typed typography token identifiers following Material 3 type scale.
	/// Each field resolves a FontSpec from the active Theme's TypographyTokenSet.
	/// </summary>
	public static class TypographyTokens
	{
		// Display
		public static readonly Token<FontSpec> DisplayLarge =
			new("theme.type.displayLarge", theme => theme.Typography?.DisplayLarge ?? default, "Display Large");
		public static readonly Token<FontSpec> DisplayMedium =
			new("theme.type.displayMedium", theme => theme.Typography?.DisplayMedium ?? default, "Display Medium");
		public static readonly Token<FontSpec> DisplaySmall =
			new("theme.type.displaySmall", theme => theme.Typography?.DisplaySmall ?? default, "Display Small");

		// Headline
		public static readonly Token<FontSpec> HeadlineLarge =
			new("theme.type.headlineLarge", theme => theme.Typography?.HeadlineLarge ?? default, "Headline Large");
		public static readonly Token<FontSpec> HeadlineMedium =
			new("theme.type.headlineMedium", theme => theme.Typography?.HeadlineMedium ?? default, "Headline Medium");
		public static readonly Token<FontSpec> HeadlineSmall =
			new("theme.type.headlineSmall", theme => theme.Typography?.HeadlineSmall ?? default, "Headline Small");

		// Title
		public static readonly Token<FontSpec> TitleLarge =
			new("theme.type.titleLarge", theme => theme.Typography?.TitleLarge ?? default, "Title Large");
		public static readonly Token<FontSpec> TitleMedium =
			new("theme.type.titleMedium", theme => theme.Typography?.TitleMedium ?? default, "Title Medium");
		public static readonly Token<FontSpec> TitleSmall =
			new("theme.type.titleSmall", theme => theme.Typography?.TitleSmall ?? default, "Title Small");

		// Body
		public static readonly Token<FontSpec> BodyLarge =
			new("theme.type.bodyLarge", theme => theme.Typography?.BodyLarge ?? default, "Body Large");
		public static readonly Token<FontSpec> BodyMedium =
			new("theme.type.bodyMedium", theme => theme.Typography?.BodyMedium ?? default, "Body Medium");
		public static readonly Token<FontSpec> BodySmall =
			new("theme.type.bodySmall", theme => theme.Typography?.BodySmall ?? default, "Body Small");

		// Label
		public static readonly Token<FontSpec> LabelLarge =
			new("theme.type.labelLarge", theme => theme.Typography?.LabelLarge ?? default, "Label Large");
		public static readonly Token<FontSpec> LabelMedium =
			new("theme.type.labelMedium", theme => theme.Typography?.LabelMedium ?? default, "Label Medium");
		public static readonly Token<FontSpec> LabelSmall =
			new("theme.type.labelSmall", theme => theme.Typography?.LabelSmall ?? default, "Label Small");
	}
}
