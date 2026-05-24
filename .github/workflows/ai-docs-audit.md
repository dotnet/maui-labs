---
description: >
  Weekly audit of AI configuration files (AGENTS.md, copilot-instructions.md,
  instruction files) to keep them in sync with the actual codebase.
  Also audits itself for drift against the repo structure.
on:
  schedule: weekly
  workflow_dispatch:
permissions:
  contents: read
  pull-requests: read
  issues: read
tools:
  edit:
  bash:
    - git
    - grep
    - find
    - cat
    - ls
    - wc
    - sort
    - diff
    - head
    - tail
  github:
network:
  allowed:
    - defaults
safe-outputs:
  create-pull-request:
    title-prefix: "[ai-docs] "
    labels: [ai-docs-audit]
    draft: true
  create-issue:
    title-prefix: "[ai-docs] "
    labels: [ai-docs-audit]
    close-older-issues: true
    max: 1
---

# Weekly AI Docs Audit

You are auditing the AI configuration files in this repository to ensure they
accurately reflect the current state of the codebase. These files are read by
AI coding agents (GitHub Copilot, Claude, etc.) and stale information causes
them to generate incorrect code.

## Step 1: Gather what changed

Use the GitHub tools to list PRs merged in the past 7 days. For each PR, note the
number, title, and changed file paths.

**Focus areas** — prioritize auditing when PRs touched:
- `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/` → MCP tools table
- `src/Cli/Microsoft.Maui.Cli/Commands/` → CLI commands (doctor, device, android, apple, devflow, go, profile, version)
- `src/DevFlow/` → DevFlow architecture, agent, driver
- `src/Comet/`, `src/Go/` → Comet MVU framework and Go server
- `src/AI/` → Essentials.AI
- `src/AppProjectReference/` → AppProjectReference
- `platforms/Linux.Gtk4/`, `platforms/MacOS/`, `platforms/Windows.WPF/` → platform backends
- `plugins/` → agent skills marketplace
- `*.csproj`, `*.slnf`, `*.slnx`, `global.json`, `NuGet.config` → packages, build commands, SDK version, feeds
- `eng/pipelines/` → AzDO pipeline, CI configuration
- `.github/workflows/` → GitHub Actions CI, agentic workflows

If no PRs were merged in the past 7 days, stop silently — no audit needed.

## Step 2: Audit each AI config file

**First, discover all AI config files:**
```
ls -la AGENTS.md .github/copilot-instructions.md 2>/dev/null
find .github/instructions -name "*.instructions.md" 2>/dev/null | sort
```
Only audit files that exist — skip any that are missing (do not create new files).
If you discover instruction files not listed below, audit them using the closest
matching checklist section.

### AGENTS.md

- **Products table**: Verify all products are listed. Current products:
  - **Cli** — `src/Cli/` (global tool: `maui`)
  - **DevFlow** — `src/DevFlow/` (agent, driver, analyzers, Blazor, logging)
  - **Comet** — `src/Comet/` (MVU framework, source generator, layout)
  - **Go** — `src/Go/` (single-file Comet apps server + companion app)
  - **Essentials.AI** — `src/AI/` (on-device AI for .NET MAUI)
  - **AppProjectReference** — `src/AppProjectReference/`
  - **Linux GTK4** — `platforms/Linux.Gtk4/`
  - **macOS AppKit** — `platforms/MacOS/`
  - **WPF** — `platforms/Windows.WPF/`
  Run `find src platforms -name "*.csproj" -not -path "*/artifacts/*" | sort` and verify every
  shipping project is represented in the products table.
- **CLI commands**: Verify all top-level commands are documented. Current command groups:
  `doctor`, `device`, `android`, `apple`, `devflow`, `go`, `profile`, `version`.
  Run: `grep "new Command\|rootCommand.Add" src/Cli/Microsoft.Maui.Cli/Program.cs`
- **MCP tools table**:
  1. Run: `grep -rn "McpServerTool" src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/*.cs`
  2. For each match, extract: tool name (from attribute), class name, source file path,
     and description (from XML doc comment or attribute parameter).
  3. Compare with AGENTS.md table — every tool must have exactly one row.
  4. If ANY mismatch: regenerate the entire table sorted by tool name. Do not patch rows.
- **Package list**: Run `grep -rn "IsPackable\|IsShipping" src platforms --include="*.csproj"` and
  cross-reference with the documented package list. Every `IsPackable=true` or
  `IsShipping=true` project must appear.
- **Build commands**: Verify documented solution files match:
  ```
  find . \( -name "*.slnf" -o -name "*.slnx" \) -not -path "*/artifacts/*" | sort
  ```
- **NuGet feeds**: Run `grep '<add key=' NuGet.config` and compare with the documented feed list.
- **SDK version**: Run `cat global.json` and verify the documented SDK version matches exactly.
- **Agent skills**: Verify `plugins/dotnet-maui/skills/` lists match what's documented.
  Run: `ls plugins/dotnet-maui/skills/`

### .github/copilot-instructions.md

- **Platform patterns**: Compare the documented multi-targeting pattern
  against actual files in `src/DevFlow/`. Run
  `grep -r '#if ' src/DevFlow/Microsoft.Maui.DevFlow.Agent/` to check
  which `#if` directives are actually used.
- **MCP tool conventions**: Run `grep -rn 'McpServerTool' src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`
  and verify that documented naming conventions match the actual tool attributes.
