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

public class BeanManagementPageState
{
	public List<Bean> Beans { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class BeanManagementPage : Component<BeanManagementPageState>
{
	void LoadBeans()
	{
		var store = InMemoryDataStore.Instance;
		if (store == null) return;
		SetState(s =>
		{
			s.Beans = store.GetAllBeans();
			s.IsLoaded = true;
		});
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadBeans();

		var beans = State.Beans;

		if (beans.Count == 0)
		{
			return VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Coffee,
					"No Beans Yet",
					"Add your first bean to start tracking your coffee collection"),
				FormHelpers.MakePrimaryButton("+ Add Bean", () =>
					Comet.NavigationView.Navigate(this, new BeanDetailPage(0)))
			)
			.Padding(new Thickness(CoffeeColors.SpacingL))
			.FillHorizontal()
			.Modifier(CoffeeModifiers.PageContainer);
		}

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakePrimaryButton("+ Add Bean", () =>
				Comet.NavigationView.Navigate(this, new BeanDetailPage(0)))
		);

		foreach (var bean in beans)
		{
			stack.Add(MakeBeanCard(bean));
		}

		return ScrollView(stack.Padding(new Thickness(CoffeeColors.SpacingM)))
			.Modifier(CoffeeModifiers.PageContainer);
	}

	View MakeBeanCard(Bean bean)
	{
		var details = VStack(4,
			Text(bean.Name)
				.Modifier(CoffeeModifiers.CardTitle)
		);

		if (bean.Roaster != null)
		{
			details.Add(HStack(6,
				FormHelpers.MakeIcon(Icons.Factory, CoffeeColors.IconSizeSmall, CoffeeColors.TextMuted),
				Text(bean.Roaster)
					.Modifier(CoffeeModifiers.CardSubtitle)
			));
		}

		if (bean.Origin != null)
		{
			details.Add(HStack(6,
				FormHelpers.MakeIcon(Icons.Globe, CoffeeColors.IconSizeSmall, CoffeeColors.TextMuted),
				Text(bean.Origin)
					.Modifier(CoffeeModifiers.CardSubtitle)
			));
		}

		details.Add(HStack(6,
			FormHelpers.MakeIcon(Icons.CalendarToday, CoffeeColors.IconSizeSmall, CoffeeColors.TextMuted),
			Text($"Added {bean.CreatedAt:MMM d, yyyy}")
				.Modifier(CoffeeModifiers.Caption)
		));

		var chevron = FormHelpers.MakeIcon(Icons.ChevronRight, 20, CoffeeColors.TextMuted);

		var row = HStack(CoffeeColors.SpacingS,
			details.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new BeanDetailPage(bean.Id)));

		return card;
	}
}
