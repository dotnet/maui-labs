---
description: |
  Analyzes merged pull requests for documentation needs. When a PR is merged,
  this workflow reviews the changes and determines if documentation updates are
  needed on dotnet/docs-maui. If updates are needed, it creates a draft PR on
  docs-maui with the actual documentation changes. If not, it comments on the
  source PR explaining why.

on:
  pull_request:
    types: [closed]
    branches:
      - main
      - release/*
    paths-ignore:
      - "eng/**"
      - ".github/**"
      - ".azure-pipelines/**"
      - ".config/**"
      - ".editorconfig"
      - ".gitignore"
      - ".gitattributes"
      - "**/*.md"
      - "**/LICENSE*"
      - "**/THIRD-PARTY*"
      - "Directory.Build.*"
      - "NuGet.config"
      - "global.json"
      - "*.sln"
      - "*.slnf"
  workflow_dispatch:
    inputs:
      pr_number:
        description: "PR number to analyze"
        required: true
        type: string

if: >-
  (github.event.pull_request.merged == true || github.event_name == 'workflow_dispatch')
  && github.repository_owner == 'dotnet'

checkout:
  - repository: dotnet/docs-maui
    github-token: ${{ secrets.MAUI_BOT_TOKEN }}
    current: true

permissions:
  contents: read
  pull-requests: read
  issues: read

network:
  allowed:
    - defaults
    - github

tools:
  github:
    toolsets: [repos, issues, pull_requests]
    github-token: ${{ secrets.MAUI_BOT_TOKEN }}

safe-outputs:
  github-token: ${{ secrets.MAUI_BOT_TOKEN }}
  create-pull-request:
    title-prefix: "[maui-labs docs] "
    labels: [docs-from-code]
    draft: true
    target-repo: "dotnet/docs-maui"
    fallback-as-issue: true
  add-comment:
    target-repo: "dotnet/maui-labs"
    hide-older-comments: true

timeout-minutes: 20
---

# PR Documentation Check

Analyze a merged pull request in `dotnet/maui-labs` and determine whether
documentation updates are needed on the `dotnet/docs-maui` documentation site.
If updates are needed, write the actual documentation changes and create a draft PR.

## Context

- **Source repository**: `dotnet/maui-labs`
- **PR Number**: `${{ github.event.pull_request.number || github.event.inputs.pr_number }}`
- **PR Title**: `${{ github.event.pull_request.title }}`

> [!NOTE]
> The agent runs with `dotnet/docs-maui` as the current workspace. Use GitHub
> tools to read the source `dotnet/maui-labs` PR details and diff.

## Step 1: Gather PR Information

Use the GitHub tools to read the full pull request details from `dotnet/maui-labs`
for the PR number above, including the title, description, author, labels, base
branch, and the full diff of changes.

If this was triggered via `workflow_dispatch`, use the `pr_number` input to look up
the PR details. If the PR number is invalid or the PR cannot be found, stop and
comment on the workflow run that the input was invalid. If the PR was not merged
(still open or closed without merging), comment that no documentation updates are
needed because the PR was not merged, then **stop**.

## Step 2: Quick Filter — Skip Obviously Non-Doc PRs

Before doing a deep analysis, check if this PR can be **skipped entirely**:

**Skip if:**
- PR has the `no-docs-needed` label
- PR author is a bot (`dotnet-maestro[bot]`, `dependabot[bot]`, `github-actions[bot]`)
- PR title contains "backport" (case-insensitive)
- ALL changed files are tests only (paths containing `/tests/`, `/TestUtils/`,
  files with `.Tests.` or `.UnitTests.` in name)
- ALL changed files are CI/build infrastructure only (`eng/`, `.github/`,
  `*.yml`, `*.yaml`, `*.props`, `*.targets`)

If the PR is skipped, comment on the PR with a brief one-line message confirming
no documentation updates are needed and why, then **stop**.

**Always proceed with analysis if:**
- Any file in `src/Cli/Microsoft.Maui.Cli/DevFlow/` was changed (CLI DevFlow commands)
- Any file in `src/Cli/Microsoft.Maui.Cli/Commands/` was changed (CLI commands)
- Any file in `src/DevFlow/**/Mcp/Tools/` was changed (MCP tool changes)
- Any `.csproj` file with `IsPackable=true` was changed (package changes)

