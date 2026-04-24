using Comet.Reactive;
using CometBaristaNotes.Services.DTOs;
using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;

namespace CometBaristaNotes.Pages;

// ── Voice chat message model ────────────────────────────────────────
public class VoiceChatMessage
{
	public string Text { get; set; } = "";
	public bool IsUser { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.Now;
}

// ── State ───────────────────────────────────────────────────────────
public class ShotLoggingPageState
{
	// Core extraction parameters
	public decimal DoseIn { get; set; } = 18.0m;
	public string GrindSetting { get; set; } = "5.5";
	public decimal ExpectedTime { get; set; } = 28;
	public decimal ExpectedOutput { get; set; } = 36.0m;
	public decimal? ActualTime { get; set; }
	public decimal? ActualOutput { get; set; }
	public decimal? PreinfusionTime { get; set; }
	public string DrinkType { get; set; } = "Espresso";
	public int SelectedDrinkIndex { get; set; }

	// Rating (0-4 UI index: terrible, bad, average, good, excellent)
	public int Rating { get; set; } = 3;
	public string TastingNotes { get; set; } = "";

	// Bag/Bean selection
	public int? SelectedBagId { get; set; }
	public int SelectedBagIndex { get; set; } = -1;
	public List<Bag> AvailableBags { get; set; } = new();
	public List<Bean> AvailableBeans { get; set; } = new();

	// Equipment
	public List<Equipment> AvailableEquipment { get; set; } = new();
	public int? SelectedMachineId { get; set; }
	public int SelectedMachineIndex { get; set; } = -1;
	public int? SelectedGrinderId { get; set; }
	public int SelectedGrinderIndex { get; set; } = -1;
	public List<int> SelectedAccessoryIds { get; set; } = new();

	// User profiles
	public List<UserProfile> AvailableUsers { get; set; } = new();
	public UserProfile? SelectedMaker { get; set; }
	public UserProfile? SelectedRecipient { get; set; }

	// Edit mode
	public DateTime? Timestamp { get; set; }
	public string? BeanName { get; set; }

	// UI state
	public bool IsLoading { get; set; }
	public string? ErrorMessage { get; set; }

	// AI Advice state
	public bool ShowAdviceSection { get; set; }
	public bool IsLoadingAdvice { get; set; }
	public AIAdviceResponseDto? AdviceResponse { get; set; }
	public bool ShowPromptDetails { get; set; }
	public string? AdviceError { get; set; }

	// Voice overlay state
	public bool IsVoiceSheetOpen { get; set; }
	public bool IsVoiceMinimized { get; set; }
	public bool IsRecording { get; set; }
	public string VoiceTranscript { get; set; } = "";
	public string VoiceState { get; set; } = "Tap to speak";
	public List<VoiceChatMessage> VoiceChatHistory { get; set; } = new();
}

// ── Gauge Arc Drawable ─────────────────────────────────────────────
class GaugeArcDrawable
{
	public float Value { get; set; }
	public float Min { get; set; }
	public float Max { get; set; }
	public string[] ScaleLabels { get; set; }
	public string Unit { get; set; } = "g";
	public Color TrackColor { get; set; } = CoffeeColors.SurfaceVariant;
	public Color ValueColor { get; set; } = CoffeeColors.Primary;
	public Color LabelColor { get; set; } = CoffeeColors.TextSecondary;
	public Color ValueTextColor { get; set; } = CoffeeColors.TextPrimary;
	public Color UnitTextColor { get; set; } = CoffeeColors.TextSecondary;
	public float StrokeWidth { get; set; } = 20f;
	public float Inset { get; set; } = 14f;

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
		var cx = dirtyRect.Width / 2f;
		var cy = dirtyRect.Height / 2f;
		var radius = size / 2f - StrokeWidth / 2f - Inset;

		const float startAngle = 225f;
		const float endAngle = -45f;

		// Track arc (background — full 270°)
		canvas.StrokeColor = TrackColor;
		canvas.StrokeSize = StrokeWidth;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2,
			startAngle, endAngle, true, false);

		// Value arc (foreground — partial)
		var fraction = Math.Clamp((Value - Min) / (Max - Min), 0f, 1f);
		if (fraction > 0.005f)
		{
			float valueEndAngle = startAngle - 270f * fraction;
			canvas.StrokeColor = ValueColor;
			canvas.StrokeSize = StrokeWidth;
			canvas.StrokeLineCap = LineCap.Round;
			canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2,
				startAngle, valueEndAngle, true, false);
		}

		// Scale labels at polar positions around the arc
		if (ScaleLabels is { Length: > 1 })
		{
			float labelR = radius + StrokeWidth / 2f + 6f;
			canvas.FontSize = 12;
			canvas.FontColor = LabelColor;
			float lw = 28f, lh = 14f;
			int count = ScaleLabels.Length;
			for (int i = 0; i < count; i++)
			{
				float deg = startAngle - 270f / (count - 1) * i;
				float rad = deg * MathF.PI / 180f;
				float lx = cx + labelR * MathF.Cos(rad);
				float ly = cy - labelR * MathF.Sin(rad);
				canvas.DrawString(ScaleLabels[i],
					lx - lw / 2f, ly - lh / 2f, lw, lh,
					HorizontalAlignment.Center, VerticalAlignment.Center);
			}
		}

		// Center value text and unit are rendered via overlaid Text views
		// for proper bold weight support (canvas.DrawString lacks FontWeight)
	}
}

// ── Page ────────────────────────────────────────────────────────────
public class ShotLoggingPage : Component<ShotLoggingPageState>
{
	static readonly string[] DrinkTypes = { "Espresso", "Americano", "Latte", "Cappuccino", "Flat White", "Cortado" };
	static readonly string[] RatingLabels = { "Terrible", "Bad", "Average", "Good", "Excellent" };
	static readonly string[] RatingIcons =
	{
		Icons.SentimentVeryDissatisfied,
		Icons.SentimentDissatisfied,
		Icons.SentimentNeutral,
		Icons.SentimentSatisfied,
		Icons.SentimentVerySatisfied,
	};

