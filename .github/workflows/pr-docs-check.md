---
description: |
  Analyzes merged pull requests for documentation needs. When a PR is merged,
  this workflow reviews the changes and determines if documentation updates are
  needed on dotnet/docs-maui. If updates are needed, it opens an issue on
  docs-maui with a detailed description of the changes needed.

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
  create-issue:
    title-prefix: "[maui-labs docs] "
    labels: [docs-from-code]
    target-repo: "dotnet/docs-maui"
  noop: false

timeout-minutes: 15
---

# PR Documentation Check

Analyze a merged pull request in `dotnet/maui-labs` and determine whether
documentation updates are needed on the `dotnet/docs-maui` documentation site.
If updates are needed, open an issue on docs-maui. If not, do nothing.

## Context

- **Source repository**: `dotnet/maui-labs`
- **PR Number**: `${{ github.event.pull_request.number || github.event.inputs.pr_number }}`
- **PR Title**: `${{ github.event.pull_request.title }}`

## Step 1: Gather PR Information

Use the GitHub tools to read the full pull request details from `dotnet/maui-labs`
for the PR number above, including the title, description, author, labels, base
branch, and the full diff of changes.

If this was triggered via `workflow_dispatch`, use the `pr_number` input to look up
the PR details. If the PR number is invalid or the PR cannot be found, stop.
If the PR was not merged (still open or closed without merging), stop.

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

If the PR is skipped, **stop silently** — do not create any issues or comments.

**Always proceed with analysis if:**
- Any file in `src/Cli/Microsoft.Maui.Cli/DevFlow/` was changed (CLI DevFlow commands)
- Any file in `src/Cli/Microsoft.Maui.Cli/Commands/` was changed (CLI commands)
- Any file in `src/DevFlow/**/Mcp/Tools/` was changed (MCP tool changes)
- Any `.csproj` file with `IsPackable=true` was changed (package changes)

## Step 3: Analyze Changes for Documentation Needs

Review the PR diff for user-facing changes that affect documentation.

**PROCEED if the change includes any of:**
- New or changed CLI commands, flags, or options
- New or changed MCP tools (new tools, renamed tools, changed parameters)
- New public APIs in user-facing packages (AgentClient, DevFlow CLI, etc.)
- New features or capabilities (new platform support, new debugging features)
- Breaking changes — removed or renamed APIs, behavioral changes
- Changed system requirements (minimum .NET version, new dependencies)
- New NuGet packages
- Behavioral changes users will notice (e.g., a CLI flag works differently)

**SKIP if the change is only:**
- Internal refactoring with no public API or behavior change
- Test-only changes
- Bug fixes that don't change documented behavior
- Dependency version bumps with no user impact
- Code style or formatting changes
- Performance improvements that don't change usage patterns
- Help text or error message fixes (these are self-documenting)

If no documentation is needed, **stop silently** — do not create any issues or comments.

**When uncertain, PROCEED.** Issues are cheap; missing docs are expensive.

## Step 4: Open an Issue on docs-maui

If documentation updates ARE needed, create an issue on `dotnet/docs-maui` with
a clear, actionable description. The issue will be picked up by an agentic
workflow on the docs-maui side that creates a draft PR from it.

The issue body MUST include all of the following sections:

### Source PR
Link to the source PR with full URL:
`https://github.com/dotnet/maui-labs/pull/<number>`

Include the PR title, author, and merge date.

### Summary of Changes
A concise summary of the user-facing changes. Focus on what changed from a
documentation perspective, not implementation details.

### Documentation Pages Affected
List which existing pages need updating. Reference the file paths in the docs repo:

- CLI docs: `docs/developer-tools/cli/`
- DevFlow docs: `docs/developer-tools/devflow/`
- Landing page: `docs/developer-tools/index.md`
- TOC: `docs/TOC.yml`

### Suggested Changes
Provide the specific text, code blocks, table rows, or sections to add or modify.
Be detailed enough that a docs author (or an agent) can apply the changes without
reading the full source PR diff. Include:

- Exact headings, parameter tables, code examples
- Where in the existing page the content should be inserted
- If a new page is needed, suggest the filename and TOC.yml placement

The issue body should be **self-contained** — everything needed to make the
documentation update should be in the issue itself.
