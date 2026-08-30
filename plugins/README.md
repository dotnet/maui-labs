# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

DevFlow runtime skills are bundled with the `maui` CLI from `plugins/dotnet-maui/skills/` and exposed through the plugin manifest. `maui devflow init` installs the recommended onboarding, debugging, and session-review skills; `maui devflow skills install` or `update` also installs the optional collaborative-testing and artifact-diagnosis skills.

## Plugin

| Plugin | Skill | Description |
|--------|-------|-------------|
| [dotnet-maui](dotnet-maui/) | [maui-devflow-onboard](dotnet-maui/skills/maui-devflow-onboard/) | Add MAUI DevFlow packages and app registration to a project. |
| | [maui-devflow-debug](dotnet-maui/skills/maui-devflow-debug/) | Run MAUI DevFlow build, deploy, connection recovery, inspect, and fix loops. |
| | [maui-devflow-session-review](dotnet-maui/skills/maui-devflow-session-review/) | Review opt-in MAUI DevFlow sessions for friction, retries, workarounds, and product feedback. |
| | [maui-devflow-test](dotnet-maui/skills/maui-devflow-test/) | Collaboratively author, review, run, triage, and hand off human-approved DevFlow tests. |
| | [maui-devflow-record](dotnet-maui/skills/maui-devflow-record/) | Prepare a human-driven Inspector recording and promote the capture into a reviewable flow. |
| | [maui-devflow-run-cli](dotnet-maui/skills/maui-devflow-run-cli/) | Run committed DevFlow flows from the terminal with the `maui devflow flow` command family. |
| | [maui-devflow-ci](dotnet-maui/skills/maui-devflow-ci/) | Wire DevFlow flow execution into GitHub Actions with least-privilege, label-gated jobs. |
| | [maui-devflow-ci-triage](dotnet-maui/skills/maui-devflow-ci-triage/) | Diagnose a red DevFlow CI run from its artifacts and hand off a bounded local reproduction. |
| | [maui-devflow-artifact](dotnet-maui/skills/maui-devflow-artifact/) | Diagnose explicit DevFlow flow-run and `.mauitrace` artifacts through a read-only trust projection. |
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
