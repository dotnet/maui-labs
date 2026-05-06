namespace CometStressTest.Pages;

public class ControlTestPageState
{
public string Name { get; set; } = "";
public string Password { get; set; } = "";
public string SearchText { get; set; } = "";
public bool ToggleValue { get; set; }
public double SliderValue { get; set; } = 0.5;
public int ClickCount { get; set; }
}

public class ControlTestPage : Component<ControlTestPageState>
{
// Stepper and DatePicker lack change-event extensions, so use Reactive<T> for two-way binding
readonly Reactive<double> stepperValue = 5;
readonly Reactive<DateTime?> selectedDate = DateTime.Today;

public override View Render() => ScrollView(
VStack(12,
Text("Control Stress Test")
.FontSize(22),

// TextField
Text("TextField:").FontSize(14).Color(Colors.Gray),
TextField(State.Name, "Enter your name...")
.OnTextChanged(v => SetState(s => s.Name = v ?? "")),
Text($"Hello {State.Name}!")
.FontSize(16),

// SecureField
Text("SecureField:").FontSize(14).Color(Colors.Gray),
SecureField(State.Password, "Enter password...")
.OnTextChanged(v => SetState(s => s.Password = v ?? "")),
Text($"Password length: {State.Password?.Length ?? 0}")
.FontSize(14),

// SearchBar
Text("SearchBar:").FontSize(14).Color(Colors.Gray),
SearchBar(State.SearchText)
.OnTextChanged(v => SetState(s => s.SearchText = v ?? "")),
Text($"Searching: \"{State.SearchText}\"")
.FontSize(14),

// Toggle
Text("Toggle:").FontSize(14).Color(Colors.Gray),
HStack(10,
Toggle(State.ToggleValue)
.OnToggled(v => SetState(s => s.ToggleValue = v)),
Text(State.ToggleValue ? "ON" : "OFF")
.FontSize(16)
),

// Slider + ProgressBar
Text("Slider + ProgressBar:").FontSize(14).Color(Colors.Gray),
Slider(State.SliderValue, 0, 1)
.OnValueChanged(v => SetState(s => s.SliderValue = v)),
ProgressBar(State.SliderValue),
Text($"Value: {State.SliderValue:F2}")
.FontSize(14),

// Stepper
Text("Stepper:").FontSize(14).Color(Colors.Gray),
HStack(10,
Stepper(stepperValue, 0, 20, 1),
Text($"Steps: {stepperValue.Value}")
.FontSize(16)
),

// DatePicker
Text("DatePicker:").FontSize(14).Color(Colors.Gray),
DatePicker(selectedDate),
Text($"Selected: {selectedDate.Value:yyyy-MM-dd}")
.FontSize(14),

// Button with click counter
Text("Button:").FontSize(14).Color(Colors.Gray),
Button($"Clicked {State.ClickCount} times", () =>
{
SetState(s => s.ClickCount++);
}),
Text($"Total clicks: {State.ClickCount}")
.FontSize(14),

Spacer()
).Padding(16)
);
}
