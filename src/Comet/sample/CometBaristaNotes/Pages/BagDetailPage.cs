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

public class BagDetailPageState
{
	public string BeanName { get; set; } = "";
	public int BeanId { get; set; }
	public string RoastDate { get; set; } = "";
	public string Notes { get; set; } = "";
	public bool IsComplete { get; set; }
	public int ShotCount { get; set; }
	public bool IsLoaded { get; set; }
	public bool IsSaving { get; set; }
	public string Error { get; set; } = "";
	public RatingAggregate Rating { get; set; } = new();
	public List<ShotRecord> RelatedShots { get; set; } = new();
}

public class BagDetailPage : Component<BagDetailPageState>
{
	readonly int _bagId;

	public BagDetailPage(int bagId = 0) { _bagId = bagId; }

	void LoadBag()
	{
		if (_bagId <= 0)
		{
			SetState(s => s.IsLoaded = true);
			return;
		}

		var store = InMemoryDataStore.Instance;
		if (store == null) return;

		var bag = store.GetBag(_bagId);
		if (bag == null)
		{
			SetState(s =>
			{
				s.Error = "Bag not found";
				s.IsLoaded = true;
			});
			return;
		}

		var rating = store.GetBagRating(_bagId);
		var shots = store.GetShotsForBag(_bagId);

		SetState(s =>
		{
			s.BeanName = bag.BeanName ?? "";
			s.BeanId = bag.BeanId;
			s.RoastDate = bag.RoastDate.ToString("yyyy-MM-dd");
			s.Notes = bag.Notes ?? "";
			s.IsComplete = bag.IsComplete;
			s.ShotCount = bag.ShotCount;
			s.Rating = rating;
			s.RelatedShots = shots;
			s.IsLoaded = true;
		});
	}

	void Save()
	{
		// Validate roast date
		if (!DateTime.TryParse(State.RoastDate, out var roastDate))
		{
			SetState(s => s.Error = "Please enter a valid date (yyyy-MM-dd)");
			return;
		}
		if (roastDate.Date > DateTime.Now.Date)
		{
			SetState(s => s.Error = "Roast date cannot be in the future");
			return;
		}
		if (!string.IsNullOrEmpty(State.Notes) && State.Notes.Length > 500)
		{
			SetState(s => s.Error = "Notes cannot exceed 500 characters");
			return;
		}

		SetState(s => { s.Error = ""; s.IsSaving = true; });

		var store = InMemoryDataStore.Instance;
		if (store == null) return;

		if (_bagId > 0)
		{
			store.UpdateBag(new Bag
			{
				Id = _bagId,
				BeanId = State.BeanId,
				RoastDate = roastDate,
				Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
				IsComplete = State.IsComplete,
				IsActive = true
			});
		}

		SetState(s => s.IsSaving = false);
		Comet.NavigationView.Pop(this);
	}

	async void DeleteBag()
	{
		var message = State.ShotCount > 0
			? $"This bag has {State.ShotCount} shot(s) logged. Deleting it will hide it from all lists. Continue?"
			: "Are you sure you want to delete this bag?";

		var confirmed = await PageHelper.DisplayAlertAsync("Delete Bag", message, "Delete", "Cancel");
		if (!confirmed) return;

		var store = InMemoryDataStore.Instance;
		if (store == null) return;

		store.ArchiveBag(_bagId);
		Comet.NavigationView.Pop(this);
	}

	void ToggleBagStatus()
	{
		var store = InMemoryDataStore.Instance;
		if (store == null) return;

		if (State.IsComplete)
		{
			store.ReactivateBag(_bagId);
			SetState(s => s.IsComplete = false);
		}
		else
		{
			store.MarkComplete(_bagId);
			SetState(s => s.IsComplete = true);
		}
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadBag();

		if (_bagId <= 0)
		{
			return FormHelpers.MakeEmptyState(Icons.Coffee, "Bag not found", "No bag ID provided.")
				.Modifier(CoffeeModifiers.PageContainer);
		}

		if (!string.IsNullOrEmpty(State.Error) && !State.IsLoaded)
		{
			return FormHelpers.MakeEmptyState(Icons.Warning, "Error", State.Error)
				.Modifier(CoffeeModifiers.PageContainer);
		}

		var items = new List<View>();

		// Form section
		items.Add(FormHelpers.MakeSectionHeader("BAG DETAILS"));
		items.Add(FormHelpers.MakeReadOnlyField("Bean", State.BeanName));
		items.Add(FormHelpers.MakeFormEntry("Roast Date", State.RoastDate, "yyyy-MM-dd", v => SetState(s => s.RoastDate = v)));
		items.Add(FormHelpers.MakeFormEntryWithLimit("Notes", State.Notes, "Bag notes", 500, v => SetState(s => s.Notes = v)));

		// Status section
		items.Add(FormHelpers.MakeSectionHeader("STATUS"));
		items.Add(FormHelpers.MakeToggleRow(
			State.IsComplete ? "Status: Complete" : "Status: Active",
			State.IsComplete,
			v => ToggleBagStatus()
		));

		// Stats section
		items.Add(FormHelpers.MakeSectionHeader("STATS"));
		items.Add(FormHelpers.MakeCard(
			HStack(CoffeeColors.SpacingM,
				VStack(2,
					Text("Shots Logged")
						.Modifier(CoffeeModifiers.SecondaryText),
					Text($"{State.ShotCount}")
						.Modifier(CoffeeModifiers.Headline)
				)
			)
		));

		// Rating section
		items.Add(FormHelpers.MakeSectionHeader("RATINGS"));
		if (State.Rating.RatedShots > 0)
			items.Add(RatingDisplayFactory.Create(State.Rating));
		else
			items.Add(Text("No ratings yet")
				.Modifier(CoffeeModifiers.SecondaryText)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Padding(new Thickness(0, CoffeeColors.SpacingM)));

		// Related shots section
		if (State.RelatedShots.Count > 0)
		{
			items.Add(FormHelpers.MakeSectionHeader("RELATED SHOTS"));
			foreach (var shot in State.RelatedShots)
			{
				items.Add(ShotRecordCardFactory.Create(shot, () =>
					Comet.NavigationView.Navigate(this, new ShotLoggingPage(shot.Id))));
			}
		}

		// Error display
		if (!string.IsNullOrEmpty(State.Error))
			items.Add(Text(State.Error)
				.Modifier(CoffeeModifiers.BodyError)
				.Padding(new Thickness(0, CoffeeColors.SpacingXS)));

		// Action buttons
		items.Add(FormHelpers.MakePrimaryButton(State.IsSaving ? "Saving..." : "Save Changes", Save));
		items.Add(FormHelpers.MakeDangerButton("Delete Bag", DeleteBag));

		var stack = VStack(CoffeeColors.SpacingS);
		foreach (var item in items) stack.Add(item);

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title("Bag Details");
	}
}