	readonly int _shotId;
	readonly IDataStore _store;
	bool _dataLoaded;
	int _savedShotId;

	// Signal-based reactive state — updates without full page rebuild
	readonly Signal<double> _doseIn = new(18.0);
	readonly Signal<double> _yieldOut = new(36.0);
	readonly Signal<double> _time = new(40.0);
	readonly Signal<string> _tastingNotes = new("");

	// Cached gauge drawables to avoid recreation on each render (Bug 6)
	GaugeArcDrawable? _doseGaugeDrawable;
	GaugeArcDrawable? _outputGaugeDrawable;

	bool IsEditMode => _shotId > 0;

	IAIAdviceService? _aiService;
	IVoiceCommandService? _voiceService;
	ISpeechRecognitionService? _speechService;
	CancellationTokenSource? _adviceCts;
	CancellationTokenSource? _speechCts;

	public ShotLoggingPage(int shotId = 0)
	{
		_shotId = shotId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void ResolveServices()
	{
		var sp = IPlatformApplication.Current?.Services;
		if (sp == null) return;
		try { _aiService ??= sp.GetService<IAIAdviceService>(); } catch { }
		try { _voiceService ??= sp.GetService<IVoiceCommandService>(); } catch { }
		try { _speechService ??= sp.GetService<ISpeechRecognitionService>(); } catch { }
	}

	// ── Data loading ────────────────────────────────────────────────

	void LoadData()
	{
		if (_dataLoaded) return;
		_dataLoaded = true;

		var store = _store;

		var bags = store.GetAllBags().Where(b => !b.IsComplete).ToList();
		var beans = store.GetAllBeans();
		var equipment = store.GetAllEquipment();
		var users = store.GetAllProfiles();

		var machines = equipment.Where(e => e.Type == EquipmentType.Machine).ToList();
		var grinders = equipment.Where(e => e.Type == EquipmentType.Grinder).ToList();

		if (IsEditMode)
		{
			var shot = store.GetShot(_shotId);
			if (shot == null)
			{
				SetState(s => { s.ErrorMessage = "Shot not found"; s.IsLoading = false; });
				return;
			}

			_tastingNotes.Value = shot.TastingNotes ?? "";
			_doseIn.Value = (double)shot.DoseIn;
			_yieldOut.Value = (double)(shot.ActualOutput ?? shot.ExpectedOutput);
			_time.Value = (double)(shot.ActualTime ?? 40);

			SetState(s => {
				s.AvailableBags = bags;
				s.AvailableBeans = beans;
				s.AvailableEquipment = equipment;
				s.AvailableUsers = users;

				s.Timestamp = shot.Timestamp;
				s.BeanName = shot.BeanName;
				s.DoseIn = shot.DoseIn;
				s.GrindSetting = shot.GrindSetting;
				s.ExpectedTime = shot.ExpectedTime;
				s.ExpectedOutput = shot.ExpectedOutput;
				s.ActualTime = shot.ActualTime;
				s.ActualOutput = shot.ActualOutput;
				s.PreinfusionTime = shot.PreinfusionTime;
				s.Rating = shot.Rating.HasValue ? Math.Max(0, shot.Rating.Value - 1) : 2;
				s.DrinkType = shot.DrinkType;
				s.TastingNotes = shot.TastingNotes ?? "";
				s.SelectedBagId = shot.BagId;
				s.SelectedBagIndex = bags.FindIndex(b => b.Id == shot.BagId);
				s.SelectedDrinkIndex = Array.IndexOf(DrinkTypes, shot.DrinkType);
				if (s.SelectedDrinkIndex < 0) s.SelectedDrinkIndex = 0;

				s.SelectedMaker = shot.MadeById.HasValue ? users.FirstOrDefault(u => u.Id == shot.MadeById.Value) : null;
				s.SelectedRecipient = shot.MadeForId.HasValue ? users.FirstOrDefault(u => u.Id == shot.MadeForId.Value) : null;
				s.SelectedMachineId = shot.MachineId;
				s.SelectedGrinderId = shot.GrinderId;
				s.SelectedMachineIndex = shot.MachineId.HasValue
					? machines.FindIndex(m => m.Id == shot.MachineId.Value) : -1;
				s.SelectedGrinderIndex = shot.GrinderId.HasValue
					? grinders.FindIndex(g => g.Id == shot.GrinderId.Value) : -1;

				s.IsLoading = false;
			});
		}
		else
		{
			SetState(s => {
				s.AvailableBags = bags;
				s.AvailableBeans = beans;
				s.AvailableEquipment = equipment;
				s.AvailableUsers = users;

				// Pre-select first user as maker, second as recipient (matches original UX)
				if (users.Count > 0)
					s.SelectedMaker = users[0];
				if (users.Count > 1)
					s.SelectedRecipient = users[1];

				// Pre-select first machine and grinder (matches original — badge shows count)
				if (machines.Count > 0)
				{
					s.SelectedMachineIndex = 0;
					s.SelectedMachineId = machines[0].Id;
				}
				if (grinders.Count > 0)
				{
					s.SelectedGrinderIndex = 0;
					s.SelectedGrinderId = grinders[0].Id;
				}

				// Pre-select first bag (matches original UX)
				if (bags.Count > 0)
				{
					s.SelectedBagIndex = 0;
					s.SelectedBagId = bags[0].Id;
				}

				s.IsLoading = false;
			});
		}
	}

	// ── Save ────────────────────────────────────────────────────────

	void SaveShot()
	{
		var store = _store;

		if (State.SelectedBagId == null)
		{
			SetState(s => s.ErrorMessage = "Please select a bag");
			return;
		}

		var record = new ShotRecord
		{
			BagId = State.SelectedBagId.Value,
			MachineId = State.SelectedMachineId,
			GrinderId = State.SelectedGrinderId,
			MadeById = State.SelectedMaker?.Id,
			MadeForId = State.SelectedRecipient?.Id,
			DoseIn = (decimal)_doseIn.Value,
			GrindSetting = State.GrindSetting,
			ExpectedTime = State.ExpectedTime,
			ExpectedOutput = State.ExpectedOutput,
			ActualTime = (decimal)_time.Value,
			ActualOutput = (decimal)_yieldOut.Value,
			PreinfusionTime = State.PreinfusionTime,
			DrinkType = State.DrinkType,
			Rating = State.Rating + 1,
			TastingNotes = string.IsNullOrWhiteSpace(_tastingNotes.Value) ? null : _tastingNotes.Value,
		};

		if (IsEditMode)
		{
			record.Id = _shotId;
			store.UpdateShot(record);
			_savedShotId = _shotId;
		}
		else
		{
			var created = store.CreateShot(record);
			_savedShotId = created.Id;
		}

		SetState(s => {
			s.ErrorMessage = null;
			s.ShowAdviceSection = IsEditMode;
		});

		if (IsEditMode)
			Navigation?.Pop();
		else
		{
			_dataLoaded = false;
			_tastingNotes.Value = "";
			_doseIn.Value = 18.0;
			_yieldOut.Value = 36.0;
			_time.Value = 40.0;
			SetState(s => {
				s.ActualTime = null;
				s.ActualOutput = null;
				s.PreinfusionTime = null;
				s.TastingNotes = "";
				s.Rating = 3;
				s.ErrorMessage = null;
			});
			LoadData();
		}
	}

	// ── AI Advice ───────────────────────────────────────────────────

	async void RequestAdvice()
	{
		ResolveServices();
		if (_aiService == null) return;

		var shotId = IsEditMode ? _shotId : _savedShotId;
		if (shotId <= 0) return;

		_adviceCts?.Cancel();
		_adviceCts = new CancellationTokenSource();

		SetState(s => {
			s.IsLoadingAdvice = true;
			s.AdviceResponse = null;
			s.AdviceError = null;
			s.ShowAdviceSection = true;
		});

		try
		{
			var response = await _aiService.GetAdviceForShotAsync(shotId, _adviceCts.Token);
			SetState(s => {
				s.IsLoadingAdvice = false;
				s.AdviceResponse = response;
				s.AdviceError = response.Success ? null : response.ErrorMessage;
			});
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			SetState(s => {
				s.IsLoadingAdvice = false;
				s.AdviceError = ex.Message;
			});
		}
	}

	// ── Voice ───────────────────────────────────────────────────────

	void OpenVoiceSheet()
	{
		ResolveServices();
		_voiceService?.ClearConversationHistory();
		SetState(s => {
			s.IsVoiceSheetOpen = true;
			s.IsVoiceMinimized = false;
			s.VoiceState = "Tap to speak";
			s.VoiceTranscript = "";
			s.VoiceChatHistory = new List<VoiceChatMessage>();
		});
	}

	void CloseVoiceSheet()
	{
		_speechCts?.Cancel();
		SetState(s => {
			s.IsVoiceSheetOpen = false;
			s.IsVoiceMinimized = false;
			s.IsRecording = false;
		});
	}

	void MinimizeVoiceSheet()
	{
		SetState(s => s.IsVoiceMinimized = true);
	}

	void RestoreVoiceSheet()
	{
		SetState(s => s.IsVoiceMinimized = false);
	}

	async void ToggleRecording()
	{
		if (_speechService == null) return;

		if (State.IsRecording)
		{
			await _speechService.StopListeningAsync();
			SetState(s => { s.IsRecording = false; s.VoiceState = "Processing..."; });
			return;
		}

		_speechCts?.Cancel();
		_speechCts = new CancellationTokenSource();

		SetState(s => { s.IsRecording = true; s.VoiceState = "Listening..."; s.VoiceTranscript = ""; });

		try
		{
			var result = await _speechService.StartListeningAsync(_speechCts.Token);
			SetState(s => { s.IsRecording = false; s.VoiceState = "Processing..."; });

			if (result.Success && !string.IsNullOrWhiteSpace(result.Transcript))
			{
				SetState(s => s.VoiceChatHistory.Add(new VoiceChatMessage
				{
					Text = result.Transcript!,
					IsUser = true
				}));

				await ProcessVoiceCommand(result.Transcript!, result.Confidence);
			}
			else
			{
				SetState(s => s.VoiceState = result.ErrorMessage ?? "Could not understand. Try again.");
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			SetState(s => {
				s.IsRecording = false;
				s.VoiceState = $"Error: {ex.Message}";
			});
		}
	}

	async Task ProcessVoiceCommand(string transcript, double confidence)
	{
		if (_voiceService == null) return;

		try
		{
			var request = new VoiceCommandRequestDto(
				transcript, confidence,
				State.SelectedBagId,
				State.SelectedMachineId,
				State.SelectedMaker?.Id);

			var toolResult = await _voiceService.ProcessCommandAsync(request);

			SetState(s => {
				s.VoiceChatHistory.Add(new VoiceChatMessage
				{
					Text = toolResult.Message,
					IsUser = false
				});
				s.VoiceState = "Tap to speak";
			});

			if (toolResult.Success)
			{
				_dataLoaded = false;
				LoadData();
			}
		}
		catch (Exception ex)
		{
			SetState(s => {
				s.VoiceChatHistory.Add(new VoiceChatMessage
				{
					Text = $"Error: {ex.Message}",
					IsUser = false
				});
				s.VoiceState = "Tap to speak";
			});
		}
	}

	// ── Render ───────────────────────────────────────────────────────

	public override View Render()
	{
		ResolveServices();
		LoadData();

		return ZStack
		(
			RenderFormContent(),
			(State.IsVoiceSheetOpen && State.IsVoiceMinimized) ? RenderVoiceFAB() : null,
			(State.IsVoiceSheetOpen && !State.IsVoiceMinimized) ? RenderVoiceOverlay() : null
		)
			.Modifier(CoffeeModifiers.PageContainer)
			.ToolbarItems(
				new Comet.ToolbarItem { IconGlyph = "mic.fill", OnClicked = () => OpenVoiceSheet() },
				new Comet.ToolbarItem { IconGlyph = "camera.fill", OnClicked = () => { /* open camera */ } })
			.Title(IsEditMode ? "Edit Shot" : "New Shot");
	}

	// ── Main form (matches original BaristaNotes layout) ────────────

	View RenderFormContent()
	{
		var bagNames = State.AvailableBags.Select(b => b.BeanName ?? $"Bag #{b.Id}").ToArray();
		var bagPickerIndex = State.SelectedBagIndex >= 0 ? State.SelectedBagIndex : 0;

		var machines = State.AvailableEquipment.Where(e => e.Type == EquipmentType.Machine).ToList();
		var grinders = State.AvailableEquipment.Where(e => e.Type == EquipmentType.Grinder).ToList();
		var accessories = State.AvailableEquipment
			.Where(e => e.Type != EquipmentType.Machine && e.Type != EquipmentType.Grinder).ToList();

		var machineNames = machines.Select(m => m.Name).ToArray();
		var grinderNames = grinders.Select(g => g.Name).ToArray();

		var machineIdx = State.SelectedMachineIndex >= 0 ? State.SelectedMachineIndex : 0;
		var grinderIdx = State.SelectedGrinderIndex >= 0 ? State.SelectedGrinderIndex : 0;

		var formChildren = new List<View>();

		// ── Edit mode header ──
		if (IsEditMode && State.BeanName != null)
		{
			formChildren.Add(
				FormHelpers.MakeCard(
					VStack(4,
						Text(State.BeanName ?? "Unknown Bean")
							.Modifier(CoffeeModifiers.TitleSmall),
						Text(State.Timestamp?.ToString("MMM d, yyyy h:mm tt") ?? "")
							.Modifier(CoffeeModifiers.SmallText)
					)
				)
			);
		}

		// ── Error display ──
		if (State.ErrorMessage != null)
		{
			formChildren.Add(
				Border(
					Text(State.ErrorMessage)
						.Modifier(CoffeeModifiers.BodyError)
						.Padding(new Thickness(CoffeeColors.SpacingM))
				)
				.Modifier(CoffeeModifiers.ErrorCard)
			);
		}

		// ═══════════════════════════════════════════════════════════
		// HERO SECTION — above the fold, matches original layout
		// ═══════════════════════════════════════════════════════════

		// Dose / Equipment / Output gauges
		formChildren.Add(RenderDoseGauges());

		// Time slider
		formChildren.Add(RenderTimeSlider());

		// User avatars (Made by → For)
		if (State.AvailableUsers.Count > 0)
			formChildren.Add(RenderUserSelectionRow());

		// Rating sentiment faces
		formChildren.Add(RenderRatingSelector());

		// Tasting Notes — Signal-bound TextEditor, no SetState needed
		formChildren.Add(
			VStack(CoffeeColors.SpacingS,
				Text("Tasting Notes (optional)")
					.Modifier(CoffeeModifiers.FormLabel),
				Border(
					SignalExtensions.TextEditor(_tastingNotes)
						.Modifier(CoffeeModifiers.FormEditor(100f, new Thickness(CoffeeColors.SpacingS, 0)))
						.Placeholder("E.g., bright, fruity, slightly sour...")
						.PlaceholderColor(CoffeeColors.TextMuted)
				)
				.Modifier(CoffeeModifiers.FormEditorContainer)
			)
		);

		// Save / Add button — primary, full width
		formChildren.Add(
			FormHelpers.MakePrimaryButton(IsEditMode ? "Update Shot" : "Add Shot", SaveShot));

		// ═══════════════════════════════════════════════════════════
		// ADDITIONAL DETAILS — below the fold
		// ═══════════════════════════════════════════════════════════

		// Divider
		formChildren.Add(
		new Comet.BoxView(CoffeeColors.Outline)
		.Modifier(CoffeeModifiers.Divider)
		.Margin(new Thickness(0, CoffeeColors.SpacingL, 0, 0)));

		// Section label
		formChildren.Add(
		Text("Additional Details")
		.Modifier(CoffeeModifiers.MutedText)
		.Margin(new Thickness(0, CoffeeColors.SpacingS, 0, CoffeeColors.SpacingS)));

		// Bag picker
		if (State.AvailableBags.Count > 0)
		{
			formChildren.Add(
			FormHelpers.MakeFormPicker("Bag", bagPickerIndex, bagNames,
			idx => SetState(s => {
				s.SelectedBagIndex = idx;
				s.SelectedBagId = idx >= 0 && idx < s.AvailableBags.Count
	? s.AvailableBags[idx].Id : null;
			})));
		}
		else
		{
			formChildren.Add(
			FormHelpers.MakeEmptyState(Icons.Coffee, "No Bags",
			"Add a bean and bag in Settings first",
			null, null, Icons.FontFamily));
		}

		// Grind Setting
		formChildren.Add(
		FormHelpers.MakeFormEntry("Grind Setting", State.GrindSetting, "5.5",
		v => SetState(s => s.GrindSetting = v)));

		// Expected Time
		formChildren.Add(
		FormHelpers.MakeFormEntry("Expected Time (s)", State.ExpectedTime.ToString("F1"), "28",
		v => { if (decimal.TryParse(v, out var d)) SetState(s => s.ExpectedTime = d); }));

		// Expected Output
		formChildren.Add(
		FormHelpers.MakeFormEntry("Expected Output (g)", State.ExpectedOutput.ToString("F1"), "36.0",
		v => { if (decimal.TryParse(v, out var d)) SetState(s => s.ExpectedOutput = d); }));

		// Drink Type
		formChildren.Add(
		FormHelpers.MakeFormPicker("Drink Type", State.SelectedDrinkIndex, DrinkTypes,
		idx => SetState(s => {
			s.SelectedDrinkIndex = idx;
			s.DrinkType = idx >= 0 && idx < DrinkTypes.Length ? DrinkTypes[idx] : "Espresso";
		})));

		// Equipment is selected via the Equipment toggle button in the hero section,
		// not via form pickers — matches original BaristaNotes design

		// Accessories section removed — not in original design

		// AI Advice (edit mode or after save)
		if (IsEditMode || _savedShotId > 0)
			formChildren.Add(RenderAdviceSection());

		// Tab bar + FAB dead space — allows Add Shot button to scroll fully above tab bar
		formChildren.Add(new Spacer().Modifier(CoffeeModifiers.FrameHeight(160f)));

		var stack = VStack(CoffeeColors.SpacingM);
		foreach (var child in formChildren)
			stack.Add(child);

		return ScrollView(
		stack.Padding(new Thickness(CoffeeColors.SpacingM, 0, CoffeeColors.SpacingM, CoffeeColors.SpacingXL))
		);
	}

	// ── Dose / Output Gauges ────────────────────────────────────────

	View RenderDoseGauges()
	{
		var equipCount = (State.SelectedMachineId.HasValue ? 1 : 0) +
		(State.SelectedGrinderId.HasValue ? 1 : 0) +
		State.SelectedAccessoryIds.Count;

		var doseVal = (decimal)_doseIn.Value;
		var outputVal = (decimal)_yieldOut.Value;
		var ratio = doseVal > 0 && outputVal > 0 ? outputVal / doseVal : 0m;

		return new Comet.Grid(
		columns: new object[] { "*", "Auto", "*" },
		rows: new object[] { "Auto", "Auto" })
{
// Dose In (left) — range 15–20 matches original scale
RenderSingleGauge(_doseIn, 15.0, 20.0, 0.1,
Icons.CupIn, Icons.CoffeeFontFamily, ref _doseGaugeDrawable)
.Cell(row: 0, column: 0),

// Equipment button (center)
RenderEquipmentButton(equipCount)
.Cell(row: 0, column: 1),

// Yield Out (right)
RenderSingleGauge(_yieldOut, 25.0, 50.0, 0.5,
Icons.CupOut, Icons.CoffeeFontFamily, ref _outputGaugeDrawable)
.Cell(row: 0, column: 2),

// Ratio (centered below)
Text($"1:{ratio:F1}")
.Modifier(CoffeeModifiers.ValueText)
.Modifier(CoffeeModifiers.TextColor(CoffeeColors.TextSecondary))
.HorizontalTextAlignment(TextAlignment.Center)
.Margin(new Thickness(0, CoffeeColors.SpacingXS, 0, 0))
.Cell(row: 1, column: 0).GridColumnSpan(3)
};
	}

	View RenderSingleGauge(Signal<double> signal, double min, double max, double step,
	string iconGlyph, string iconFont, ref GaugeArcDrawable? cached)
	{
		const float gaugeSize = 115f;
		var value = signal.Value;

		var range = max - min;
		var interval = range / 5.0;
		var labels = new string[6];
		for (int i = 0; i <= 5; i++)
			labels[i] = (min + interval * i).ToString("F0");

		if (cached == null)
		{
			cached = new GaugeArcDrawable
			{
				Min = (float)min,
				Max = (float)max,
				ScaleLabels = labels,
				Unit = "g",
				TrackColor = CoffeeColors.SurfaceVariant,
				ValueColor = CoffeeColors.Primary,
				LabelColor = CoffeeColors.TextSecondary,
				ValueTextColor = CoffeeColors.TextPrimary,
				UnitTextColor = CoffeeColors.TextSecondary,
			};
		}
		cached.Value = (float)value;

		var drawable = cached;
		var gv = new Comet.GraphicsView { Draw = drawable.Draw };

		// ZStack: arc drawing + bold text overlay (canvas.DrawString lacks FontWeight)
		var gaugeView = new ZStack
{
	gv.Frame(gaugeSize, gaugeSize),
	VStack(spacing: 0f,
		Text(() => $"{signal.Value:F1}")
			.Modifier(CoffeeModifiers.Headline)
			.HorizontalTextAlignment(TextAlignment.Center),
		Text("g")
			.Modifier(CoffeeModifiers.MicroText)
			.HorizontalTextAlignment(TextAlignment.Center)
	).Alignment(Alignment.Center)
}.Frame(gaugeSize, gaugeSize);

		return VStack(spacing: 0,
			gaugeView,
			HStack(CoffeeColors.SpacingL,
				MakeStepperButton(Icons.Remove,
					() => signal.Value = Math.Round(Math.Max(min, signal.Value - step), 1)),
				Text(iconGlyph)
					.Modifier(CoffeeModifiers.IconFont(24, iconFont, CoffeeColors.TextSecondary)),
				MakeStepperButton(Icons.Add,
					() => signal.Value = Math.Round(Math.Min(max, signal.Value + step), 1))
			)
		);
	}

	View MakeStepperButton(string icon, Action action) =>
	Text(icon)
	.Modifier(CoffeeModifiers.IconMedium(CoffeeColors.TextSecondary))
	.Modifier(CoffeeModifiers.FrameSize(32f, 32f))
	.OnTap(_ => action());

	View RenderEquipmentButton(int selectedCount)
	{
		var children = new List<View>
		{
			Border(
				Text(Icons.Machine)
					.Modifier(CoffeeModifiers.IconFont(28, Icons.CoffeeFontFamily, CoffeeColors.TextSecondary))
			)
			.Modifier(CoffeeModifiers.SurfaceVariantField)
			.Modifier(CoffeeModifiers.FrameSize(44f, 44f))
		};//children

		if (selectedCount > 0)
		{
			children.Add(
			Border(
			Text(selectedCount.ToString())
			.Modifier(CoffeeModifiers.BadgeText)
			.HorizontalTextAlignment(TextAlignment.Center)
			.VerticalTextAlignment(TextAlignment.Center)
			)
			.Modifier(CoffeeModifiers.CornerRadius(8))
			.Modifier(CoffeeModifiers.Background(CoffeeColors.Primary))
			.StrokeThickness(0)
			.Modifier(CoffeeModifiers.FrameSize(16f, 16f))
			.Alignment(Alignment.TopTrailing)
			);
		}

		var stack = new ZStack();
		foreach (var c in children)
			stack.Add(c);

		return stack
		.Modifier(CoffeeModifiers.FrameSize(50f, 50f))
		.OnTap(_ => ShowEquipmentPopup());
	}

	void ShowEquipmentPopup()
	{
		var machines = State.AvailableEquipment.Where(e => e.Type == EquipmentType.Machine).ToList();
		var grinders = State.AvailableEquipment.Where(e => e.Type == EquipmentType.Grinder).ToList();

		var machineNames = machines.Select(m => m.Name).ToArray();
		var grinderNames = grinders.Select(g => g.Name).ToArray();

		var machineIdx = State.SelectedMachineIndex >= 0 ? State.SelectedMachineIndex : 0;
		var grinderIdx = State.SelectedGrinderIndex >= 0 ? State.SelectedGrinderIndex : 0;

		var children = new List<View>();

		children.Add(
		Text("Equipment")
		.Modifier(CoffeeModifiers.SubHeadline)
		.HorizontalTextAlignment(TextAlignment.Center));

		if (machineNames.Length > 0)
		{
			children.Add(
			FormHelpers.MakeFormPicker("Machine", machineIdx, machineNames,
			idx => SetState(s => {
				s.SelectedMachineIndex = idx;
				s.SelectedMachineId = idx >= 0 && idx < machines.Count ? machines[idx].Id : null;
			})));
		}

		if (grinderNames.Length > 0)
		{
			children.Add(
			FormHelpers.MakeFormPicker("Grinder", grinderIdx, grinderNames,
			idx => SetState(s => {
				s.SelectedGrinderIndex = idx;
				s.SelectedGrinderId = idx >= 0 && idx < grinders.Count ? grinders[idx].Id : null;
			})));
		}

		children.Add(
		FormHelpers.MakePrimaryButton("Done", () => ModalView.Dismiss()));

		var stack = VStack(CoffeeColors.SpacingM);
		foreach (var child in children)
			stack.Add(child);

		var popup = stack
		.Padding(new Thickness(CoffeeColors.SpacingL))
		.Modifier(CoffeeModifiers.Background(CoffeeColors.Surface));

		ModalView.Present(popup);
	}

	// ── Time Slider ─────────────────────────────────────────────────

	View RenderTimeSlider()
	{
		var timeVal = _time.Value;
		return new Comet.Grid(
			columns: new object[] { "*" },
			rows: new object[] { "Auto", "50" })
			{
				// Row 0: Label
				Text(() => $"Time: {_time.Value:F0}s")
					.Modifier(CoffeeModifiers.FormLabel)
					.Margin(new Thickness(CoffeeColors.SpacingM, 0, 0, CoffeeColors.SpacingS))
					.Cell(row: 0, column: 0),

				// Row 1: Rounded capsule background
				Border(new Spacer())
					.Modifier(CoffeeModifiers.SurfaceVariantField)
					.Modifier(CoffeeModifiers.FrameHeight(50f))
					.Cell(row: 1, column: 0),

				// Row 1: Slider bound directly to signal — no SetState needed
				SignalExtensions.Slider(_time, 0, 60)
					.Modifier(CoffeeModifiers.FormSlider(
						CoffeeColors.Primary,
						Colors.Transparent,
						CoffeeColors.Primary,
						margin: new Thickness(CoffeeColors.SpacingM, 0)))
					.Cell(row: 1, column: 0)
};
	}

	// ── User Selection ──────────────────────────────────────────────

	const string ArrowForward = "\ue5c8";

	View RenderUserSelectionRow()
	{
		var content = HStack(CoffeeColors.SpacingM,
			RenderUserAvatar(State.SelectedMaker, "Made by",
				() => CycleUser(u => SetState(s => s.SelectedMaker = u), State.SelectedMaker)),

			Text(ArrowForward)
				.Modifier(CoffeeModifiers.IconLarge(CoffeeColors.TextSecondary))
				.Frame(width: 32f),

			RenderUserAvatar(State.SelectedRecipient, "For",
				() => CycleUser(u => SetState(s => s.SelectedRecipient = u), State.SelectedRecipient))
		);
		// Center the group by wrapping in a full-width container
		return HStack(
			new Spacer(),
			content,
			new Spacer()
		).FillHorizontal();
	}

	View RenderUserAvatar(UserProfile? user, string label, Action onTapped)
	{
		const float avatarSize = 60f;
		const float avatarRadius = 30f;
		View avatar;

		if (user != null && !string.IsNullOrEmpty(user.AvatarPath))
		{
			var imagePath = System.IO.Path.Combine(FileSystem.AppDataDirectory, user.AvatarPath);
			if (System.IO.File.Exists(imagePath))
			{
				avatar = Border(
					Image(imagePath)
						.Aspect(Aspect.AspectFill)
						.Modifier(CoffeeModifiers.FrameSize(avatarSize, avatarSize))
				)
				.Modifier(CoffeeModifiers.AvatarBorder(avatarSize, avatarRadius, CoffeeColors.SurfaceVariant, Colors.LightGray));
			}
			else
			{
				avatar = RenderIconAvatar(user != null ? CoffeeColors.TextPrimary : CoffeeColors.TextSecondary,
					CoffeeColors.SurfaceVariant, avatarSize, avatarRadius);
			}
		}
		else
		{
			avatar = RenderIconAvatar(
				user != null ? CoffeeColors.TextPrimary : CoffeeColors.TextSecondary,
				CoffeeColors.SurfaceVariant, avatarSize, avatarRadius);
		}

		return VStack(4,
		avatar,
		Text(label)
		.Modifier(CoffeeModifiers.FormLabel)
		.HorizontalTextAlignment(TextAlignment.Center)
		).OnTap(_ => onTapped());
	}

	View RenderIconAvatar(Color iconColor, Color bgColor, float size, float radius)
	{
		return Border(
		Text(Icons.AccountCircle)
			.Modifier(CoffeeModifiers.Icon(36, iconColor))
		)
		.Modifier(CoffeeModifiers.AvatarBorder(size, radius, bgColor, Colors.LightGray));
	}

	void CycleUser(Action<UserProfile?> setter, UserProfile? current)
	{
		var users = State.AvailableUsers;
		if (users.Count == 0) return;

		if (current == null)
			setter(users.First());
		else
		{
			var idx = users.FindIndex(u => u.Id == current.Id);
			if (idx >= 0 && idx < users.Count - 1)
				setter(users[idx + 1]);
			else
				setter(null);
		}
	}

	// ── Rating ──────────────────────────────────────────────────────

	View RenderRatingSelector()
	{
		var chips = new View[RatingIcons.Length];
		for (int i = 0; i < RatingIcons.Length; i++)
		{
			var idx = i;
			var isSelected = State.Rating == idx;
			chips[i] = Text(RatingIcons[idx])
			.Modifier(CoffeeModifiers.EmojiChip(isSelected ? CoffeeColors.Primary : CoffeeColors.TextMuted))
			.OnTap(_ => SetState(s => s.Rating = idx));
		}
		return HStack(
		new Spacer(),
		HStack(spacing: CoffeeColors.SpacingS, chips),
		new Spacer()
		);
	}

	// ── Accessory chips ─────────────────────────────────────────────

	View BuildAccessoryChips(List<Equipment> accessories)
	{
		var chips = new List<View>();
		foreach (var acc in accessories)
		{
			var id = acc.Id;
			var isSelected = State.SelectedAccessoryIds.Contains(id);

			chips.Add(
			Border(
			Text(acc.Name)
			.Modifier(CoffeeModifiers.SmallText)
			.Modifier(CoffeeModifiers.TextColor(isSelected ? Colors.White : CoffeeColors.TextPrimary))
			.HorizontalTextAlignment(TextAlignment.Center)
			.VerticalTextAlignment(TextAlignment.Center)
			.Padding(new Thickness(CoffeeColors.SpacingS, CoffeeColors.SpacingXS))
			)
			.Modifier(CoffeeModifiers.PillChip(isSelected ? CoffeeColors.Primary : CoffeeColors.SurfaceVariant))
			.OnTap(_ => {
				SetState(s => {
					if (s.SelectedAccessoryIds.Contains(id))
						s.SelectedAccessoryIds.Remove(id);
					else
						s.SelectedAccessoryIds.Add(id);
				});
			})
			);
		}

		var row = HStack(CoffeeColors.SpacingXS);
		foreach (var c in chips)
			row.Add(c);
		return row;
	}


	// ── AI Advice section ───────────────────────────────────────────

	View RenderAdviceSection()
	{
		var children = new List<View>();

		children.Add(FormHelpers.MakeSectionHeader("AI Advice"));

		// Get Advice button
		children.Add(
			Border(
				HStack(CoffeeColors.SpacingS,
					FormHelpers.MakeIcon(Icons.MagicButton, 20, CoffeeColors.Primary),
					Text("Get AI Advice")
						.Modifier(CoffeeModifiers.ValueText)
						.Modifier(CoffeeModifiers.TextColor(CoffeeColors.Primary))
						.VerticalTextAlignment(TextAlignment.Center)
				).Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
			)
			.Modifier(CoffeeModifiers.OutlinePill(CoffeeColors.Primary))
			.OnTap(_ => RequestAdvice())
		);

		// Loading
		if (State.IsLoadingAdvice)
		{
			children.Add(
				FormHelpers.MakeCard(
					HStack(CoffeeColors.SpacingM,
						ActivityIndicator(true)
							.Modifier(CoffeeModifiers.TextColor(CoffeeColors.Primary))
							.Frame(width: 24, height: 24),
						Text("Analyzing your shot...")
							.Modifier(CoffeeModifiers.SecondaryText)
							.VerticalTextAlignment(TextAlignment.Center)
					)
				)
			);
		}

		// Error
		if (State.AdviceError != null && !State.IsLoadingAdvice)
		{
			children.Add(
				FormHelpers.MakeCard(
					VStack(CoffeeColors.SpacingS,
						HStack(CoffeeColors.SpacingS,
							FormHelpers.MakeIcon(Icons.Error, 20, CoffeeColors.Error),
							Text(State.AdviceError)
								.Modifier(CoffeeModifiers.Body)
								.Modifier(CoffeeModifiers.TextColor(CoffeeColors.Error))
								.VerticalTextAlignment(TextAlignment.Center)
						),
						FormHelpers.MakeSecondaryButton("Retry", RequestAdvice)
					)
				)
			);
		}

		// Results
		if (State.AdviceResponse is { Success: true } advice && !State.IsLoadingAdvice)
		{
			var adjustmentViews = new List<View>();

			foreach (var adj in advice.Adjustments)
			{
				adjustmentViews.Add(
					Border(
						VStack(CoffeeColors.SpacingXS,
							HStack(CoffeeColors.SpacingS,
								Text(adj.Parameter.ToUpperInvariant())
									.Modifier(CoffeeModifiers.LabelStrong)
									.Modifier(CoffeeModifiers.TextColor(CoffeeColors.Primary)),
								Text($"{adj.Direction} {adj.Amount}")
									.Modifier(CoffeeModifiers.Body)
							)
						).Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
					)
					.Modifier(CoffeeModifiers.SurfaceVariantCard)
				);
			}

			if (!string.IsNullOrWhiteSpace(advice.Reasoning))
			{
				adjustmentViews.Add(
					Text(advice.Reasoning)
						.Modifier(CoffeeModifiers.SmallText)
						.Margin(new Thickness(0, CoffeeColors.SpacingXS, 0, 0)));
			}

			// Source indicator
			if (!string.IsNullOrWhiteSpace(advice.Source))
			{
				adjustmentViews.Add(
					Text($"Source: {advice.Source}")
						.Modifier(CoffeeModifiers.TinyText));
			}

			// Prompt transparency toggle
			adjustmentViews.Add(
				FormHelpers.MakeToggleRow("Show prompt details", State.ShowPromptDetails,
					v => SetState(s => s.ShowPromptDetails = v)));

			if (State.ShowPromptDetails && !string.IsNullOrWhiteSpace(advice.PromptSent))
			{
				adjustmentViews.Add(
					Border(
						Text(advice.PromptSent)
							.Modifier(CoffeeModifiers.TinyText)
							.Padding(new Thickness(CoffeeColors.SpacingS))
					)
					.Modifier(CoffeeModifiers.SurfaceVariantCard)
				);
			}

			var adviceStack = VStack(CoffeeColors.SpacingS);
			foreach (var v in adjustmentViews)
				adviceStack.Add(v);

			children.Add(FormHelpers.MakeCard(adviceStack));
		}

		var section = VStack(CoffeeColors.SpacingS);
		foreach (var c in children)
			section.Add(c);
		return section;
	}

	// ── Voice FAB ───────────────────────────────────────────────────

	View RenderVoiceFAB()
	{
		return Border(
			FormHelpers.MakeIcon(Icons.Mic, 28, Colors.White)
		)
		.Modifier(CoffeeModifiers.FloatingActionButton)
		.Margin(new Thickness(0, 0, CoffeeColors.SpacingM, CoffeeColors.SpacingXL))
		.Alignment(Alignment.BottomTrailing)
		.OnTap(_ => RestoreVoiceSheet());
	}

	// ── Voice overlay (bottom sheet) ────────────────────────────────

	View RenderVoiceOverlay()
	{
		var chatViews = new List<View>();
		foreach (var msg in State.VoiceChatHistory)
		{
			chatViews.Add(
				Border(
					Text(msg.Text)
						.Modifier(CoffeeModifiers.Body)
						.Modifier(CoffeeModifiers.TextColor(msg.IsUser ? Colors.White : CoffeeColors.TextPrimary))
						.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
				)
				.Modifier(CoffeeModifiers.ChatBubble(msg.IsUser ? CoffeeColors.Primary : CoffeeColors.Surface))
				.Margin(new Thickness(
					msg.IsUser ? 60 : 0,
					CoffeeColors.SpacingXS,
					msg.IsUser ? 0 : 60,
					CoffeeColors.SpacingXS))
			);
		}

		var chatStack = VStack(spacing: 0f);
		foreach (var v in chatViews)
			chatStack.Add(v);

		// Mic button appearance
		var micColor = State.IsRecording ? CoffeeColors.Error : CoffeeColors.Primary;
		var micSize = State.IsRecording ? 36f : 28f;

		var sheet = VStack(CoffeeColors.SpacingM,
			// Handle bar
			Border(new Spacer())
				.Modifier(CoffeeModifiers.CornerRadius(2))
				.Modifier(CoffeeModifiers.Background(CoffeeColors.TextMuted))
				.Modifier(CoffeeModifiers.FrameSize(40, 4))
				.Margin(new Thickness(0, CoffeeColors.SpacingS, 0, 0)),

			// Close button row (with minimize to its left)
			HStack(
				new Spacer(),
				Border(
					FormHelpers.MakeIcon(Icons.Remove, 20, CoffeeColors.TextPrimary)
				)
				.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusCircular))
				.Modifier(CoffeeModifiers.Background(CoffeeColors.SurfaceVariant))
				.StrokeThickness(0)
				.Modifier(CoffeeModifiers.FrameSize(32, 32))
				.Margin(new Thickness(0, 0, CoffeeColors.SpacingS, 0))
				.OnTap(_ => MinimizeVoiceSheet()),
				Border(
					FormHelpers.MakeIcon(Icons.Close, 20, CoffeeColors.TextPrimary)
				)
				.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusCircular))
				.Modifier(CoffeeModifiers.Background(CoffeeColors.SurfaceVariant))
				.StrokeThickness(0)
				.Modifier(CoffeeModifiers.FrameSize(32, 32))
				.OnTap(_ => CloseVoiceSheet())
			).Padding(new Thickness(CoffeeColors.SpacingM, 0)),

