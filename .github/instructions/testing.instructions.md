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
| DevFlow (integration) | `src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/` | `net10.0` |
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

The matrix is per product (`.github/workflows/ci-{product}.yml` calling `_build.yml`), defaulting
to **macOS and Windows**. DevFlow pins `os: '["windows-latest"]'`, so its tests run on Windows only:

- **macOS**: `./eng/common/cibuild.sh --configuration Release --prepareMachine --projects src/DevFlow/DevFlow.slnf`
- **Windows**: `eng\common\cibuild.cmd -configuration Release -prepareMachine -projects src/DevFlow/DevFlow.slnf`

Test results are uploaded as artifacts from `artifacts/TestResults/`. The format depends on the
runner Arcade selects: the default xunit console runner writes `*.xml`, while coverage legs switch
to the VSTest runner (see `eng/Tests.targets`) which writes `*.trx` plus a `coverage.cobertura.xml`
per test project.

### Coverage

Products that opt in pass `collect-coverage: true` and `coverage-threshold` to `_build.yml`.
Coverage is collected and gated on a single matrix leg only (`coverage-os`, default
`windows-latest`) because the thresholds are calibrated against one OS; the other legs still run
the tests. A project is held to the threshold only if it sets `EnforceCoverageThreshold` in its
csproj. See `eng/Tests.targets` and `eng/test.runsettings`.

```powershell
# Reproduce the CI coverage run locally
dotnet build src/DevFlow/Microsoft.Maui.DevFlow.Tests/Microsoft.Maui.DevFlow.Tests.csproj `
  -c Release -t:Test -p:CollectCoverage=true -p:Threshold=30
```

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

### What to Test

- **Do test**: Public API methods, edge cases, error handling, serialization/deserialization
- **Do test**: AgentClient methods (they're the public NuGet API surface)
- **Don't test**: Platform-specific overrides (require actual devices/simulators)
- **Don't test**: MCP tool registration (covered by integration at runtime)
