# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

## Plugin

| Plugin | Skills | Description |
|--------|--------|-------------|
| [dotnet-maui](dotnet-maui/) | [maui-devflow-setup](dotnet-maui/skills/maui-devflow-setup/), [devflow-connect](dotnet-maui/skills/devflow-connect/) | MAUI development — DevFlow automation, profiling, accessibility, platform bindings, diagnostics. Ships the DevFlow MCP server, setup skill, `maui-devflow-specialist` subagent, and project hooks. Requires the `maui` CLI. |

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
