using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;
using CometBorder = Comet.Border;
using CometButton = Comet.Button;
using CometSlider = Comet.Slider;
using CometToggle = Comet.Toggle;

namespace CometBaristaNotes.Styles;

/// <summary>
/// Reusable ViewModifier subclasses for the coffee theme.
/// Colors resolve dynamically from CoffeeColors (which delegates to ThemeManager)
/// so modifiers respond to light/dark theme changes.
/// </summary>

public class TypographyModifier : ViewModifier
{
	readonly string _fontFamily;
	readonly double _fontSize;
	readonly FontWeight? _fontWeight;
	readonly Func<Color> _color;

	public TypographyModifier(string fontFamily, double fontSize, Func<Color> color, FontWeight? fontWeight = null)
	{
		_fontFamily = fontFamily;
		_fontSize = fontSize;
		_color = color;
		_fontWeight = fontWeight;
	}

	public override View Apply(View view)
	{
		view.FontFamily(_fontFamily)
			.FontSize(_fontSize)
			.Color(_color());

		if (_fontWeight.HasValue)
			view.FontWeight(_fontWeight.Value);

		return view;
	}
}

public class TextColorModifier : ViewModifier
{
	readonly Color _color;

	public TextColorModifier(Color color)
	{
		_color = color;
	}

	public override View Apply(View view) => view.Color(_color);
}

public class BackgroundColorModifier : ViewModifier
{
	readonly Color _color;

	public BackgroundColorModifier(Color color)
	{
		_color = color;
	}

	public override View Apply(View view) => view.Background(_color);
}

public class StrokeColorModifier : ViewModifier
{
	readonly Color _color;

	public StrokeColorModifier(Color color)
	{
		_color = color;
	}

	public override View Apply(View view)
	{
		if (view is CometBorder border)
			border.StrokeColor(_color);
		else if (view is CometButton button)
			button.StrokeColor(_color);

		return view;
	}
}

public class CornerRadiusModifier : ViewModifier
{
	readonly float _radius;

	public CornerRadiusModifier(float radius)
	{
		_radius = radius;
	}

	public override View Apply(View view)
	{
		if (view is CometBorder border)
			border.CornerRadius(_radius);
		else if (view is CometButton button)
			button.CornerRadius((int)Math.Round(_radius));

		return view;
	}
}

public class ClipShapeModifier : ViewModifier
{
	readonly IShape _shape;

	public ClipShapeModifier(IShape shape)
	{
		_shape = shape;
	}

	public override View Apply(View view) => view.ClipShape(_shape);
}

public class FrameModifier : ViewModifier
{
	readonly float? _width;
	readonly float? _height;

	public FrameModifier(float? width, float? height)
	{
		_width = width;
		_height = height;
	}

	public override View Apply(View view) => view.Frame(_width, _height);
}

public class DividerModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Frame(height: 1)
		.FillHorizontal();
}

public class RatingBarModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Frame(height: 8)
		.ClipShape(new RoundedRectangle(4));
}

public class SurfaceCardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(CoffeeColors.Surface)
		.RoundedBorder(radius: CoffeeColors.RadiusCard, color: CoffeeColors.Outline, strokeSize: 1);
}

public class SurfaceVariantCardModifier : ViewModifier
{
	public override View Apply(View view)
	{
		view.Background(CoffeeColors.SurfaceVariant)
			.ClipShape(new RoundedRectangle(CoffeeColors.RadiusCard));

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class CardSurfaceModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(CoffeeColors.CardBackground)
		.RoundedBorder(radius: CoffeeColors.RadiusCard, color: CoffeeColors.CardStroke, strokeSize: 1);
}

public class ShotCardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(CoffeeColors.Surface)
		.RoundedBorder(radius: 8, color: CoffeeColors.Outline, strokeSize: 1)
		.Padding(new Thickness(10));
}

public class SurfaceVariantFieldModifier : ViewModifier
{
	public override View Apply(View view)
	{
		view.Background(CoffeeColors.SurfaceVariant)
			.ClipShape(new RoundedRectangle(CoffeeColors.RadiusPill));

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class TransparentFieldModifier : ViewModifier
{
	public override View Apply(View view) => view.Background(Colors.Transparent);
}

public class CardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.SurfaceCard)
		.Padding(new Thickness(CoffeeColors.SpacingM));
}

