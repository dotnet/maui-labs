namespace CometBaristaNotes.Pages;

public class BagFormPageState
{
	public string RoastDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
	public string Notes { get; set; } = "";
	public string Error { get; set; } = "";
	public string BeanName { get; set; } = "";
	public bool IsLoaded { get; set; }
	public bool IsSaving { get; set; }
}

public class BagFormPage : Component<BagFormPageState>
{
	readonly int _beanId;
	readonly IDataStore _store;

	public BagFormPage(int beanId = 0)
	{
		_beanId = beanId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadBeanName()
	{
		var bean = _store.GetBean(_beanId);
		SetState(s =>
		{
			s.BeanName = bean?.Name ?? "Unknown Bean";
			s.IsLoaded = true;
		});
	}

	bool Validate()
	{
		if (!DateTime.TryParse(State.RoastDate, out var roastDate))
		{
			SetState(s => s.Error = "Please enter a valid date (yyyy-MM-dd)");
			return false;
		}

		if (roastDate.Date > DateTime.Now.Date)
		{
			SetState(s => s.Error = "Roast date cannot be in the future");
			return false;
		}

		if (!string.IsNullOrEmpty(State.Notes) && State.Notes.Length > 500)
		{
			SetState(s => s.Error = "Notes cannot exceed 500 characters");
			return false;
		}

		return true;
	}

	void Save()
	{
		if (!Validate()) return;

		SetState(s => { s.Error = ""; s.IsSaving = true; });

		_store.CreateBag(new Bag
		{
			BeanId = _beanId,
			RoastDate = DateTime.Parse(State.RoastDate),
			Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
		});

		SetState(s => s.IsSaving = false);
		Comet.NavigationView.Pop(this);
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadBeanName();

		var stack = VStack(CoffeeColors.SpacingM,
			Text($"Add Bag for {State.BeanName}")
				.Modifier(CoffeeModifiers.Headline)
				.Padding(new Thickness(0, 0, 0, CoffeeColors.SpacingS)),
			FormHelpers.MakeReadOnlyField("Bean", State.BeanName),
			FormHelpers.MakeFormEntry("Roast Date", State.RoastDate, "yyyy-MM-dd",
				v => SetState(s => s.RoastDate = v)),
			FormHelpers.MakeFormEntryWithLimit("Notes (optional)", State.Notes,
				"e.g., From Trader Joe's, Gift from friend", 500,
				v => SetState(s => s.Notes = v))
		);

		if (!string.IsNullOrEmpty(State.Error))
		{
			stack.Add(
				Border(
					Text(State.Error)
						.Modifier(CoffeeModifiers.BodyError)
						.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
				)
				.Modifier(CoffeeModifiers.ErrorCard)
			);
		}

		stack.Add(FormHelpers.MakePrimaryButton(
			State.IsSaving ? "Saving..." : "Add Bag", Save));

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title("Add Bag");
	}
}