## Step 3: Analyze Changes for Documentation Needs

Review the PR diff for user-facing changes that affect documentation.

**PROCEED with a docs PR if the change includes any of:**
- New or changed CLI commands, flags, or options
- New or changed MCP tools (new tools, renamed tools, changed parameters)
- New public APIs in user-facing packages (AgentClient, DevFlow CLI, etc.)
- New features or capabilities (new platform support, new debugging features)
- Breaking changes — removed or renamed APIs, behavioral changes
- Changed system requirements (minimum .NET version, new dependencies)
- New NuGet packages
- Behavioral changes users will notice (e.g., a CLI flag works differently)

**SKIP (no docs PR needed) if the change is only:**
- Internal refactoring with no public API or behavior change
- Test-only changes
- Bug fixes that don't change documented behavior
- Dependency version bumps with no user impact
- Code style or formatting changes
- Performance improvements that don't change usage patterns
- Help text or error message fixes (these are self-documenting)

**When uncertain, PROCEED.** Draft PRs are cheap; missing docs are expensive.

## Step 4: If Documentation IS Needed

### 4a: Read Existing Documentation

Browse the checked-out `dotnet/docs-maui` workspace to understand the current
documentation structure. The developer-tools documentation lives under
`docs/developer-tools/` with this structure:

**CLI documentation** (`docs/developer-tools/cli/`):
- `index.md` — .NET MAUI CLI overview
- `environment-diagnostics.md` — Environment diagnostics with `maui doctor`
- `android-management.md` — Android SDK and emulator management
- `device-management.md` — Device management

**DevFlow documentation** (`docs/developer-tools/devflow/`):
- `index.md` — DevFlow overview
- `visual-tree-screenshots.md` — Visual tree inspection and screenshots
- `element-interaction.md` — Element interaction and automation
- `blazor-cdp.md` — Blazor WebView debugging with CDP
- `mcp-server.md` — MCP server for AI agents
- `network-profiling.md` — Network monitoring and profiling
- `broker.md` — DevFlow broker architecture
- `setup-windows.md` — DevFlow Windows setup
- `setup-android.md` — DevFlow Android setup
- `setup-apple.md` — DevFlow Apple platforms setup

Also check:
- `docs/developer-tools/index.md` — Landing page for the developer-tools section
- `docs/TOC.yml` — Table of contents (update if adding new pages)

### 4b: Write Documentation Changes

Based on your analysis, make the actual file changes in the workspace:

- **For updates to existing pages**: Edit the relevant `.md` files in place
- **For new pages**: Create new `.md` files in the appropriate directory

Follow these MS Learn documentation conventions:
- **Frontmatter**: Every page needs `title`, `description`, and `ms.date` (format: `MM/DD/YYYY`)
- **Headings**: Use `#` for the page title (must match frontmatter `title`), `##` for sections
- **Code blocks**: Use triple backticks with language identifier (e.g., ````csharp`, ````bash`)
- **Notes/warnings**: Use `> [!NOTE]`, `> [!IMPORTANT]`, `> [!WARNING]`
- **Cross-references**: Use relative paths for internal links (e.g., `[DevFlow overview](../devflow/index.md)`)
- **TOC.yml**: If adding new pages, add an entry under the appropriate section

### 4c: Create Draft PR

Create a draft pull request on `dotnet/docs-maui` with:

**Title**: A clear, concise title describing the documentation work
(the `[maui-labs docs]` prefix will be added automatically)

**Description** that includes:
- A link to the source PR: `Documents changes from dotnet/maui-labs#<number>`
- The PR author mention: `@<author>`
- A summary of what documentation was added or changed
- A list of files modified or created

### 4d: Comment on Source PR

After the draft PR is created, comment on the original PR in `dotnet/maui-labs` with:
- A message indicating documentation updates have been drafted
- A link to the newly created draft PR on `dotnet/docs-maui`
- A brief summary of what documentation changes were made
- A note that the draft PR needs human review before merging

## Step 5: If Documentation is NOT Needed

Comment on the PR in `dotnet/maui-labs` with a brief one-line message confirming
no documentation updates are required and a short reason (e.g., "internal
refactoring only", "test changes only", "bug fix with no behavioral change").

Keep the comment concise — one or two sentences maximum.