public class ListCardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.SurfaceCard)
		.Padding(new Thickness(CoffeeColors.SpacingM));
}

public class FormFieldModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.SurfaceVariantField)
		.Frame(height: CoffeeColors.FormFieldHeight)
		.Padding(new Thickness(CoffeeColors.SpacingM, 0));
}

public class FormTextFieldModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.FormValue)
		.Modifier(CoffeeModifiers.TransparentField)
		.VerticalTextAlignment(TextAlignment.Center)
		.VerticalLayoutAlignment(LayoutAlignment.Center)
		.Margin(new Thickness(CoffeeColors.SpacingM, 0));
}

public class FormPickerModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.FormValue)
		.Modifier(CoffeeModifiers.TransparentField)
		.VerticalTextAlignment(TextAlignment.Center)
		.VerticalLayoutAlignment(LayoutAlignment.Center)
		.Margin(new Thickness(CoffeeColors.SpacingM, 0));
}

public class FormEditorModifier : ViewModifier
{
	readonly float _height;
	readonly Thickness _margin;

	public FormEditorModifier(float height, Thickness margin)
	{
		_height = height;
		_margin = margin;
	}

	public override View Apply(View view) => view
		.Modifier(CoffeeModifiers.FormValue)
		.Modifier(CoffeeModifiers.TransparentField)
		.Frame(height: _height)
		.Margin(_margin);
}

public class FormEditorContainerModifier : ViewModifier
{
	public override View Apply(View view)
	{
		view.Background(CoffeeColors.SurfaceVariant)
			.ClipShape(new RoundedRectangle(CoffeeColors.RadiusEditor));

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class SliderModifier : ViewModifier
{
	readonly Color _minimum;
	readonly Color _maximum;
	readonly Color? _thumb;
	readonly Thickness? _margin;

	public SliderModifier(Color minimum, Color maximum, Color? thumb = null, Thickness? margin = null)
	{
		_minimum = minimum;
		_maximum = maximum;
		_thumb = thumb;
		_margin = margin;
	}

	public override View Apply(View view)
	{
		if (view is CometSlider slider)
		{
			slider.MinimumTrackColor(_minimum)
				.MaximumTrackColor(_maximum);

			if (_thumb != null)
				slider.ThumbColor(_thumb);
		}

		if (_margin.HasValue)
			view.Margin(_margin.Value);

		return view;
	}
}

public class ToggleModifier : ViewModifier
{
	readonly Color _onColor;

	public ToggleModifier(Color onColor)
	{
		_onColor = onColor;
	}

	public override View Apply(View view)
	{
		if (view is CometToggle toggle)
			toggle.OnColor(_onColor);

		return view;
	}
}

public class IconModifier : ViewModifier
{
	readonly double _size;
	readonly Color? _color;

	public IconModifier(double size, Color? color = null)
	{
		_size = size;
		_color = color;
	}

	public override View Apply(View view)
	{
		view.FontFamily(Icons.FontFamily)
			.FontSize(_size)
			.HorizontalTextAlignment(TextAlignment.Center)
			.VerticalTextAlignment(TextAlignment.Center);

		if (_color != null)
			view.Color(_color);

		return view;
	}
}

public class IconFontModifier : ViewModifier
{
	readonly string _fontFamily;
	readonly double _size;
	readonly Color? _color;

	public IconFontModifier(string fontFamily, double size, Color? color = null)
	{
		_fontFamily = fontFamily;
		_size = size;
		_color = color;
	}

	public override View Apply(View view)
	{
		view.FontFamily(_fontFamily)
			.FontSize(_size)
			.HorizontalTextAlignment(TextAlignment.Center)
			.VerticalTextAlignment(TextAlignment.Center);

		if (_color != null)
			view.Color(_color);

		return view;
	}
}

public class EmojiChipModifier : ViewModifier
{
	readonly Color _color;

	public EmojiChipModifier(Color color)
	{
		_color = color;
	}

	public override View Apply(View view) => view
		.FontFamily(Icons.FontFamily)
		.FontSize(32)
		.Color(_color)
		.HorizontalTextAlignment(TextAlignment.Center)
		.VerticalTextAlignment(TextAlignment.Center)
		.Frame(width: 48f, height: 48f);
}

public class AvatarCircleModifier : ViewModifier
{
	readonly float _size;

	public AvatarCircleModifier(float size)
	{
		_size = size;
	}

	public override View Apply(View view) => view
		.Frame(width: _size, height: _size)
		.ClipShape(new Ellipse());
}

public class AvatarBorderModifier : ViewModifier
{
	readonly float _size;
	readonly float _radius;
	readonly Color _background;
	readonly Color _strokeColor;
	readonly float _strokeThickness;

	public AvatarBorderModifier(float size, float radius, Color background, Color strokeColor, float strokeThickness)
	{
		_size = size;
		_radius = radius;
		_background = background;
		_strokeColor = strokeColor;
		_strokeThickness = strokeThickness;
	}

	public override View Apply(View view)
	{
		if (view is CometBorder border)
		{
			border.CornerRadius(_radius);
			border.StrokeColor(_strokeColor);
			border.StrokeThickness(_strokeThickness);
		}

		view.Background(_background)
			.Frame(width: _size, height: _size);

		return view;
	}
}

public class ErrorCardModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(CoffeeColors.Error.WithAlpha(0.1f))
		.RoundedBorder(radius: CoffeeColors.RadiusCard, color: CoffeeColors.Error, strokeSize: 1);
}

public class OutlinePillModifier : ViewModifier
{
	readonly Color _strokeColor;

