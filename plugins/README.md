# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

DevFlow runtime skills (`maui-devflow-onboard`, `maui-devflow-debug`) are bundled with the `maui` CLI from `plugins/dotnet-maui/skills/`, installed with `maui devflow init`, and exposed through the plugin manifest.

## Plugin

| Plugin | Skill | Description |
|--------|-------|-------------|
| [dotnet-maui](dotnet-maui/) | [maui-devflow-onboard](dotnet-maui/skills/maui-devflow-onboard/) | Add MAUI DevFlow packages and app registration to a project. |
| | [maui-devflow-debug](dotnet-maui/skills/maui-devflow-debug/) | Run MAUI DevFlow build, deploy, connection recovery, inspect, and fix loops. |
| | [devflow-connect](dotnet-maui/skills/devflow-connect/) | Diagnose and fix DevFlow agent connectivity issues between the `maui` CLI and running .NET MAUI apps. |
| | [maui-ai-debugging](dotnet-maui/skills/maui-ai-debugging/) | Legacy compatibility skill for older DevFlow clients. |
| | [android-slim-bindings](dotnet-maui/skills/android-slim-bindings/) | Create Android slim bindings using the Native Library Interop approach. |
| | [ios-slim-bindings](dotnet-maui/skills/ios-slim-bindings/) | Create iOS slim bindings using the Native Library Interop approach. |
| | [dotnet-workload-info](dotnet-maui/skills/dotnet-workload-info/) | Discover installed .NET workloads, SDK versions, and dependency requirements. |

## Installation

```bash
# Add this repo as a marketplace
/plugin marketplace add dotnet/maui-labs

# Install the plugin
/plugin install dotnet-maui@dotnet-maui-labs
```

## Adding Skills

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide. Quick summary:

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with YAML frontmatter
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Submit a PR — the `skill-check` workflow validates automatically
4. A maintainer posts `/evaluate` to run LLM-based evaluation
