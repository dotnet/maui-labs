---
description: >
  Weekly audit of AI configuration files (AGENTS.md, copilot-instructions.md,
  instruction files) to keep them in sync with the actual codebase.
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
- `*.csproj`, `*.slnf`, `global.json`, `NuGet.config` → packages, build commands, SDK version, feeds
- `src/DevFlow/` project structure → architecture diagram
- Pipeline YAML files → CI matrix

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

- **MCP tools table**:
  1. Run: `grep -rn "McpServerTool" src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/*.cs`
  2. For each match, extract: tool name (from attribute), class name, source file path,
     and description (from XML doc comment or attribute parameter).
  3. Compare with AGENTS.md table — every tool must have exactly one row.
  4. If ANY mismatch: regenerate the entire table sorted by tool name. Do not patch rows.
  5. Verify: `grep -rl "McpServerTool" src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/*.cs | wc -l`
     and compare against the number of table rows (one tool per file).
- **Package list**: Run `grep -rn "IsPackable" src --include="*.csproj"` and
  cross-reference with the documented package list. Every `IsPackable=true` project
  must appear in the list.
- **Build commands**: Run `ls src/DevFlow/*.slnf src/Cli/*.slnf 2>/dev/null` and
  verify AGENTS.md build commands reference these exact paths.
- **NuGet feeds**: Run `grep '<add key=' NuGet.config` and compare with the documented feed list.
- **SDK version**: Run `cat global.json` and verify the documented SDK version matches exactly.

### .github/copilot-instructions.md

- **Platform patterns**: Compare the documented multi-targeting pattern
  (Core vs Platform projects, `#if IOS || MACCATALYST` guards, handler
  registration steps) against actual files in `src/DevFlow/`. Run
  `grep -r '#if ' src/DevFlow/Microsoft.Maui.DevFlow.Agent/` to check
  which `#if` directives are actually used.
- **MCP tool conventions**: Run `grep -rn 'McpServerTool' src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`
  and verify that documented naming conventions (`maui_` prefix, snake_case,
  parameter ordering) match the actual tool attributes.
- **Arcade gotchas**: Check that the "Arcade SDK Gotchas" section (e.g.,
  "never modify `eng/common/`") is still accurate by verifying `eng/common/`
  exists and `.arcaderc` / `global.json` SDK version match what's documented.

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

- **Test projects**: Run `find src -type f \( -name "*.Tests.csproj" -o -name "*Tests*.csproj" \) | sort`
  and verify every result appears in the documented test project list.
- **Test patterns**: Sample 2-3 recent test files and confirm xUnit patterns
  (attributes, assertion style) match what's documented.
- **CI matrix**: Run `find . -type f \( -name "*.yml" -o -name "*.yaml" \) | grep -E "pipeline|azure" | sort`
  and verify documented CI platforms match the actual pipeline definitions.

### .github/instructions/packaging.instructions.md

- **Package metadata**: Run `cat global.json` for Arcade SDK version, verify
  signing config in `eng/Signing.props`, and check CPM in `Directory.Packages.props`.
- **Package list**: Must match AGENTS.md (cross-reference both lists).
- **Feed configuration**: Must match `NuGet.config` (cross-reference with
  `grep '<add key=' NuGet.config`).

### Cross-file consistency (required)

After auditing individual files, verify these stay in sync:
- **MCP tools table**: Extract tool names from both `AGENTS.md` and
  `mcp-tools.instructions.md` and diff them:
  `grep '|' AGENTS.md | grep -i tool | sort` vs equivalent in mcp-tools file.
  AGENTS.md is authoritative — update mcp-tools to match if they diverge.
- **Package list**: Extract package names from `AGENTS.md` and
  `packaging.instructions.md` and verify both list the same packages.
  Cross-check with `grep -rn "IsPackable" src --include="*.csproj"`.
- **NuGet feeds**: Run `grep '<add key=' NuGet.config` and verify the same
  feeds appear in both `AGENTS.md` and `packaging.instructions.md`.

## Step 3: Decide and act

**If changes are needed:**
1. Make all corrections and open a single pull request.
2. PR description must include:
   - Summary: "Updated N files based on M merged PRs"
   - Table of changes: `| File | What changed | Caused by PR |`
   - Example: `| AGENTS.md | Added maui_new_tool to MCP table | #142 |`

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
