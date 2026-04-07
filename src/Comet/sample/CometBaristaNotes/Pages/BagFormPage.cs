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

	public BagFormPage(int beanId = 0) { _beanId = beanId; }

	void LoadBeanName()
	{
		var store = InMemoryDataStore.Instance;
		if (store != null)
		{
			var bean = store.GetBean(_beanId);
			SetState(s => s.BeanName = bean?.Name ?? "Unknown Bean");
		}
		SetState(s => s.IsLoaded = true);
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

		var store = InMemoryDataStore.Instance;
		if (store == null) return;

		store.CreateBag(new Bag
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

		var items = new List<View>
		{
			// Header
			Text($"Add Bag for {State.BeanName}")
				.Modifier(CoffeeModifiers.Headline)
				.Padding(new Thickness(0, 0, 0, CoffeeColors.SpacingS)),

			// Bean name (read-only)
			FormHelpers.MakeReadOnlyField("Bean", State.BeanName),

			// Roast Date
			FormHelpers.MakeFormEntry("Roast Date", State.RoastDate, "yyyy-MM-dd",
				v => SetState(s => s.RoastDate = v)),

			// Notes with char limit
			FormHelpers.MakeFormEntryWithLimit("Notes (optional)", State.Notes,
				"e.g., From Trader Joe's, Gift from friend", 500,
				v => SetState(s => s.Notes = v)),
		};

		// Validation error
		if (!string.IsNullOrEmpty(State.Error))
		{
			items.Add(
				Border(
					Text(State.Error)
						.Modifier(CoffeeModifiers.BodyError)
						.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
				)
				.Modifier(CoffeeModifiers.ErrorCard)
			);
		}

		// Add Bag button
		items.Add(FormHelpers.MakePrimaryButton(
			State.IsSaving ? "Saving..." : "Add Bag", Save));

		var stack = VStack(CoffeeColors.SpacingM);
		foreach (var item in items) stack.Add(item);

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title("Add Bag");
	}
}
