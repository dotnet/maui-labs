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
	readonly IDataStore _store;

	public BagDetailPage(int bagId = 0)
	{
		_bagId = bagId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadBag()
	{
		if (_bagId <= 0)
		{
			SetState(s => s.IsLoaded = true);
			return;
		}

		var store = _store;

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

		var store = _store;

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

		_store.ArchiveBag(_bagId);
		Comet.NavigationView.Pop(this);
	}

	void ToggleBagStatus()
	{
		var store = _store;

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

		var stack = VStack(CoffeeColors.SpacingS,
			// Form section
			FormHelpers.MakeSectionHeader("BAG DETAILS"),
			FormHelpers.MakeReadOnlyField("Bean", State.BeanName),
			FormHelpers.MakeFormEntry("Roast Date", State.RoastDate, "yyyy-MM-dd", v => SetState(s => s.RoastDate = v)),
			FormHelpers.MakeFormEntryWithLimit("Notes", State.Notes, "Bag notes", 500, v => SetState(s => s.Notes = v)),

			// Status section
			FormHelpers.MakeSectionHeader("STATUS"),
			FormHelpers.MakeToggleRow(
				State.IsComplete ? "Status: Complete" : "Status: Active",
				State.IsComplete,
				v => ToggleBagStatus()
			),

			// Stats section
			FormHelpers.MakeSectionHeader("STATS"),
			FormHelpers.MakeCard(
				HStack(CoffeeColors.SpacingM,
					VStack(2,
						Text("Shots Logged")
							.Modifier(CoffeeModifiers.SecondaryText),
						Text($"{State.ShotCount}")
							.Modifier(CoffeeModifiers.Headline)
					)
				)
			),

			// Rating section
			FormHelpers.MakeSectionHeader("RATINGS"),
			State.Rating.RatedShots > 0
				? new RatingDisplay(State.Rating)
				: Text("No ratings yet")
					.Modifier(CoffeeModifiers.SecondaryText)
					.HorizontalTextAlignment(TextAlignment.Center)
					.Padding(new Thickness(0, CoffeeColors.SpacingM))
		);

		// Related shots (dynamic count — use Add)
		if (State.RelatedShots.Count > 0)
		{
			stack.Add(FormHelpers.MakeSectionHeader("RELATED SHOTS"));
			foreach (var shot in State.RelatedShots)
			{
				stack.Add(new ShotRecordCard(shot, () =>
					Comet.NavigationView.Navigate(this, new ShotLoggingPage(shot.Id))));
			}
		}

		if (!string.IsNullOrEmpty(State.Error))
			stack.Add(Text(State.Error)
				.Modifier(CoffeeModifiers.BodyError)
				.Padding(new Thickness(0, CoffeeColors.SpacingXS)));

		stack.Add(FormHelpers.MakePrimaryButton(State.IsSaving ? "Saving..." : "Save Changes", Save));
		stack.Add(FormHelpers.MakeDangerButton("Delete Bag", DeleteBag));

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title("Bag Details");
	}
}
