# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

## Plugins

| Plugin | Purpose |
|--------|---------|
| [dotnet-maui](dotnet-maui/) | Default app-development plugin for building production .NET MAUI apps. |
| [dotnet-maui-tooling](dotnet-maui-tooling/) | Specialist tooling plugin for DevFlow automation, slim bindings, workload discovery, and diagnostics. |

DevFlow runtime skills (`maui-devflow-onboard`, `maui-devflow-debug`, `maui-devflow-session-review`) live in `plugins/dotnet-maui-tooling/skills/`, are bundled with the `maui` CLI by `maui devflow init`, and are exposed through the plugin manifest.

## Installation

For one skill, use a `maui` CLI build that exposes `maui ai add skill`:

```bash
maui ai list skill --env CopilotCli --json
maui ai add skill maui-xcode27-migration --env CopilotCli --yes
```

Run these from the app project. Use `--env Claude` or `--env VsCode` for those
clients. Restart/reload the agent session if needed, then ask it to
"Prepare this MAUI app for Xcode 27" (or explicitly use `maui-xcode27-migration`).
The CLI installs instructions; the agent performs the migration.
**Validation status:** this is a candidate for expert-reviewed testing, not a
validated complex-migration workflow. Evaluations have still produced
compilable code that loses cold window work or additional URLs. Native host
audits now reject those covered failure patterns; source review remains
necessary for other behavior. Aggregate judge scores are not acceptance.
Supported released-toolchain build and runtime qualification remain incomplete.
Automatic selection is not guaranteed;
explicitly requesting the skill controls intent, not migration correctness.
Inspect every callback mapping and validate with a matching released toolchain.

**Availability:** the skill must exist in the public marketplace's selected
revision before the commands can discover it. The `maui ai` command is present
in repository source; older published tools may not expose it. Check
`maui ai --help` for the actual `ai` subcommands, not just a successful exit code.
No minimum published CLI version is asserted here. The tool package ID is
`Microsoft.Maui.Cli`; follow [CLI installation](../src/Cli/README.md) and verify
the chosen package contains `ai` before relying on this route.

Contributor testing can select a reviewed catalog revision with
`--repo dotnet/maui-labs --branch <full-40-character-commit-sha>` on the list
and add commands. A full SHA stays pinned on later updates. Real GitHub
discovery, installation, status and no-op updates have been exercised with an
isolated CI-built CLI for all three clients, including delivery of the audit
script. This does not establish a minimum released CLI version or qualify a
future signed release; repeat the consumer checks against the published package.

Alternatively, install the complete plugin in an agent that supports marketplaces:

```bash
# Add this repo as a marketplace
/plugin marketplace add dotnet/maui-labs

# Install app-development skills
/plugin install dotnet-maui@dotnet-maui-labs

# Install specialist tooling skills
/plugin install dotnet-maui-tooling@dotnet-maui-labs
```

## dotnet-maui skills

