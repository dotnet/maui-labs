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

public class UserProfileManagementPageState
{
	public List<UserProfile> Profiles { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class UserProfileManagementPage : Component<UserProfileManagementPageState>
{
	void LoadProfiles()
	{
		var store = InMemoryDataStore.Instance;
		if (store == null) return;
		SetState(s =>
		{
			s.Profiles = store.GetAllProfiles();
			s.IsLoaded = true;
		});
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadProfiles();

		var profiles = State.Profiles;

		if (profiles.Count == 0)
		{
			return VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Person,
					"No Profiles Yet",
					"Create profiles for different users or coffee preferences"),
				FormHelpers.MakePrimaryButton("+ Add Profile", () =>
					Comet.NavigationView.Navigate(this, new ProfileFormPage(0)))
			)
			.Padding(new Thickness(CoffeeColors.SpacingL))
			.FillHorizontal()
			.Modifier(CoffeeModifiers.PageContainer);
		}

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakePrimaryButton("+ Add Profile", () =>
				Comet.NavigationView.Navigate(this, new ProfileFormPage(0)))
		);

		foreach (var profile in profiles)
		{
			stack.Add(MakeProfileCard(profile));
		}

		return ScrollView(stack.Padding(new Thickness(CoffeeColors.SpacingM)))
			.Modifier(CoffeeModifiers.PageContainer);
	}

	View MakeProfileCard(UserProfile profile)
	{
		var details = VStack(4,
			Text(profile.Name)
				.Modifier(CoffeeModifiers.CardTitle),
			HStack(6,
				FormHelpers.MakeIcon(Icons.CalendarToday, CoffeeColors.IconSizeSmall, CoffeeColors.TextMuted),
				Text($"Member since {profile.CreatedAt:MMM yyyy}")
					.Modifier(CoffeeModifiers.Caption)
			)
		);

		var chevron = FormHelpers.MakeIcon(Icons.ChevronRight, 20, CoffeeColors.TextMuted);

		var row = HStack(CoffeeColors.SpacingS,
			details.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new ProfileFormPage(profile.Id)));

		return card;
	}
}
