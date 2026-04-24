using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;

namespace CometBaristaNotes.Components;

/// <summary>
/// Factory methods returning Comet views for form fields and UI components.
/// Applies CoffeeModifiers for consistent styling.
/// </summary>
public static class FormHelpers
{
	public static View MakeIcon(string glyph, double size, Color color)
	{
		return Text(glyph)
			.Modifier(CoffeeModifiers.Icon(size, color));
	}

	public static View MakeCard(View content)
	{
		return Border(content)
			.Modifier(CoffeeModifiers.Card);
	}

	public static View MakeSectionHeader(string title)
	{
		return Text(title.ToUpperInvariant())
			.Modifier(CoffeeModifiers.SectionHeader);
	}

	public static View MakeFormEntry(string label, string value, string placeholder, Action<string> onChanged)
	{
		return new Comet.Grid(
			columns: new object[] { "*" },
			rows: new object[] { "Auto", CoffeeColors.FormFieldHeight.ToString() })
		{
			Text(label)
				.Modifier(CoffeeModifiers.FormLabel)
				.Margin(new Thickness(16, 0, 0, 4))
				.Cell(row: 0, column: 0),

			Border(new Spacer())
				.Modifier(CoffeeModifiers.FormField)
				.Cell(row: 1, column: 0),

			TextField(value, placeholder)
				.Modifier(CoffeeModifiers.FormTextField)
				.OnTextChanged(onChanged)
				.Cell(row: 1, column: 0)
		};
	}

	public static View MakeReadOnlyField(string label, string value)
	{
		return VStack(
			Text(label)
				.Modifier(CoffeeModifiers.FormLabel)
				.Margin(new Thickness(16, 0, 0, 4)),

			new Comet.Grid(
				columns: new object[] { "*" },
				rows: new object[] { CoffeeColors.FormFieldHeight.ToString() })
			{
				Border(new Spacer())
					.Modifier(CoffeeModifiers.SurfaceVariantField)
					.Cell(row: 0, column: 0),
				Text(value)
					.Modifier(CoffeeModifiers.CardTitle)
					.VerticalTextAlignment(TextAlignment.Center)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.Padding(new Thickness(CoffeeColors.SpacingM, 0))
					.Cell(row: 0, column: 0)
			}
		);
	}

	public static View MakePrimaryButton(string title, Action action)
	{
		return Button(title, action)
			.Modifier(CoffeeModifiers.PrimaryButton);
	}

	public static View MakeSecondaryButton(string title, Action action)
	{
		return Button(title, action)
			.Modifier(CoffeeModifiers.SecondaryButton);
	}

	public static View MakeDangerButton(string title, Action action)
	{
		return Button(title, action)
			.Modifier(CoffeeModifiers.DangerButton);
	}

	public static View MakeEmptyState(string icon, string title, string description, Action? action = null, string? actionLabel = null, string? iconFontFamily = null)
	{
		var views = new List<View>
		{
			Text(icon)
				.Modifier(CoffeeModifiers.IconFont(48, iconFontFamily ?? Icons.FontFamily)),

			Text(title)
				.Modifier(CoffeeModifiers.TitleSmall)
				.HorizontalTextAlignment(TextAlignment.Center),

			Text(description)
				.Modifier(CoffeeModifiers.SecondaryText)
				.HorizontalTextAlignment(TextAlignment.Center)
		};

		if (action != null && actionLabel != null)
			views.Add(MakePrimaryButton(actionLabel, action));

		var stack = VStack(12);
		foreach (var v in views)
			stack.Add(v);

		return stack.Padding(new Thickness(CoffeeColors.SpacingXL));
	}

	public static View MakeListCard(string title, string? subtitle, string? detail, Action? onTap)
	{
		var infoViews = new List<View>
		{
			Text(title)
				.Modifier(CoffeeModifiers.CardTitle),
		};
		if (subtitle != null)
			infoViews.Add(
				Text(subtitle)
					.Modifier(CoffeeModifiers.CardSubtitle));
		if (detail != null)
			infoViews.Add(
				Text(detail)
					.Modifier(CoffeeModifiers.Caption));

		var infoStack = VStack(2);
		foreach (var v in infoViews)
			infoStack.Add(v);

		var chevron = Text(Icons.ChevronRight)
			.Modifier(CoffeeModifiers.IconMedium(CoffeeColors.TextMuted))
			.Padding(new Thickness(CoffeeColors.SpacingS, 0));

		var row = HStack(CoffeeColors.SpacingS,
			infoStack.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.ListCard);

		if (onTap != null)
			card = card.OnTap(_ => onTap());

		return card;
	}

