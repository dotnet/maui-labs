using Microsoft.Maui.Dispatching;

namespace CometStressTest.Pages;

public class StateTestPageState
{
public int TimerCount { get; set; }
public int RapidCount { get; set; }
public string FirstName { get; set; } = "John";
public string LastName { get; set; } = "Doe";
public double Multiplier { get; set; } = 1.0;
public bool TimerRunning { get; set; }
}

public class StateTestPage : Component<StateTestPageState>
{
IDispatcherTimer? _timer;

public override View Render() => ScrollView(
VStack(12,
Text("State Management Stress Test")
.FontSize(22),

// Multiple state variables
Text("Multi-State Binding:").FontSize(16).Color(Colors.Gray),
TextField(State.FirstName, "First name")
.OnTextChanged(v => SetState(s => s.FirstName = v ?? "")),
TextField(State.LastName, "Last name")
.OnTextChanged(v => SetState(s => s.LastName = v ?? "")),
Text($"Full name: {State.FirstName} {State.LastName}")
.FontSize(16),
Text($"Initials: {(State.FirstName?.Length > 0 ? State.FirstName[0].ToString() : "")}{(State.LastName?.Length > 0 ? State.LastName[0].ToString() : "")}")
.FontSize(14)
.Color(Colors.DarkBlue),

// Computed values
Text("Computed Values:").FontSize(16).Color(Colors.Gray),
Slider(State.Multiplier, 0, 10)
.OnValueChanged(v => SetState(s => s.Multiplier = v)),
Text($"Multiplier: {State.Multiplier:F1}")
.FontSize(14),
Text($"Name length × multiplier = {(State.FirstName?.Length ?? 0 + State.LastName?.Length ?? 0) * State.Multiplier:F1}")
.FontSize(14),

// Timer
Text("Timer Test:").FontSize(16).Color(Colors.Gray),
Text($"Timer: {State.TimerCount}s")
.FontSize(24),
HStack(10,
Button(State.TimerRunning ? "Stop Timer" : "Start Timer", () =>
{
if (State.TimerRunning)
StopTimer();
else
StartTimer();
}),
Button("Reset", () =>
{
StopTimer();
SetState(s => s.TimerCount = 0);
})
),

// Rapid state updates
Text("Thread-Safety Test:").FontSize(16).Color(Colors.Gray),
Text($"Rapid count: {State.RapidCount}")
.FontSize(18),
Button("Fire 100 Rapid Updates", () =>
{
for (int i = 0; i < 100; i++)
{
SetState(s => s.RapidCount++);
}
}),

// Reset all
Button("Reset All State", () =>
{
StopTimer();
SetState(s =>
{
s.TimerCount = 0;
s.RapidCount = 0;
s.FirstName = "John";
s.LastName = "Doe";
s.Multiplier = 1.0;
});
}),

Spacer()
).Padding(16)
);

void StartTimer()
{
if (_timer != null) return;

var dispatcher = Dispatcher.GetForCurrentThread();
if (dispatcher == null) return;

_timer = dispatcher.CreateTimer();
_timer.Interval = TimeSpan.FromSeconds(1);
_timer.Tick += (s, e) => SetState(st => st.TimerCount++);
_timer.Start();
SetState(s => s.TimerRunning = true);
}

void StopTimer()
{
_timer?.Stop();
_timer = null;
SetState(s => s.TimerRunning = false);
}
}