| Area | Skill | Description |
|------|-------|-------------|
| App fundamentals | [maui-app-architecture](dotnet-maui/skills/maui-app-architecture/) | Structure MAUI apps with DI, MVVM, Shell routing, compiled bindings, and trim-safe navigation. |
| App fundamentals | [maui-current-apis](dotnet-maui/skills/maui-current-apis/) | Give target-framework-aware guidance for current MAUI APIs and avoid Xamarin.Forms-era suggestions. |
| App fundamentals | [maui-project-structure](dotnet-maui/skills/maui-project-structure/) | Organize single-project resources, app metadata, Central Package Management, fonts, images, and assets. |
| App fundamentals | [maui-ui-patterns](dotnet-maui/skills/maui-ui-patterns/) | Build robust layouts, typed templates, command binding, state handling, and automation-friendly UI. |
| App fundamentals | [maui-accessibility](dotnet-maui/skills/maui-accessibility/) | Add semantic labels, hints, headings, focus behavior, screen-reader announcements, and accessibility test hooks. |
| App fundamentals | [maui-performance](dotnet-maui/skills/maui-performance/) | Improve startup, scrolling, compiled bindings, image sizing, Release-mode measurement, trimming, and NativeAOT safety. |
| App fundamentals | [maui-unit-testing](dotnet-maui/skills/maui-unit-testing/) | Make ViewModels and services testable with fake platform abstractions and clear device/integration boundaries. |
| App features | [maui-app-assets-lifecycle](dotnet-maui/skills/maui-app-assets-lifecycle/) | Manage app icons, splash screens, packaged files, fonts, images, lifecycle events, and state restore. |
| App features | [maui-aspire-client](dotnet-maui/skills/maui-aspire-client/) | Connect MAUI clients to Aspire-hosted services with service discovery, device fallbacks, certificates, and debug cleartext. |
| App features | [maui-auth-secure-storage](dotnet-maui/skills/maui-auth-secure-storage/) | Implement WebAuthenticator and MSAL flows, callback URIs, token storage, logout cleanup, and Blazor Hybrid auth handoff. |
| App features | [maui-blazor-hybrid](dotnet-maui/skills/maui-blazor-hybrid/) | Configure BlazorWebView and HybridWebView, static assets, JS/.NET messaging, trim-safe JSON, and DevFlow CDP debugging. |
| App features | [maui-device-capabilities](dotnet-maui/skills/maui-device-capabilities/) | Use camera, media picker, file picker, geolocation, maps, contacts, permissions, and platform declarations. |
| App features | [maui-localization-theming](dotnet-maui/skills/maui-localization-theming/) | Implement RESX localization, culture switching, RTL layout, platform metadata, AppThemeBinding, and runtime themes. |
| App features | [maui-networking-offline-data](dotnet-maui/skills/maui-networking-offline-data/) | Build typed HttpClient flows, emulator/device networking, retries, cancellation, SQLite/offline sync, and encryption decisions. |
| App features | [maui-notifications-deep-links](dotnet-maui/skills/maui-notifications-deep-links/) | Add local and push notifications, FCM/APNs/Azure Notification Hubs, app links, universal links, URI schemes, and permission UX. |
| Advanced and migration | [maui-ai-tool-bindings](dotnet-maui/skills/maui-ai-tool-bindings/) | Create source-generated Microsoft.Extensions.AI tool bindings with DI-bound parameters, approvals, and per-session scope. |
| Advanced and migration | [maui-controls-deep-dive](dotnet-maui/skills/maui-controls-deep-dive/) | Tune CollectionView, SafeAreaEdges, GraphicsView, gestures, animations, and control-specific semantics. |
| Advanced and migration | [maui-custom-handlers](dotnet-maui/skills/maui-custom-handlers/) | Customize handlers with mappers, platform partials, custom handler structure, and renderer-to-handler migration. |
| Advanced and migration | [maui-essentials-ai](dotnet-maui/skills/maui-essentials-ai/) | Use Microsoft.Maui.Essentials.AI, Apple Intelligence chat, local embeddings, local tool invocation, and fallback UX. |
| Advanced and migration | [maui-labs-platform-targeting](dotnet-maui/skills/maui-labs-platform-targeting/) | Target MAUI Labs GTK4, AppKit, and WPF platforms with project setup and conditional guidance. |
| Advanced and migration | [maui-platform-invoke](dotnet-maui/skills/maui-platform-invoke/) | Wrap native platform APIs behind DI services, permissions, platform metadata, partial platform files, and lifecycle hooks. |
| Advanced and migration | [maui-release-notes](dotnet-maui/skills/maui-release-notes/) | Convert official .NET/MAUI release notes into app upgrade notes, CI changes, requirements, and validation plans. |
| Advanced and migration | [maui-xcode27-migration](dotnet-maui/skills/maui-xcode27-migration/) | Migrate existing iOS/Catalyst apps to Xcode 27.0 scene lifecycle compatibility, preserving custom callbacks and OS-support decisions. |
| Advanced and migration | [xamarin-forms-migration](dotnet-maui/skills/xamarin-forms-migration/) | Audit Xamarin.Forms apps, replace namespaces/APIs, migrate DependencyService and MessagingCenter usage, and plan parity work. |

## dotnet-maui-tooling skills

| Skill | Description |
|-------|-------------|
| [maui-devflow-onboard](dotnet-maui-tooling/skills/maui-devflow-onboard/) | Add MAUI DevFlow packages and app registration to a project. |
| [maui-devflow-debug](dotnet-maui-tooling/skills/maui-devflow-debug/) | Run MAUI DevFlow build, deploy, connection recovery, inspect, and fix loops. |
| [maui-devflow-session-review](dotnet-maui-tooling/skills/maui-devflow-session-review/) | Review opt-in MAUI DevFlow sessions for friction, retries, workarounds, and product feedback. |
| [devflow-automation](dotnet-maui-tooling/skills/devflow-automation/) | Automate MAUI app inspection and debugging workflows through DevFlow tools. |
| [devflow-connect](dotnet-maui-tooling/skills/devflow-connect/) | Diagnose and fix DevFlow agent connectivity issues between the `maui` CLI and running .NET MAUI apps. |
| [maui-ai-debugging](dotnet-maui-tooling/skills/maui-ai-debugging/) | Legacy compatibility skill for older DevFlow clients. |
| [android-slim-bindings](dotnet-maui-tooling/skills/android-slim-bindings/) | Create Android slim bindings using the Native Library Interop approach. |
| [ios-slim-bindings](dotnet-maui-tooling/skills/ios-slim-bindings/) | Create iOS slim bindings using the Native Library Interop approach. |
| [dotnet-workload-info](dotnet-maui-tooling/skills/dotnet-workload-info/) | Discover installed .NET workloads, SDK versions, and dependency requirements. |

## Adding Skills

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide. Quick summary:

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with YAML frontmatter
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Submit a PR -- the `skill-check` workflow validates automatically
4. A maintainer posts `/evaluate` to run LLM-based evaluation
