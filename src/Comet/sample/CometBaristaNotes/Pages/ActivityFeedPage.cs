using Microsoft.Maui.ApplicationModel;

namespace CometBaristaNotes.Pages;

public class ActivityFeedState
{
	public List<ShotRecord> ShotRecords { get; set; } = new();
	public bool IsLoading { get; set; }
	public string? ErrorMessage { get; set; }
	public int PageSize { get; set; } = 20;
	public bool HasMore { get; set; } = true;
	public int TotalShotCount { get; set; }
	public int RefreshVersion { get; set; }

	// Filter state
	public ShotFilterCriteria ActiveFilters { get; set; } = new();
	public ShotFilterCriteria DraftFilters { get; set; } = new();
	public bool IsFilterSheetOpen { get; set; }
	public bool HasActiveFilters => ActiveFilters.HasFilters;
}

public class ActivityFeedPage : Component<ActivityFeedState>
{
	readonly IDataStore _store;
	readonly IDataChangeNotifier? _notifier;

	public ActivityFeedPage()
	{
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>() ?? (IDataStore)InMemoryDataStore.Instance;
		_notifier = IPlatformApplication.Current?.Services.GetService<IDataChangeNotifier>();
		if (_notifier != null)
			_notifier.DataChanged += OnDataChanged;
	}

	protected override void OnWillUnmount()
	{
		if (_notifier != null)
			_notifier.DataChanged -= OnDataChanged;
		base.OnWillUnmount();
	}

	void OnDataChanged(string entityType, int entityId, DataChangeType changeType)
	{
		if (entityType != "Shot") return;
		MainThread.BeginInvokeOnMainThread(() => LoadShots(refresh: true));
	}

	void LoadShots(bool refresh = false)
	{
		try
		{
			if (refresh)
				SetState(s => { s.ShotRecords = new List<ShotRecord>(); s.ErrorMessage = null; });

			List<ShotRecord> shots;
			if (State.HasActiveFilters)
				shots = _store.GetFilteredShots(State.ActiveFilters);
			else
				shots = _store.GetAllShots();

			var totalCount = _store.GetAllShots().Count;
			var pageEnd = State.ShotRecords.Count + State.PageSize;
			var paged = shots.Take(pageEnd).ToList();
			var hasMore = paged.Count < shots.Count;

			SetState(s =>
			{
				s.ShotRecords = paged;
				s.TotalShotCount = totalCount;
				s.HasMore = hasMore;
				s.IsLoading = false;
				s.ErrorMessage = null;
			});
		}
		catch (System.Exception ex)
		{
			SetState(s =>
			{
				s.IsLoading = false;
				s.ErrorMessage = ex.Message;
			});
		}
	}

	void LoadMore()
	{
		if (!State.HasMore || State.IsLoading) return;
		SetState(s => s.IsLoading = true);
		LoadShots();
	}

	void OpenFilterSheet()
	{
		SetState(s =>
		{
			s.DraftFilters = s.ActiveFilters.Clone();
			s.IsFilterSheetOpen = true;
		});
	}

	void CloseFilterSheet()
	{
		SetState(s =>
		{
			s.DraftFilters = s.ActiveFilters.Clone();
			s.IsFilterSheetOpen = false;
		});
	}

	void ToggleDraftSelection(List<int> selectedIds, int id)
	{
		SetState(_ =>
		{
			if (selectedIds.Contains(id))
				selectedIds.Remove(id);
			else
				selectedIds.Add(id);
		});
	}

	void OnFiltersApplied(ShotFilterCriteria filters)
	{
		SetState(s =>
		{
			s.ActiveFilters = filters;
			s.DraftFilters = filters.Clone();
			s.IsFilterSheetOpen = false;
			s.ShotRecords = new List<ShotRecord>();
		});
		LoadShots(refresh: true);
	}

	void OnFiltersCleared()
	{
		SetState(s =>
		{
			s.ActiveFilters = new ShotFilterCriteria();
			s.DraftFilters = new ShotFilterCriteria();
			s.IsFilterSheetOpen = false;
			s.ShotRecords = new List<ShotRecord>();
		});
		LoadShots(refresh: true);
	}

	public override View Render()
	{
		_ = State.RefreshVersion;

		// Initial load
		if (State.ShotRecords.Count == 0 && State.ErrorMessage == null && !State.IsLoading)
		{
			SetState(s => s.IsLoading = true);
			LoadShots();
		}

		return ZStack(
			RenderContent(),
			State.IsFilterSheetOpen ? RenderFilterOverlay() : null
		)
			.Modifier(CoffeeModifiers.PageContainer)
			.ToolbarItems(
				new Comet.ToolbarItem { IconGlyph = "line.3.horizontal.decrease", OnClicked = OpenFilterSheet })
			.Title("Shot History");
	}