			// Chat history
			ScrollView(chatStack)
				.FillHorizontal()
				.Modifier(CoffeeModifiers.FrameHeight(200)),

			// State text
			Text(State.VoiceState)
				.Modifier(CoffeeModifiers.SecondaryText)
				.HorizontalTextAlignment(TextAlignment.Center),

			// Mic button
			Border(
				FormHelpers.MakeIcon(Icons.Mic, micSize, Colors.White)
			)
			.Modifier(CoffeeModifiers.CornerRadius(CoffeeColors.RadiusCircular))
			.Modifier(CoffeeModifiers.Background(micColor))
			.StrokeThickness(0)
			.Modifier(CoffeeModifiers.FrameSize(64, 64))
			.Margin(new Thickness(0, 0, 0, CoffeeColors.SpacingM))
			.OnTap(_ => ToggleRecording()),

			new Spacer().Modifier(CoffeeModifiers.FrameHeight((float)CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.Background(CoffeeColors.Surface))
		.Modifier(CoffeeModifiers.ClipShape(new RoundedRectangle(CoffeeColors.RadiusCard)))
		.Alignment(Alignment.Bottom);

		// Semi-transparent scrim + sheet
		return new ZStack
		{
			// Scrim
			new Spacer()
				.Modifier(CoffeeModifiers.Background(Colors.Black.WithAlpha(0.4f)))
				.OnTap(_ => CloseVoiceSheet()),
			sheet
		};
	}
}
