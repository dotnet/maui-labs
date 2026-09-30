# AppKit Picker native regression

This executable runs a real MAUI `Picker` through the AppKit backend in a native
window, on the AppKit main thread. It requires macOS and the MAUI/macOS workloads; it
is not a portable unit test or a replacement for interactive testing.

Build with `dotnet build platforms/MacOS/tests/PickerRegressionTests -p:UseMaui=false`,
then run the built `PickerRegressionTests.app/Contents/MacOS/PickerRegressionTests`.
Set `PICKER_EVIDENCE` to a writable output directory for native screenshots and
JSONL state observations.

The harness checks the initial unselected/untitled state, selecting and resetting,
adding/removing/empty titles, replacing/clearing/repopulating items, and native
activation with and without a title. Activation sends the control's native action;
it does not simulate opening the menu with a mouse.

Exit 0 means all assertions passed. Exit 42 specifically identifies issue #563's
original mismatch (`SelectedIndex == -1`, no title, native index 0 displaying
January). Other failures, including the 60-second watchdog, exit 1.

The `picker-regression` CI job runs the identical harness against the issue's
pinned original `PickerHandler.cs` and the PR handler. Both builds use identical
current surrounding source and build infrastructure to isolate the handler
change. The job requires the original handler's exact failure and the fixed
version's complete pass, and uploads logs, states, and screenshots.