	View RenderFilterOverlay()
	{
		var beans = _store.GetBeansWithShots();
		var people = _store.GetPeopleWithShots();
		var ratingOptions = new List<(int Id, string Name)>
		{
			(0, "0"),
			(1, "1"),
			(2, "2"),
			(3, "3"),
			(4, "4"),
		};

		var ratingIcons = new[]
		{
			Icons.SentimentVeryDissatisfied,
			Icons.SentimentDissatisfied,
			Icons.SentimentNeutral,
			Icons.SentimentSatisfied,
			Icons.SentimentVerySatisfied,
		};

		var content = VStack(CoffeeColors.SpacingL,
			HStack(
				Text("Filter Shots")
					.Modifier(CoffeeModifiers.SubHeadline)
					.Modifier(CoffeeModifiers.TextColor(Colors.White)),
				Spacer(),
				Text(Icons.Close)
					.Modifier(CoffeeModifiers.IconLarge(Colors.White))
					.OnTap(_ => CloseFilterSheet())
			),
			beans.Count > 0 ? RenderDarkFilterSection("Bean", beans, State.DraftFilters.BeanIds) : null,
			people.Count > 0 ? RenderDarkFilterSection("Made For", people, State.DraftFilters.MadeForIds) : null,
			RenderDarkRatingSection(ratingOptions, ratingIcons, State.DraftFilters.Ratings)
		);

		return ZStack(
			new Comet.BoxView(CoffeeColors.Primary)
				.FillHorizontal()
				.FillVertical(),
			VStack(
				ScrollView(
					content.Padding(new Thickness(CoffeeColors.SpacingL, CoffeeColors.SpacingXL, CoffeeColors.SpacingL, CoffeeColors.SpacingL))
				).FillVertical(),
				VStack(CoffeeColors.SpacingM,
					Text("Clear All")
						.Modifier(CoffeeModifiers.Body)
						.Modifier(CoffeeModifiers.TextColor(Colors.White.WithAlpha(0.8f)))
						.HorizontalTextAlignment(TextAlignment.Center)
						.OnTap(_ => OnFiltersCleared()),
					Button("Apply", () => OnFiltersApplied(State.DraftFilters.Clone()))
						.FontFamily(CoffeeColors.FontSemibold)
						.FontSize(16)
						.FontWeight(FontWeight.Bold)
						.Background(CoffeeColors.Primary.WithAlpha(0.6f))
						.Color(Colors.White)
						.ClipShape(new RoundedRectangle(CoffeeColors.RadiusPill))
						.Modifier(CoffeeModifiers.FrameHeight(CoffeeColors.ButtonHeight))
						.FillHorizontal()
				)
				.Padding(new Thickness(CoffeeColors.SpacingL, CoffeeColors.SpacingS, CoffeeColors.SpacingL, CoffeeColors.SpacingL))
			)
		)
		.IgnoreSafeArea();
	}

	View RenderDarkFilterSection(string title, IEnumerable<(int Id, string Name)> items, List<int> selectedIds)
	{
		var section = VStack(CoffeeColors.SpacingS,
			Text(title)
				.Modifier(CoffeeModifiers.BodyStrong)
				.Modifier(CoffeeModifiers.TextColor(Colors.White)));

		// Two-column grid of pill chips
		var itemList = items.ToList();
		for (int i = 0; i < itemList.Count; i += 2)
		{
			var left = itemList[i];
			var leftChip = RenderDarkChip(left.Name, selectedIds.Contains(left.Id), () => ToggleDraftSelection(selectedIds, left.Id));
			if (i + 1 < itemList.Count)
			{
				var right = itemList[i + 1];
				var rightChip = RenderDarkChip(right.Name, selectedIds.Contains(right.Id), () => ToggleDraftSelection(selectedIds, right.Id));
				section.Add(HStack(CoffeeColors.SpacingS,
					leftChip.FillHorizontal(),
					rightChip.FillHorizontal()
				));
			}
			else
			{
				section.Add(HStack(CoffeeColors.SpacingS,
					leftChip.FillHorizontal(),
					new Spacer().FillHorizontal()
				));
			}
		}

		return section;
	}