	public OutlinePillModifier(Color strokeColor)
	{
		_strokeColor = strokeColor;
	}

	public override View Apply(View view) => view
		.Background(Colors.Transparent)
		.RoundedBorder(radius: CoffeeColors.RadiusPill, color: _strokeColor, strokeSize: 1);
}

public class PillChipModifier : ViewModifier
{
	readonly Color _background;

	public PillChipModifier(Color background)
	{
		_background = background;
	}

	public override View Apply(View view)
	{
		view.Background(_background)
			.ClipShape(new RoundedRectangle(CoffeeColors.RadiusPill));

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class FloatingActionButtonModifier : ViewModifier
{
	public override View Apply(View view)
	{
		view.Background(CoffeeColors.Primary)
			.ClipShape(new Ellipse())
			.Frame(width: 56, height: 56)
			.Shadow(Colors.Black.WithAlpha(0.3f), radius: 8, x: 0, y: 4);

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class ChatBubbleModifier : ViewModifier
{
	readonly Color _background;

	public ChatBubbleModifier(Color background)
	{
		_background = background;
	}

	public override View Apply(View view)
	{
		view.Background(_background)
			.ClipShape(new RoundedRectangle(CoffeeColors.RadiusCard));

		if (view is CometBorder border)
			border.StrokeThickness(0);

		return view;
	}
}

public class PageContainerModifier : ViewModifier
{
	public override View Apply(View view) => view
		.Background(CoffeeColors.Background)
		.IgnoreSafeArea();
}

public class SectionHeaderModifier : ViewModifier
{
	public override View Apply(View view) => view
		.FontFamily(CoffeeColors.FontSemibold)
		.FontSize(13)
		.FontWeight(FontWeight.Bold)
		.Color(CoffeeColors.TextSecondary)
		.Margin(new Thickness(0, CoffeeColors.SpacingM, 0, CoffeeColors.SpacingXS));
}

public class PrimaryButtonModifier : ViewModifier
{
	public override View Apply(View view) => view
		.FontFamily(CoffeeColors.FontSemibold)
		.FontSize(14)
		.Background(CoffeeColors.Primary)
		.Color(Colors.White)
		.ClipShape(new RoundedRectangle(8))
		.Frame(height: 50f)
		.FontWeight(FontWeight.Bold);
}

public class SecondaryButtonModifier : ViewModifier
{
	public override View Apply(View view) => view
		.FontFamily(CoffeeColors.FontSemibold)
		.FontSize(16)
		.Background(Colors.Transparent)
		.RoundedBorder(radius: CoffeeColors.RadiusPill, color: CoffeeColors.Primary, strokeSize: 1)
		.Color(CoffeeColors.Primary)
		.Frame(height: CoffeeColors.ButtonHeight);
}

public class DangerButtonModifier : ViewModifier
{
	public override View Apply(View view) => view
		.FontFamily(CoffeeColors.FontSemibold)
		.FontSize(16)
		.Background(CoffeeColors.Error)
		.Color(Colors.White)
		.ClipShape(new RoundedRectangle(CoffeeColors.RadiusPill))
		.Frame(height: CoffeeColors.ButtonHeight)
		.FontWeight(FontWeight.Bold);
}

/// <summary>
/// Convenience singleton instances for use with .Modifier().
/// </summary>
public static class CoffeeModifiers
{
	public static readonly ViewModifier Divider = new DividerModifier();
	public static readonly ViewModifier RatingBar = new RatingBarModifier();
	public static readonly ViewModifier SurfaceCard = new SurfaceCardModifier();
	public static readonly ViewModifier SurfaceVariantCard = new SurfaceVariantCardModifier();
	public static readonly ViewModifier CardSurface = new CardSurfaceModifier();
	public static readonly ViewModifier ShotCard = new ShotCardModifier();
	public static readonly ViewModifier SurfaceVariantField = new SurfaceVariantFieldModifier();
	public static readonly ViewModifier TransparentField = new TransparentFieldModifier();
	public static readonly ViewModifier Headline = new TypographyModifier(CoffeeColors.FontSemibold, 24, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier SubHeadline = new TypographyModifier(CoffeeColors.FontSemibold, 20, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier TitleSmall = new TypographyModifier(CoffeeColors.FontSemibold, 18, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier Body = new TypographyModifier(CoffeeColors.FontRegular, 14, () => CoffeeColors.TextPrimary);
	public static readonly ViewModifier BodyStrong = new TypographyModifier(CoffeeColors.FontSemibold, 14, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier SecondaryText = new TypographyModifier(CoffeeColors.FontRegular, 14, () => CoffeeColors.TextSecondary);
	public static readonly ViewModifier MutedText = new TypographyModifier(CoffeeColors.FontRegular, 14, () => CoffeeColors.TextMuted);
	public static readonly ViewModifier Caption = new TypographyModifier(CoffeeColors.FontRegular, 12, () => CoffeeColors.TextMuted);
	public static readonly ViewModifier SmallText = new TypographyModifier(CoffeeColors.FontRegular, 13, () => CoffeeColors.TextSecondary);
	public static readonly ViewModifier TinyText = new TypographyModifier(CoffeeColors.FontRegular, 11, () => CoffeeColors.TextMuted);
	public static readonly ViewModifier MicroText = new TypographyModifier(CoffeeColors.FontRegular, 9, () => CoffeeColors.TextSecondary);
	public static readonly ViewModifier LabelStrong = new TypographyModifier(CoffeeColors.FontSemibold, 12, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier ValueText = new TypographyModifier(CoffeeColors.FontSemibold, 16, () => CoffeeColors.TextPrimary);
	public static readonly ViewModifier CardTitle = new TypographyModifier(CoffeeColors.FontSemibold, 16, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier CardSubtitle = new TypographyModifier(CoffeeColors.FontRegular, 14, () => CoffeeColors.TextSecondary);
	public static readonly ViewModifier FormLabel = new TypographyModifier(CoffeeColors.FontRegular, 12, () => CoffeeColors.TextSecondary);
	public static readonly ViewModifier FormValue = new TypographyModifier(CoffeeColors.FontRegular, 16, () => CoffeeColors.TextPrimary);
	public static readonly ViewModifier BadgeText = new TypographyModifier(CoffeeColors.FontSemibold, 10, () => Colors.White, FontWeight.Bold);
	public static readonly ViewModifier BodyError = new TypographyModifier(CoffeeColors.FontRegular, 14, () => CoffeeColors.Error);
	public static readonly ViewModifier RatingAverage = new TypographyModifier(CoffeeColors.FontSemibold, 36, () => CoffeeColors.Primary, FontWeight.Bold);
	public static readonly ViewModifier RatingLabel = new TypographyModifier(CoffeeColors.FontRegular, 12, () => CoffeeColors.TextMuted);
	public static readonly ViewModifier RatingStatLabel = new TypographyModifier(CoffeeColors.FontRegular, 12, () => CoffeeColors.TextMuted);
	public static readonly ViewModifier RatingStatValue = new TypographyModifier(CoffeeColors.FontSemibold, 12, () => CoffeeColors.TextPrimary, FontWeight.Bold);
	public static readonly ViewModifier RatingLevelLabel = new TypographyModifier(CoffeeColors.FontSemibold, 12, () => CoffeeColors.TextSecondary, FontWeight.Bold);
	public static readonly ViewModifier RatingCountLabel = new TypographyModifier(CoffeeColors.FontRegular, 12, () => CoffeeColors.TextMuted);
	public static readonly CardModifier Card = new();
	public static readonly ListCardModifier ListCard = new();
	public static readonly FormFieldModifier FormField = new();
	public static readonly FormTextFieldModifier FormTextField = new();
	public static readonly FormPickerModifier FormPicker = new();
	public static readonly SectionHeaderModifier SectionHeader = new();
	public static readonly PrimaryButtonModifier PrimaryButton = new();
	public static readonly SecondaryButtonModifier SecondaryButton = new();
	public static readonly DangerButtonModifier DangerButton = new();
	public static readonly ErrorCardModifier ErrorCard = new();
	public static readonly PageContainerModifier PageContainer = new();
	public static readonly FloatingActionButtonModifier FloatingActionButton = new();

	public static ViewModifier TextColor(Color color) => new TextColorModifier(color);
	public static ViewModifier Background(Color color) => new BackgroundColorModifier(color);
	public static ViewModifier StrokeColor(Color color) => new StrokeColorModifier(color);
	public static ViewModifier CornerRadius(float radius) => new CornerRadiusModifier(radius);
	public static ViewModifier ClipShape(IShape shape) => new ClipShapeModifier(shape);
	public static ViewModifier FrameHeight(float height) => new FrameModifier(null, height);
	public static ViewModifier FrameSize(float width, float height) => new FrameModifier(width, height);
	public static ViewModifier EmojiChip(Color color) => new EmojiChipModifier(color);
	public static ViewModifier FormEditor(float height, Thickness? margin = null)
		=> new FormEditorModifier(height, margin ?? new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS));
	public static readonly ViewModifier FormEditorContainer = new FormEditorContainerModifier();
	public static ViewModifier FormSlider(Color minimum, Color maximum, Color? thumb = null, Thickness? margin = null)
		=> new SliderModifier(minimum, maximum, thumb, margin);
	public static ViewModifier FormToggle(Color onColor) => new ToggleModifier(onColor);
	public static ViewModifier IconSmall(Color color) => new IconModifier(16, color);
	public static ViewModifier IconMedium(Color color) => new IconModifier(20, color);
	public static ViewModifier IconLarge(Color color) => new IconModifier(24, color);
	public static ViewModifier IconXLarge(Color color) => new IconModifier(32, color);
	public static ViewModifier Icon(double size, Color color) => new IconModifier(size, color);
	public static ViewModifier Icon(double size) => new IconModifier(size);
	public static ViewModifier IconFont(double size, string fontFamily, Color? color = null)
		=> new IconFontModifier(fontFamily, size, color);
	public static ViewModifier AvatarCircle(float size) => new AvatarCircleModifier(size);
	public static ViewModifier AvatarBorder(float size, float radius, Color background, Color strokeColor, float strokeThickness = 2)
		=> new AvatarBorderModifier(size, radius, background, strokeColor, strokeThickness);
	public static ViewModifier OutlinePill(Color strokeColor) => new OutlinePillModifier(strokeColor);
	public static ViewModifier PillChip(Color background) => new PillChipModifier(background);
	public static ViewModifier ChatBubble(Color background) => new ChatBubbleModifier(background);
}
