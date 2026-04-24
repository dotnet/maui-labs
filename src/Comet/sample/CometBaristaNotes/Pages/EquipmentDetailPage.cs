namespace CometBaristaNotes.Pages;

public class EquipmentDetailPageState
{
	public string Name { get; set; } = "";
	public int SelectedTypeIndex { get; set; }
	public string Notes { get; set; } = "";
	public bool IsLoaded { get; set; }
	public string Error { get; set; } = "";
}

public class EquipmentDetailPage : Component<EquipmentDetailPageState>
{
	static readonly string[] TypeNames = { "Machine", "Grinder", "Tamper", "Puck Screen", "Other" };
	static readonly EquipmentType[] TypeValues =
		{ EquipmentType.Machine, EquipmentType.Grinder, EquipmentType.Tamper, EquipmentType.PuckScreen, EquipmentType.Other };

	readonly int _equipmentId;
	readonly IDataStore _store;

	public EquipmentDetailPage(int equipmentId = 0)
	{
		_equipmentId = equipmentId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadEquipment()
	{
		if (_equipmentId <= 0) { SetState(s => s.IsLoaded = true); return; }

		var store = _store;

		var eq = store.GetEquipment(_equipmentId);
		if (eq == null)
		{
			SetState(s =>
			{
				s.Error = "Equipment not found";
				s.IsLoaded = true;
			});
			return;
		}

		var typeIndex = Array.IndexOf(TypeValues, eq.Type);
		if (typeIndex < 0) typeIndex = 0;

		SetState(s =>
		{
			s.Name = eq.Name;
			s.SelectedTypeIndex = typeIndex;
			s.Notes = eq.Notes ?? "";
			s.IsLoaded = true;
		});
	}

	void Save()
	{
		if (string.IsNullOrWhiteSpace(State.Name))
		{
			SetState(s => s.Error = "Equipment name is required");
			return;
		}
		SetState(s => s.Error = "");

		var store = _store;

		var typeIdx = State.SelectedTypeIndex;
		var eqType = (typeIdx >= 0 && typeIdx < TypeValues.Length) ? TypeValues[typeIdx] : EquipmentType.Machine;

		if (_equipmentId > 0)
		{
			store.UpdateEquipment(new Equipment
			{
				Id = _equipmentId,
				Name = State.Name,
				Type = eqType,
				Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
				IsActive = true
			});
		}
		else
		{
			store.CreateEquipment(new Equipment
			{
				Name = State.Name,
				Type = eqType,
				Notes = string.IsNullOrWhiteSpace(State.Notes) ? null : State.Notes,
			});
		}

		Navigation?.Pop();
	}

	async void Archive()
	{
		if (_equipmentId <= 0) return;

		var confirm = await Services.PageHelper.DisplayAlertAsync(
			"Archive Equipment?",
			$"Are you sure you want to archive '{State.Name}'? This action cannot be undone.",
			"Archive", "Cancel");
		if (!confirm) return;

		var store = _store;

		store.ArchiveEquipment(_equipmentId);
		Navigation?.Pop();
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadEquipment();

		var isEdit = _equipmentId > 0;

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakeSectionHeader(isEdit ? "EDIT EQUIPMENT" : "NEW EQUIPMENT"),
			FormHelpers.MakeFormEntry("Name *", State.Name, "Equipment name (required)", v => SetState(s => s.Name = v)),
			FormHelpers.MakeFormPicker("Type", State.SelectedTypeIndex, TypeNames, v => SetState(s => s.SelectedTypeIndex = v)),
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

		stack.Add(FormHelpers.MakePrimaryButton(isEdit ? "Save Changes" : "Add Equipment", Save));

		if (isEdit)
			stack.Add(FormHelpers.MakeDangerButton("Archive Equipment", Archive));

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title(isEdit ? "Edit Equipment" : "New Equipment");
	}
}