	public static View MakeFormPicker(string label, int selectedIndex, string[] items, Action<int> onChanged)
	{
		return VStack(
			Text(label)
				.Modifier(CoffeeModifiers.FormLabel)
				.Margin(new Thickness(16, 0, 0, 4)),

			new Comet.Grid(
				columns: new object[] { "*" },
				rows: new object[] { CoffeeColors.FormFieldHeight.ToString() })
			{
				Border(new Spacer())
					.Modifier(CoffeeModifiers.FormField)
					.Cell(row: 0, column: 0),
				Picker(selectedIndex, items)
					.Modifier(CoffeeModifiers.FormPicker)
					.OnSelectedIndexChanged(onChanged)
					.Cell(row: 0, column: 0)
			}
		);
	}

	public static View MakeFormSlider(string label, double value, double min, double max, Action<double> onChanged)
	{
		return VStack(
			Text(label)
				.Modifier(CoffeeModifiers.FormLabel)
				.Margin(new Thickness(16, 0, 0, 4)),

			Slider(value, min, max)
				.Modifier(CoffeeModifiers.FormSlider(
					CoffeeColors.Primary,
					CoffeeColors.SurfaceVariant,
					margin: new Thickness(16, 0)))
				.OnValueChanged(onChanged)
		);
	}

	public static View MakeFormEditor(string label, string value, Action<string> onChanged, double height = 80)
	{
		return VStack(
			Text(label)
				.Modifier(CoffeeModifiers.FormLabel)
				.Margin(new Thickness(16, 0, 0, 4)),

			Border(
				TextEditor(value)
					.Modifier(CoffeeModifiers.FormEditor((float)height))
					.OnTextChanged(onChanged)
			)
			.Modifier(CoffeeModifiers.FormEditorContainer)
		);
	}

	public static View MakeFormEntryWithLimit(string label, string value, string placeholder, int maxLength, Action<string> onChanged)
	{
		var currentLength = value?.Length ?? 0;

		return VStack(
			new Comet.Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", CoffeeColors.FormFieldHeight.ToString() })
			{
				Text(label)
					.Modifier(CoffeeModifiers.FormLabel)
					.Margin(new Thickness(16, 0, 0, 4))
					.Cell(row: 0, column: 0),

				Border(new Spacer())
					.Modifier(CoffeeModifiers.FormField)
					.Cell(row: 1, column: 0),

				TextField(value, placeholder)
					.Modifier(CoffeeModifiers.FormTextField)
					.OnTextChanged(text =>
					{
						text ??= string.Empty;
						if (text.Length > maxLength)
							text = text[..maxLength];
						onChanged(text);
					})
					.Cell(row: 1, column: 0)
			},

			Text($"{currentLength}/{maxLength}")
				.Modifier(CoffeeModifiers.Caption)
				.Modifier(CoffeeModifiers.TextColor(currentLength >= maxLength ? CoffeeColors.Warning : CoffeeColors.TextMuted))
				.HorizontalTextAlignment(TextAlignment.End)
		);
	}

	public static View MakeToggleRow(string label, bool isOn, Action<bool> onChanged)
	{
		var grid = Grid(columns: new object[] { "*", "Auto" }, rows: new object[] { "Auto" },
			Text(label)
				.Modifier(CoffeeModifiers.BodyStrong)
				.VerticalTextAlignment(TextAlignment.Center)
				.Cell(row: 0, column: 0),

			Toggle(isOn)
				.Modifier(CoffeeModifiers.FormToggle(CoffeeColors.Primary))
				.OnToggled(onChanged)
				.Cell(row: 0, column: 1)
		);

		return MakeCard(grid);
	}
}
