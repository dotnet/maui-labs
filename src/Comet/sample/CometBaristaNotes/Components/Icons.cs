using FontIcon = Comet.FontImageSource;

namespace CometBaristaNotes.Components;

/// <summary>
/// MaterialSymbols icon codepoints, coffee-icons glyphs, and themed icon factories.
/// Codepoints sourced from Resources/Fonts/MaterialSymbolsFont.cs.
/// </summary>
public static class Icons
{
	public const string FontFamily = "MaterialIcons";
	public const string CoffeeFontFamily = "coffee-icons";

	// ── Navigation & Actions ─────────────────────────────────────────
	public const string Settings = "\ue8b8";
	public const string Add = "\ue145";
	public const string AddCircle = "\ue147";
	public const string Remove = "\ue15b";
	public const string Delete = "\ue872";
	public const string DeleteForever = "\ue92b";
	public const string Edit = "\ue150";
	public const string Close = "\ue14c";
	public const string Check = "\ue5ca";
	public const string CheckCircle = "\ue86c";
	public const string ChevronRight = "\ue409";
	public const string ChevronLeft = "\ue408";
	public const string ArrowBack = "\ue5c4";
	public const string MoreVert = "\ue5d4";
	public const string Search = "\ue8b6";

	// ── App-specific ─────────────────────────────────────────────────
	public const string Coffee = "\uefef";
	public const string Factory = "\uebbc";
	public const string Globe = "\ue64c";
	public const string CalendarToday = "\ue935";
	public const string Person = "\ue7fd";
	public const string AccountCircle = "\ue853";
	public const string Assignment = "\ue85d";
	public const string Feed = "\uf009";
	public const string FilterList = "\ue152";
	public const string FilterListOff = "\ueb57";
	public const string Mic = "\ue029";
	public const string PhotoCamera = "\ue3b0";
	public const string MagicButton = "\uf136";
	public const string Build = "\ue869";
	public const string Info = "\ue88e";
	public const string Warning = "\ue002";
	public const string Error = "\ue000";
	public const string Inventory = "\ue179";

	// ── Theme mode ───────────────────────────────────────────────────
	public const string LightMode = "\ue518";
	public const string DarkMode = "\ue51c";
	public const string BrightnessAuto = "\ue1ab";

	// ── Star ratings ─────────────────────────────────────────────────
	public const string Star = "\ue838";
	public const string StarHalf = "\ue839";
	public const string StarRate = "\uf0ec";

	// ── Rating sentiment icons (matching original) ───────────────────
	public const string SentimentVeryDissatisfied = "\ue814";
	public const string SentimentDissatisfied = "\ue811";
	public const string SentimentNeutral = "\ue812";
	public const string SentimentSatisfied = "\ue0ed";
	public const string SentimentVerySatisfied = "\ue815";

	// ── Coffee-icons font glyphs ─────────────────────────────────────
	public const string CupIn = "u";
	public const string CupOut = "t";
	public const string Machine = "s";

	// ── Icon factories ───────────────────────────────────────────────
	// Return Comet.FontImageSource for use in tabs, toolbars, and buttons.
	// Color is theme-aware — pass the appropriate Theme color.

	public static FontIcon MaterialIcon(string glyph, Color color, double size = CoffeeColors.IconSizeMedium)
		=> new(FontFamily, glyph, size, color);

	public static FontIcon CoffeeIcon(string glyph, Color color, double size = CoffeeColors.IconSizeMedium)
		=> new(CoffeeFontFamily, glyph, size, color);

	// Convenience factories for common icons using primary text color
	public static FontIcon CoffeeLabIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Coffee, CoffeeColors.Primary, size);

	public static FontIcon SettingsIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Settings, CoffeeColors.TextPrimary, size);

	public static FontIcon AddIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Add, CoffeeColors.Primary, size);

	public static FontIcon DeleteIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Delete, CoffeeColors.Error, size);

	public static FontIcon EditIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Edit, CoffeeColors.TextPrimary, size);

	public static FontIcon SearchIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Search, CoffeeColors.TextSecondary, size);

	public static FontIcon FilterIcon(bool active, double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(active ? FilterList : FilterListOff, active ? CoffeeColors.Primary : CoffeeColors.TextMuted, size);

	public static FontIcon WarningIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Warning, CoffeeColors.Warning, size);

	public static FontIcon CheckIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(CheckCircle, CoffeeColors.Success, size);

	public static FontIcon InfoIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Info, CoffeeColors.TextSecondary, size);

	public static FontIcon MicIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Mic, CoffeeColors.Primary, size);

	public static FontIcon MagicIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(MagicButton, CoffeeColors.Primary, size);

	public static FontIcon PersonIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Person, CoffeeColors.TextPrimary, size);

	public static FontIcon FeedIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Feed, CoffeeColors.TextPrimary, size);

	public static FontIcon FactoryIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Factory, CoffeeColors.TextPrimary, size);

	public static FontIcon GlobeIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Globe, CoffeeColors.TextPrimary, size);

	public static FontIcon CalendarIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(CalendarToday, CoffeeColors.TextPrimary, size);

	public static FontIcon InventoryIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Inventory, CoffeeColors.TextPrimary, size);

	public static FontIcon BackIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(ArrowBack, CoffeeColors.TextPrimary, size);

	public static FontIcon MoreIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(MoreVert, CoffeeColors.TextPrimary, size);

	public static FontIcon CloseIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Close, CoffeeColors.TextPrimary, size);

	public static FontIcon StarFilledIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(Star, CoffeeColors.StarFilled, size);

	public static FontIcon StarHalfIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(StarHalf, CoffeeColors.StarFilled, size);

	public static FontIcon StarEmptyIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(StarRate, CoffeeColors.StarEmpty, size);

	public static FontIcon LightModeIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(LightMode, CoffeeColors.TextPrimary, size);

	public static FontIcon DarkModeIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(DarkMode, CoffeeColors.TextPrimary, size);

	public static FontIcon AutoBrightnessIcon(double size = CoffeeColors.IconSizeMedium)
		=> MaterialIcon(BrightnessAuto, CoffeeColors.TextPrimary, size);

	public static FontIcon EspressoMachineIcon(double size = CoffeeColors.IconSizeMedium)
		=> CoffeeIcon(Machine, CoffeeColors.Primary, size);
}
