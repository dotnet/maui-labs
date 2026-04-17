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
| DevFlow | `src/DevFlow/Microsoft.Maui.DevFlow.Tests/` | `net10.0` |

## Running Tests

```bash
# All tests
dotnet test MauiLabs.sln

# DevFlow tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/

# Specific test
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTestMethod"

# With verbose output
dotnet test --logger "console;verbosity=detailed"
```

## CI Matrix

Tests run on **macOS and Windows** in CI (`.github/workflows/_build.yml`):

- **macOS**: `./eng/common/cibuild.sh --configuration Release --prepareMachine --projects src/DevFlow/DevFlow.slnf`
- **Windows**: `eng\common\cibuild.cmd -configuration Release -prepareMachine -projects src/DevFlow/DevFlow.slnf`

Test results are uploaded as artifacts: `artifacts/TestResults/**/*.xml`

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
