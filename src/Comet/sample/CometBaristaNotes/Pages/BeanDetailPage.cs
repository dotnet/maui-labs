namespace CometBaristaNotes.Pages;

public class BeanDetailPageState
{
	public string Name { get; set; } = "";
	public string Roaster { get; set; } = "";
	public string Origin { get; set; } = "";
	public string Notes { get; set; } = "";
	public bool IsLoaded { get; set; }
	public string Error { get; set; } = "";
	public List<Bag> Bags { get; set; } = new();
	public RatingAggregate Rating { get; set; } = new();
	public List<ShotRecord> AllShots { get; set; } = new();
	public int VisibleShotCount { get; set; } = 20;
}

public class BeanDetailPage : Component<BeanDetailPageState>
{
	readonly int _beanId;
	readonly IDataStore _store;
	const int ShotsPageSize = 20;

	public BeanDetailPage(int beanId = 0)
	{
		_beanId = beanId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadBean()
	{
		if (_beanId <= 0) { SetState(s => s.IsLoaded = true); return; }

		var store = _store;

		var bean = store.GetBean(_beanId);
		if (bean == null)
		{
			SetState(s => { s.Error = "Bean not found"; s.IsLoaded = true; });
			return;
		}

		SetState(s =>
		{
			s.Name = bean.Name;
			s.Roaster = bean.Roaster ?? "";
			s.Origin = bean.Origin ?? "";
			s.Notes = bean.Notes ?? "";
			s.Bags = store.GetBagsForBean(_beanId);
			s.Rating = store.GetBeanRating(_beanId);
			s.AllShots = store.GetShotsByBean(_beanId);
			s.IsLoaded = true;
		});
	}

	void Save()
	{
		if (string.IsNullOrWhiteSpace(State.Name))
		{
			SetState(s => s.Error = "Bean name is required");
			return;
		}
		SetState(s => s.Error = "");

		var store = _store;

		if (_beanId > 0)
		{
			store.UpdateBean(new Bean
			{
				Id = _beanId,
				Name = State.Name,
				Roaster = string.IsNullOrWhiteSpace(State.Roaster) ? null : State.Roaster,
				Origin = string.IsNullOrWhiteSpace(State.Origin) ? null : State.Origin,
				Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
				IsActive = true
			});
		}
		else
		{
			store.CreateBean(new Bean
			{
				Name = State.Name,
				Roaster = string.IsNullOrWhiteSpace(State.Roaster) ? null : State.Roaster,
				Origin = string.IsNullOrWhiteSpace(State.Origin) ? null : State.Origin,
				Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
			});
		}

		Navigation?.Pop();
	}

	async void DeleteBean()
	{
		var confirmed = await Services.PageHelper.DisplayAlertAsync(
			"Delete Bean?",
			$"Are you sure you want to delete \"{State.Name}\"? This will also archive all associated bags.",
			"Delete", "Cancel");

		if (!confirmed) return;

		_store.ArchiveBean(_beanId);
		Navigation?.Pop();
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadBean();

		var isEdit = _beanId > 0;

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakeSectionHeader(isEdit ? "EDIT BEAN" : "NEW BEAN"),
			FormHelpers.MakeFormEntry("Name *", State.Name, "Bean name (required)", v => SetState(s => s.Name = v)),
			FormHelpers.MakeFormEntry("Roaster", State.Roaster, "Roaster name", v => SetState(s => s.Roaster = v)),
			FormHelpers.MakeFormEntry("Origin", State.Origin, "Country or region of origin", v => SetState(s => s.Origin = v)),
			FormHelpers.MakeFormEditor("Notes", State.Notes, v => SetState(s => s.Notes = v), height: 120)
		);

		if (!string.IsNullOrEmpty(State.Error))
		{
			stack.Add(
				Border(
					Text(State.Error)
						.Modifier(CoffeeModifiers.BodyError)
						.Padding(new Thickness(12))
				)
				.Modifier(CoffeeModifiers.ErrorCard)
			);
		}

		stack.Add(FormHelpers.MakePrimaryButton(isEdit ? "Save Changes" : "Create Bean", Save));

		if (isEdit)
		{
			stack.Add(FormHelpers.MakeDangerButton("Delete Bean", DeleteBean));

			// Rating section
			stack.Add(MakeDivider());
			stack.Add(FormHelpers.MakeSectionHeader("BEAN RATINGS"));
			stack.Add(new RatingDisplay(State.Rating));
			stack.Add(BuildRatingDistribution());

			// Bags section
			stack.Add(MakeDivider());
			stack.Add(FormHelpers.MakeSectionHeader("BAGS"));

			if (State.Bags.Count == 0)
			{
				stack.Add(
					Text("No bags added yet")
						.Modifier(CoffeeModifiers.SecondaryText)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Padding(new Thickness(0, CoffeeColors.SpacingS))
				);
			}
			else
			{
				foreach (var bag in State.Bags)
					stack.Add(BuildBagCard(bag));
			}

			stack.Add(FormHelpers.MakeSecondaryButton("+ Add Bag", () =>
			{
				Navigation?.Navigate(new BagFormPage(_beanId));
			}));

			// Shot history section
			stack.Add(MakeDivider());
			stack.Add(FormHelpers.MakeSectionHeader("SHOT HISTORY"));

			var shots = State.AllShots;
			if (shots.Count == 0)
			{
				stack.Add(FormHelpers.MakeEmptyState(
					Icons.Assignment, "No Shots Yet",
					"No shots recorded with this bean yet"));
			}
			else
			{
				var visible = shots.Take(State.VisibleShotCount).ToList();
				foreach (var shot in visible)
				{
					var shotId = shot.Id;
					stack.Add(new ShotRecordCard(shot, () =>
					{
						Navigation?.Navigate(new ShotLoggingPage(shotId));
					}));
				}

				if (State.VisibleShotCount < shots.Count)
				{
					stack.Add(FormHelpers.MakeSecondaryButton(
						$"Load More ({shots.Count - State.VisibleShotCount} remaining)",
						() => SetState(s => s.VisibleShotCount += ShotsPageSize)));
				}
			}
		}

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title(isEdit ? "Edit Bean" : "New Bean");
	}