- **CLI command conventions**: Check if the documented patterns for adding new CLI
  commands (partial classes, `Create()` factory, `Program.GetFormatter()`) still match
  the actual code in `src/Cli/Microsoft.Maui.Cli/Commands/`.
- **New product checklist**: Verify the new product checklist covers all current
  requirements: CI workflow, AzDO pipeline job, signing entries, solution filter,
  NuGet README with absolute URLs.
- **Arcade gotchas**: Check that the "Arcade SDK Gotchas" section is still accurate
  by verifying `eng/common/` exists and `global.json` SDK version matches.

### .github/instructions/devflow-architecture.instructions.md

- **Architecture diagram**: Run `grep -rn "ProjectReference" src --include="*.csproj"`
  and verify the documented dependency graph matches actual project references.
  Check for new or removed projects with `find src -name "*.csproj" | sort`.
- **Communication protocols**: Verify WebSocket/HTTP handler patterns in
  `src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/` still match documented patterns.
- **Package dependency graph**: Cross-reference `PackageReference` entries in `.csproj`
  files with the documented dependency chain between shipping packages.

### .github/instructions/mcp-tools.instructions.md

- **Tool creation guide**: Cross-reference the step-by-step guide against the
  most recently added tool in `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`.
- **Naming conventions**: Run `grep -rn "class.*Tool" src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`
  and verify Tool/Tools suffix guidance matches actual class names.
- **Tools reference table**: Must match the AGENTS.md table exactly (same rows,
  same ordering). AGENTS.md is authoritative — if they diverge, update this file
  to match AGENTS.md.

### .github/instructions/testing.instructions.md

- **Test projects**: Run `find src platforms -type f \( -name "*.Tests.csproj" -o -name "*Tests*.csproj" \) | sort`
  and verify every result appears in the documented test project list.
- **Test patterns**: Sample 2-3 recent test files and confirm xUnit patterns
  (attributes, assertion style) match what's documented.
- **CI matrix**: Verify documented CI platforms match actual workflows:
  ```
  ls .github/workflows/ci-*.yml
  ```

### .github/instructions/packaging.instructions.md

- **Package metadata**: Run `cat global.json` for Arcade SDK version, verify
  signing config in `eng/Signing.props`, and check CPM in `Directory.Packages.props`.
- **Package list**: Must match AGENTS.md (cross-reference both lists).
- **Feed configuration**: Must match `NuGet.config`.
- **Template packages**: Verify template projects are documented:
  ```
  find platforms -name "*Templates.csproj" | sort
  ```

### Cross-file consistency (required)

After auditing individual files, verify these stay in sync:
- **MCP tools table**: AGENTS.md vs mcp-tools.instructions.md — AGENTS.md is authoritative.
- **Package list**: AGENTS.md vs packaging.instructions.md — cross-check with
  `grep -rn "IsPackable\|IsShipping" src platforms --include="*.csproj"`.
- **NuGet feeds**: Both files must match `NuGet.config`.
- **CLI commands**: AGENTS.md product description must list all command groups
  that exist in `src/Cli/Microsoft.Maui.Cli/Commands/` and `Program.cs`.

## Step 3: Self-audit — check this workflow for drift

Before finishing, audit whether THIS WORKFLOW (`ai-docs-audit.md`) is still
accurate and comprehensive. Compare what you observed during the audit against
the configuration of this workflow itself:

- **Focus area paths (Step 1)**: Are there new source directories, command groups,
  or product folders that aren't listed in the "Focus areas" section?
  Run: `ls src/ platforms/` and `ls src/Cli/Microsoft.Maui.Cli/Commands/`
- **Product list**: Are all products listed in the AGENTS.md audit section?
  Compare against actual `src/` and `platforms/` directories.
- **Instruction files**: Are there new `.instructions.md` files that this
  workflow doesn't have an audit section for?
  Run: `find .github/instructions -name "*.instructions.md" | sort`
- **Agentic workflows**: Are there new `.md` workflows that might need auditing?
  Run: `ls .github/workflows/*.md`
- **Skills**: Has the skills marketplace structure changed?
  Run: `ls plugins/dotnet-maui/skills/`

If you find drift — paths, products, files, or audit sections that are missing
or outdated in this workflow — open an issue on this repository with the title
prefix `[ai-docs]` describing what needs updating in
`.github/workflows/ai-docs-audit.md`. Be specific about what's missing and why.

## Step 4: Decide and act

**If changes are needed to AI config files:**
1. Make all corrections and open a single pull request.
2. PR description must include:
   - Summary: "Updated N files based on M merged PRs"
   - Table of changes: `| File | What changed | Caused by PR |`
   - Example: `| AGENTS.md | Added maui_new_tool to MCP table | #142 |`

**If this workflow needs updating (from Step 3):**
Open a separate issue describing the drift — do not modify this workflow directly.

**If no changes needed** and there were merged PRs to review, comment on the
most recent merged PR confirming the AI docs are still in sync. Do NOT create
an issue just to say "nothing changed."

**If no merged PRs** in the past 7 days, stop silently.

## Rules

- Only modify AI configuration and documentation files covered by this audit (AGENTS.md, copilot-instructions.md, instruction files, and related docs).
- Never modify source code, tests, or build pipelines.
- When updating the MCP tools table, regenerate the entire table from source.
- Preserve existing formatting and structure of each file.
- Be precise — wrong information in these files is worse than missing information.
- Do not audit general documentation (README, etc.) unless it is explicitly
  referenced by an AI config file section you are updating.
- When verifying tables "match", ignore whitespace differences but content
  (names, paths, versions) must be exact. File paths are case-sensitive.
