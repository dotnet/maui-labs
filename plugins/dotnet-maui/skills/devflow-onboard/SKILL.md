---
name: devflow-onboard
description: >-
  Set up .NET MAUI projects for MAUI DevFlow using the maui CLI. USE FOR:
  first-run DevFlow onboarding, adding DevFlow packages and MauiProgram.cs
  registration, choosing one or more MAUI projects in a workspace, reading the
  MAUI-DEVFLOW-INIT-REPORT.md report, and continuing after partial setup. DO
  NOT USE FOR: troubleshooting an already-integrated app that cannot connect
  (use devflow-connect), generic build failures, or non-MAUI projects.
---

# DevFlow Onboard

Use this skill to set up DevFlow in a workspace and then continue from the CLI-authored report.

## When to Use

- DevFlow is not yet integrated into the current MAUI workspace
- The user has just installed the plugin and needs the next gesture
- A workspace contains multiple MAUI apps and the user wants to choose one or more
- The user needs to resume from a previous init run and inspect `MAUI-DEVFLOW-INIT-REPORT.md`

## Workflow

1. Ensure the `maui` CLI is available. If it is missing, install or update it:

   ```bash
   dotnet tool install -g Microsoft.Maui.Cli --prerelease || dotnet tool update -g Microsoft.Maui.Cli --prerelease
   ```

2. Run DevFlow onboarding from the workspace root:

   ```bash
   maui devflow init
   ```

   Useful variants:

   ```bash
   maui devflow init --project path/to/App.csproj
   maui devflow init --all
   maui devflow init --no-ai
   ```

3. After `init` completes, read:

   ```text
   MAUI-DEVFLOW-INIT-REPORT.md
   ```

4. Treat the report as the source of truth for:

- which projects were changed
- which steps succeeded, were skipped, or need manual follow-up
- which AI host was selected
- whether repo-local skills were synced

5. If setup succeeded, continue with normal DevFlow verification:

   ```bash
   maui devflow diagnose
   maui devflow wait
   ```

6. If the report says setup is complete but the app still will not connect, switch to `devflow-connect`.

## Important Rules

- Prefer `maui devflow init` over hand-editing project files when possible.
- Do not treat an empty `maui devflow list` result as proof that DevFlow is not integrated.
- If `MAUI-DEVFLOW-INIT-REPORT.md` exists, read it before guessing what the CLI did.