	static View MakeDivider()
	{
		return new Comet.BoxView(CoffeeColors.Outline.WithAlpha(0.5f))
			.Modifier(CoffeeModifiers.Divider)
			.Margin(new Thickness(0, CoffeeColors.SpacingS));
	}

	View BuildRatingDistribution()
	{
		var sentiments = new[]
		{
			Icons.SentimentVeryDissatisfied,
			Icons.SentimentDissatisfied,
			Icons.SentimentNeutral,
			Icons.SentimentSatisfied,
			Icons.SentimentVerySatisfied
		};
		var sentimentColors = new[]
		{
			CoffeeColors.Error,
			CoffeeColors.Warning,
			CoffeeColors.TextMuted,
			CoffeeColors.Success,
			CoffeeColors.StarFilled
		};

		var counts = new int[5];
		foreach (var shot in State.AllShots)
		{
			if (shot.Rating.HasValue)
			{
				var idx = Math.Clamp(shot.Rating.Value - 1, 0, 4);
				counts[idx]++;
			}
		}
		var maxCount = counts.Max();

		var container = VStack(CoffeeColors.SpacingXS);

		for (var i = 4; i >= 0; i--)
		{
			var barFraction = maxCount > 0 ? (double)counts[i] / maxCount : 0;

			container.Add(
				Grid(columns: new object[] { 28, "*", 30 }, rows: new object[] { "Auto" },
					Text(sentiments[i])
						.Modifier(CoffeeModifiers.Icon(18, sentimentColors[i]))
						.HorizontalTextAlignment(TextAlignment.Center)
						.VerticalTextAlignment(TextAlignment.Center)
						.Cell(row: 0, column: 0),

					ProgressBar(barFraction)
						.ProgressColor(sentimentColors[i])
						.TrackColor(CoffeeColors.SurfaceVariant)
						.Modifier(CoffeeModifiers.FrameHeight(12))
						.Cell(row: 0, column: 1),

					Text(counts[i].ToString())
						.Modifier(CoffeeModifiers.FormLabel)
						.HorizontalTextAlignment(TextAlignment.End)
						.VerticalTextAlignment(TextAlignment.Center)
						.Cell(row: 0, column: 2)
				)
				.ColumnSpacing(CoffeeColors.SpacingS)
				.Modifier(CoffeeModifiers.FrameHeight(24))
			);
		}

		return Border(container)
			.Modifier(CoffeeModifiers.CardSurface)
			.Padding(new Thickness(CoffeeColors.SpacingM))
			.Margin(new Thickness(0, CoffeeColors.SpacingXS, 0, 0));
	}

	View BuildBagCard(Bag bag)
	{
		// Header row: roast date + status badge
		var headerRow = HStack(CoffeeColors.SpacingS,
			Text($"Roasted {bag.RoastDate:MMM d, yyyy}")
				.Modifier(CoffeeModifiers.BodyStrong),
			Spacer(),
			Text(bag.IsComplete ? "Complete" : "Active")
				.Modifier(CoffeeModifiers.FormLabel)
				.Modifier(CoffeeModifiers.TextColor(bag.IsComplete ? CoffeeColors.Success : CoffeeColors.Primary))
				.Padding(new Thickness(6, 2))
		);

		var infoStack = VStack(4, headerRow);

		if (bag.Notes != null)
		{
			infoStack.Add(
				Text(bag.Notes)
					.Modifier(CoffeeModifiers.FormLabel)
			);
		}

		infoStack.Add(HStack(CoffeeColors.SpacingM,
			Text($"{bag.ShotCount} shots")
				.Modifier(CoffeeModifiers.Caption),
			bag.AverageRating.HasValue
				? Text($"{bag.AverageRating.Value:F1}")
					.Modifier(CoffeeModifiers.LabelStrong)
					.Modifier(CoffeeModifiers.TextColor(CoffeeColors.StarFilled))
				: Text("No ratings")
					.Modifier(CoffeeModifiers.Caption)
		));

		return Border(
			Grid(columns: new object[] { "*", "Auto" }, rows: new object[] { "Auto" },
				infoStack.Cell(row: 0, column: 0),
				Text(Icons.ChevronRight)
					.Modifier(CoffeeModifiers.IconMedium(CoffeeColors.TextMuted))
					.VerticalTextAlignment(TextAlignment.Center)
					.Cell(row: 0, column: 1)
			)
		)
		.Modifier(CoffeeModifiers.CardSurface)
		.Padding(new Thickness(CoffeeColors.SpacingM))
		.OnTap(_ => Navigation?.Navigate(new BagDetailPage(bag.Id)));
	}
}
