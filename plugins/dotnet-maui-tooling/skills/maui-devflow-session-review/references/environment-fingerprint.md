# Environment fingerprint

Every report or GitHub issue should include the best available environment
fingerprint. Unknown values are acceptable; guessing is not.

## Required fields

```markdown
## Environment

- Host OS/platform: {macOS/Windows/Linux + version, if known}
- AI tool/host: {Copilot CLI, VS Code Copilot Chat, Claude Code, other, Unknown}
- Model: {model and reasoning effort if known, otherwise Unknown}
- MAUI CLI: {Microsoft.Maui.Cli version and invocation mode, otherwise Unknown}
- App framework(s): {.NET MAUI/MAUI Blazor Hybrid/GTK/WPF/macOS AppKit + versions}
- Target platform(s): {Android/iOS/Mac Catalyst/macOS/Windows/Linux/GTK/WebView/etc.}
- MAUI DevFlow package/reference versions: {package names and versions}
- Review scope: {current session/recent sessions/date range/platform/feature}
```

## Host platform

Use explicit session metadata when available. Otherwise collect it from safe
local commands or project outputs, such as OS name/version or architecture. Do
not include usernames, home paths, machine identifiers, local file paths, or
hostnames.

## AI tool and model

Prefer observable metadata:

- session metadata such as agent name, model ID, or usage model
- transcript headers or host-specific run metadata
- user-provided information
- visible model strings in assistant/system context

Common tool names to normalize:

- Copilot CLI
- VS Code GitHub Copilot Chat
- Claude Code
- GitHub Copilot coding agent
- Other
- Unknown

If the model or reasoning effort cannot be detected, write `Unknown`. Do not
infer a model from writing style.

## MAUI CLI version

Use the first reliable source available:

- `maui --version`
- `maui version`
- `maui devflow diagnose`
- JSON output from `maui devflow skills doctor`
- local tool manifest or package version when the CLI binary is unavailable
- command output captured in the session, after removing local identifiers

Record whether the session used CLI commands, MCP tools, or both.

## App frameworks and versions

Collect framework versions from project files and lockfiles inside the scoped
workspace:

- `.csproj`, `Directory.Packages.props`, target frameworks, and
  `PackageReference` entries
- `global.json`, `.config/dotnet-tools.json`, and solution filters when relevant
- MAUI platform package references for GTK, WPF, macOS AppKit, or Blazor WebView

Only collect the versions needed to explain the finding.

## Target platforms

Infer targets from observable signals:

- .NET target frameworks such as `net*-android`, `net*-ios`,
  `net*-maccatalyst`, `net*-windows`, `net*-macos`, or GTK-specific head
  projects
- simulator, emulator, device, or desktop launch commands
- `maui devflow device` and `maui devflow diagnose` output
- user-provided platform context

## MAUI DevFlow packages

Look for package/reference names such as:

- `Microsoft.Maui.Cli`
- `Microsoft.Maui.DevFlow.Agent`
- `Microsoft.Maui.DevFlow.Agent.Core`
- `Microsoft.Maui.DevFlow.Agent.Gtk`
- `Microsoft.Maui.DevFlow.Blazor`
- `Microsoft.Maui.DevFlow.Blazor.Gtk`
- `Microsoft.Maui.DevFlow.Driver`
- `Microsoft.Maui.DevFlow.Logging`

Include versions and whether the package came from a project reference, package
reference, central package management entry, or local source checkout.

## PII scrub

Before sending, saving, or filing the environment fingerprint, remove names,
usernames, emails, session IDs, local file paths, machine names, private server
URLs, request bodies, user profile text, screenshots, credentials, and tokens.
Prefer generic source types such as "project manifest" or "CLI output" over
paths or identifiers.
