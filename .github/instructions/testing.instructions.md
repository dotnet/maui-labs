---
applyTo: "**/*Tests*/**,**/*.Tests.*"
---

# Testing Guide

## Test Framework

- **xUnit** v2.9.3 with `Microsoft.NET.Test.Sdk`
- **coverlet.collector** for code coverage
- No quarantine, outerloop, or flaky test infrastructure (unlike dotnet/aspire or dotnet/maui)

## Test Projects

| Product | Test Project | Target |
|---------|-------------|--------|
| AIExtensions | `tests/AIExtensions/Microsoft.Maui.AI.Attributes.Tests/` | `net10.0` |
| AIExtensions | `tests/AIExtensions/Microsoft.Maui.AI.Attributes.Generators.Tests/` | `net10.0` |
| AppProjectReference | `src/AppProjectReference/Microsoft.Maui.Build.AppProjectReference.Tests/` | `net10.0` |
| Essentials.AI | `tests/AI/Microsoft.Maui.Essentials.AI.UnitTests/` | `net10.0` |
| Essentials.AI | `tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/` | multi-TFM |
| Cli | `src/Cli/Microsoft.Maui.Cli.UnitTests/` | `net10.0` |
| Comet | `src/Comet/tests/Comet.Tests/` | multi-TFM |
| Comet | `src/Comet/tests/Comet.Blazor.Tests/` | multi-TFM |
| Comet | `src/Comet/src/Comet.Layout.Yoga.Tests/` | multi-TFM |
| DevFlow | `src/DevFlow/Microsoft.Maui.DevFlow.Tests/` | `net10.0` |
| DevFlow (client) | `src/DevFlow/Microsoft.Maui.DevFlow.Client.Tests/` | `net10.0` (Windows: `net472;net10.0`) |
| DevFlow (integration) | `src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/` | `net10.0` |
| DevFlow (inspector) | `src/DevFlow/Microsoft.Maui.DevFlow.Inspector.Tests/` | `net10.0` |
| WPF | `platforms/Windows.WPF/tests/HandlerTests/` | `net10.0-windows` |
| WPF | `platforms/Windows.WPF/tests/UITests/` | `net10.0-windows` |

## Running Tests

```bash
# All tests
dotnet test MauiLabs.slnx

# DevFlow tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/

# DevFlow integration tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/

# Cli unit tests
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/

# Specific test
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTestMethod"

# With verbose output
dotnet test --logger "console;verbosity=detailed"
```

## CI Matrix

The matrix is selected by each product workflow that calls
`.github/workflows/_build.yml`; it is not one repository-wide macOS + Windows
matrix.

DevFlow currently uses three separate jobs:

- **Library and test projects:** Windows, with tests enabled, using
  `src/DevFlow/DevFlow.slnf`.
- **JavaScript hosts and VSIX:** Ubuntu, using the npm workspace and Canvas
  tests.
- **Native sample apps:** macOS, with tests disabled, using
  `src/DevFlow/DevFlow.Samples.Native.slnf`. This job proves the app heads
  bundle; it is not a second DevFlow test leg.

The equivalent library/test commands are:

- **Windows:** `eng\common\build.cmd -restore -build -test -ci -configuration Release -prepareMachine -projects <repo>\src\DevFlow\DevFlow.slnf`
- **macOS/Unix syntax:** `./eng/common/build.sh --restore --build --test --ci --configuration Release --prepareMachine --projects <repo>/src/DevFlow/DevFlow.slnf`

Test results are uploaded from `artifacts/TestResults/**/*.xml`.

## Test Patterns

### DevFlow Tests

DevFlow tests use **real Agent.Core code** — they instantiate actual services and test behavior:

```csharp
[Fact]
public void VisualTreeWalker_FindsElementById()
{
    var walker = new VisualTreeWalker();
    // Test with real MAUI types where possible
}
```

### Naming Convention

Use descriptive names that communicate the scenario:

- `MethodName_Condition_ExpectedResult` — e.g., `ParseVersion_InvalidInput_ThrowsArgumentException`
- Or descriptive `[Fact]` — e.g., `Should_return_all_connected_agents_when_multiple_registered`

### Shared AI Backend Coverage

- Use `docs/ai/local-chat-provider.md` as the provider-neutral contract. Put common
  chat and embedding behavior in the existing generic test bases.
- Bind applicable suites using capability hooks; keep native SDK, marshalling,
  OS-version, numeric ranges, and provider error-policy tests platform-specific.
- Preserve existing assertions and `RequiresModel` traits. New fail-fast rejection
  tests need no model; inference tests still require the trait.
- Conservative virtual defaults keep legacy Apple and cloud bindings compiling.
  `SupportsRequiredToolMode` and JSON-suite `RequiresExplicitJsonSchema` use null
  for an unmigrated binding, not support. New bindings must choose true or false.
- Required=false asserts rejection; structured-output=false asserts rejection.
  Rejection hooks opt into validation, not support for all other combinations.
  Explicitly bind media/role, prompt, stateful/background, Activity, and cancellation
  policies; never silently skip an unsupported feature without a capability hook.
- Usage defaults preserve full-count assertions. `IsUsageAvailable=false` asserts
  absent usage; `IsOutputUsageAvailable=false` asserts input-only usage with absent
  output/total counts. Telemetry must match only the supplied token counts.
- Host controls verify the shared harness, not provider implementations. Establish
  Azure/OpenAI compatibility with live cloud tests, not compilation alone.

### What to Test

- **Do test**: Public API methods, edge cases, error handling, serialization/deserialization
- **Do test**: AgentClient methods (they're the public NuGet API surface)
- **Don't test**: Platform-specific overrides (require actual devices/simulators)
- **Don't test**: MCP tool registration (covered by integration at runtime)
