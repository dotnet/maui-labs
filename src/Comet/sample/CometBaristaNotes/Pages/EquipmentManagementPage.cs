using Comet;
using Comet.Styles;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services;
using CometBaristaNotes.Components;
using CometBaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometBaristaNotes.Pages;

public class EquipmentManagementPageState
{
	public List<Equipment> Equipment { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class EquipmentManagementPage : Component<EquipmentManagementPageState>
{
	void LoadEquipment()
	{
		var store = InMemoryDataStore.Instance;
		if (store == null) return;
		SetState(s =>
		{
			s.Equipment = store.GetAllEquipment();
			s.IsLoaded = true;
		});
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadEquipment();

		var items = State.Equipment;

		if (items.Count == 0)
		{
			return VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Machine,
					"No Equipment Yet",
					"Add your coffee machines, grinders, and accessories",
					iconFontFamily: Icons.CoffeeFontFamily),
				FormHelpers.MakePrimaryButton("+ Add Equipment", () =>
					Comet.NavigationView.Navigate(this, new EquipmentDetailPage(0)))
			)
			.Padding(new Thickness(CoffeeColors.SpacingL))
			.FillHorizontal()
			.Modifier(CoffeeModifiers.PageContainer);
		}

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakePrimaryButton("+ Add Equipment", () =>
				Comet.NavigationView.Navigate(this, new EquipmentDetailPage(0)))
		);

		foreach (var eq in items)
		{
			stack.Add(MakeEquipmentCard(eq));
		}

		return ScrollView(stack.Padding(new Thickness(CoffeeColors.SpacingM)))
			.Modifier(CoffeeModifiers.PageContainer);
	}

	View MakeEquipmentCard(Equipment eq)
	{
		var details = VStack(4,
			Text(eq.Name)
				.Modifier(CoffeeModifiers.CardTitle),
			Text(eq.Type.ToString())
				.Modifier(CoffeeModifiers.CardSubtitle)
		);

		var chevron = FormHelpers.MakeIcon(Icons.ChevronRight, 20, CoffeeColors.TextMuted);

		var row = HStack(CoffeeColors.SpacingS,
			details.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new EquipmentDetailPage(eq.Id)));

		return card;
	}
}