	View RenderDarkRatingSection(List<(int Id, string Name)> ratings, string[] icons, List<int> selectedIds)
	{
		var chips = HStack(CoffeeColors.SpacingS);
		for (int i = 0; i < ratings.Count; i++)
		{
			var id = ratings[i].Id;
			var icon = icons[i];
			var isSelected = selectedIds.Contains(id);
			chips.Add(
				Border(
					Text(icon)
						.Modifier(CoffeeModifiers.IconLarge(Colors.White))
						.Padding(new Thickness(CoffeeColors.SpacingS))
				)
				.Modifier(CoffeeModifiers.Background(isSelected ? Colors.White.WithAlpha(0.25f) : CoffeeColors.Primary.WithAlpha(0.4f)))
				.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusPill))
				.Modifier(CoffeeModifiers.StrokeColor(isSelected ? Colors.White : Colors.Transparent))
				.StrokeThickness(isSelected ? 1 : 0)
				.OnTap(_ => ToggleDraftSelection(selectedIds, id))
			);
		}

		return VStack(CoffeeColors.SpacingS,
			Text("Rating")
				.Modifier(CoffeeModifiers.BodyStrong)
				.Modifier(CoffeeModifiers.TextColor(Colors.White)),
			chips
		);
	}

	View RenderDarkChip(string label, bool isSelected, Action toggle) =>
		Border(
			Text(label)
				.Modifier(CoffeeModifiers.Body)
				.Modifier(CoffeeModifiers.TextColor(Colors.White))
				.HorizontalTextAlignment(TextAlignment.Center)
				.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
		)
		.Modifier(CoffeeModifiers.Background(isSelected ? Colors.White.WithAlpha(0.25f) : CoffeeColors.Primary.WithAlpha(0.4f)))
		.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusPill))
		.Modifier(CoffeeModifiers.StrokeColor(isSelected ? Colors.White : Colors.Transparent))
		.StrokeThickness(isSelected ? 1 : 0)
		.OnTap(_ => toggle());

	View RenderFilterSection(string title, IEnumerable<(int Id, string Name)> items, List<int> selectedIds)
	{
		var section = VStack(CoffeeColors.SpacingXS,
			Text(title)
				.Modifier(CoffeeModifiers.BodyStrong)
				.Modifier(CoffeeModifiers.TextColor(CoffeeColors.TextSecondary)));

		foreach (var item in items)
		{
			section.Add(RenderFilterOption(
				item.Name,
				selectedIds.Contains(item.Id),
				() => ToggleDraftSelection(selectedIds, item.Id)));
		}

		return section;
	}

	View RenderFilterOption(string label, bool isSelected, Action toggle) =>
		Border(
			HStack(
				Text(label)
					.Modifier(CoffeeModifiers.Body)
					.Modifier(CoffeeModifiers.TextColor(isSelected ? Colors.White : CoffeeColors.TextPrimary)),
				Spacer(),
				Text(isSelected ? Icons.Check : string.Empty)
					.Modifier(CoffeeModifiers.IconSmall(isSelected ? Colors.White : CoffeeColors.TextSecondary))
			)
			.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
		)
		.Modifier(CoffeeModifiers.Background(isSelected ? CoffeeColors.Primary : CoffeeColors.SurfaceElevated))
		.Modifier(CoffeeModifiers.StrokeColor(isSelected ? CoffeeColors.Primary : CoffeeColors.Outline))
		.StrokeThickness(1)
		.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusPill))
		.OnTap(_ => toggle());

	View RenderContent()
	{
		// Loading state
		if (State.IsLoading && State.ShotRecords.Count == 0)
		{
			return VStack(CoffeeColors.SpacingS,
				ActivityIndicator(true),
				Text("Loading...")
					.Modifier(CoffeeModifiers.FormValue)
					.Modifier(CoffeeModifiers.TextColor(CoffeeColors.TextSecondary))
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.FillHorizontal()
			.Padding(new Thickness(CoffeeColors.SpacingXL));
		}

		// Error state
		if (State.ErrorMessage != null)
		{
			return FormHelpers.MakeEmptyState(
				Icons.Warning, "Error Loading History", State.ErrorMessage,
				() => LoadShots(refresh: true), "Retry");
		}

		// Empty states
		if (State.ShotRecords.Count == 0)
		{
			if (State.HasActiveFilters)
			{
				return FormHelpers.MakeEmptyState(
					Icons.FilterListOff,
					"No Matching Shots",
					"Try adjusting or clearing your filters",
					() => OnFiltersCleared(),
					"Clear Filters");
			}

			return FormHelpers.MakeEmptyState(
				Icons.Coffee,
				"No Shots Yet",
				"Start logging your espresso shots to see them here");
		}

		// Shot list via CollectionView with virtualization
		var list = new CollectionView<ShotRecord>(() => State.ShotRecords)
		{
			ViewFor = shot => new ShotRecordCard(shot, () =>
				Comet.NavigationView.Navigate(this, new ShotLoggingPage(shot.Id)))
				.Margin(new Thickness(0, 3)),
			ItemsLayout = Comet.ItemsLayout.Vertical(20),
			RemainingItemsThreshold = 5,
			RemainingItemsThresholdReached = () => { if (State.HasMore) LoadMore(); },
		};

		var content = VStack(spacing: 0f);

		// Active filter count header
		if (State.HasActiveFilters)
		{
			var resultText = $"Showing {State.ShotRecords.Count} of {State.TotalShotCount} shots";
			content.Add(Text(resultText)
				.Modifier(CoffeeModifiers.SecondaryText)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Padding(new Thickness(0, CoffeeColors.SpacingXS)));
		}

		content.Add(list.FillHorizontal().FillVertical());

		return content
			.Padding(new Thickness(CoffeeColors.SpacingM, 0));
	}
}
