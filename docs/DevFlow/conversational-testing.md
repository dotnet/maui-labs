# Conversational DevFlow collaborative testing

> **Experimental preview.** The conversational layer helps an AI agent prepare
> safe, reviewable DevFlow test work. It does not change the restricted
> test-agent protocol, authorize operations, or make an agent a test owner.

The optional `maui-devflow-test` skill is distributed in the `dotnet-maui`
plugin and bundled with the `maui` CLI. It is intentionally not part of
`maui devflow init`; install it explicitly with:

```text
maui devflow skills install
```

## First-time setup

Before authoring or running a conversational test, start a **Debug** build of
the app with the DevFlow agent connected. The broker-owned authoring surface is
opt-in. Set both environment variables in the process environment that starts
the broker:

```powershell
$env:DEVFLOW_PREVIEW_WORKBENCH = 'true'
$env:DEVFLOW_PREVIEW_AGENT_AUTHORING = 'true'
```

The advanced proposal/import surfaces are separate, optional opt-ins; enable
only the one needed:

```powershell
$env:DEVFLOW_PREVIEW_REPAIR_PROPOSALS = 'true'
$env:DEVFLOW_PREVIEW_SOURCE_PROPOSALS = 'true'
$env:DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT = 'true'
```

Restart the broker after changing these variables, then confirm the gate is open
with `maui devflow approve --list --json`: `ok: true` means the authoring route
is enabled. A kill switch that names `workbench` or `agent-authoring` in
`DEVFLOW_PREVIEW_KILL_SWITCHES` keeps the corresponding surface unavailable.

Install to the project skill directory used by the agent host. For example,
GitHub uses `.github/skills`:

```text
maui devflow skills install --scope project --target github
```

The target presets are `github` (`.github/skills`), `claude`
(`.claude/skills`), `agent` (`.agent/skills`), and `agents`
(`.agents/skills`). `--target auto` first reuses a directory containing a
current DevFlow skill, then any existing skill directory, and otherwise falls
back to `claude`. Specify the target explicitly when the project uses more
than one host.

The skill is for a conversation that may author an inert draft, guide a human
review, explain a run result, discuss a selector-repair handoff, recommend
application testability improvements, or summarize CI evidence. It works with
the restricted profile:

```text
maui devflow mcp --profile test-agent
```

See [Restricted DevFlow test-agent protocol](test-agent.md) for the actual
tool inventory, grants, and enforcement rules, and [human-authored testing](testing.md)
for the underlying flow lifecycle.

## Walkthrough: what the loop looks like

A worked end-to-end pass, from an empty terminal to a reviewed repair. Each
command here is the one an operator actually types.

### 1. Bring the pieces up

```powershell
$env:DEVFLOW_PREVIEW_WORKBENCH = 'true'
$env:DEVFLOW_PREVIEW_AGENT_AUTHORING = 'true'

maui devflow broker start
maui devflow skills install --scope project --target github
maui devflow mcp --profile test-agent
```

Both variables must be set **in the process environment that starts the broker
and the MCP server**. They default to off; without
`DEVFLOW_PREVIEW_AGENT_AUTHORING` the `test-agent` profile refuses to start and
reports that it is a disabled preview surface.

The last command above runs the MCP server in the foreground, which suits a
terminal agent. An IDE starts the server itself, so it needs the same command as
configuration instead. In VS Code that is `.vscode/mcp.json`:

```json
{
  "servers": {
    "maui-test-agent": {
      "type": "stdio",
      "command": "<absolute path to maui or maui.exe>",
      "args": ["devflow", "mcp", "--profile", "test-agent"],
      "env": {
        "DEVFLOW_PREVIEW_AGENT_AUTHORING": "true",
        "DEVFLOW_PREVIEW_WORKBENCH": "true",
        "DEVFLOW_PREVIEW_REPAIR_PROPOSALS": "true"
      }
    }
  }
}
```

`.vscode/` is git-ignored in this repository, and the `command` is an absolute
path, so this file is per-machine and is not shared through the repository. The
`env` block here covers only the MCP server the IDE launches; the broker is a
separate long-running process and needs the variables in *its* environment too,
as below.

> **The broker is a long-running daemon and does not re-read these.** If a
> broker is already running, exporting the variables in a shell, or setting them
> in an agent host's MCP configuration, changes nothing for it: the authoring
> route stays closed and every draft is refused with *"agent authoring preview is
> disabled"*. Stop and restart the broker from an environment that has the
> variables set:
>
> ```powershell
> $env:DEVFLOW_PREVIEW_AGENT_AUTHORING = 'true'
> maui devflow broker stop
> maui devflow broker start
> maui devflow approve --list      # ok:true means the gate is open
> ```
>
> An agent host that spawns the MCP server with its own `env` block covers the
> MCP process only. To avoid the mismatch entirely, set both variables at user
> or machine scope so every later process inherits them.

Start a Debug build of the app so its agent registers and confirm it with
`maui devflow list`. `maui devflow approve` is the approval host in this layer;
it is a command, not a running service, so nothing extra needs to be open. If no
one decides the requests it lists, drafts still get prepared but every mutation
stops at a pending request.

Keep exactly one instance of the app under test connected. Approvals bind to one
exact target, so a second instance makes `maui devflow approve` fail as
`AgentAmbiguous`, and a second instance on another platform can take the agent
port and turn runs into infrastructure failures.

> **Restarting the broker does not invalidate the app you are testing.** The
> agent instance identity that approvals and sessions bind to is derived from the
> app's own process, so it survives a broker restart or a dropped connection and
> changes only when the app itself is relaunched. Relaunching the app *does*
> invalidate every retained draft, approval, and run binding, because they were
> reviewed against a process that no longer exists — expect to re-request them.

### 2. Describe the journey

> "Write a test that adds a todo and checks the count goes to 4."

The skill does not begin drafting. It restates the journey as numbered steps in
the user's own vocabulary and marks each one `[known]`, `[unknown: selector]`,
`[unknown: oracle]`, or `[unknown: reset]`. `[known]` means *observed* in a tree
dump, a committed flow, or a user statement — a control that is "probably called
`AddButton`" is `[unknown: selector]`. See
[conversational intake](../../plugins/dotnet-maui/skills/maui-devflow-test/references/intake.md).

### 3. Answer one combined question

Every unknown arrives in a single message naming the step number and the
smallest answer that unblocks it — never one question per step, and never a
guessed default to keep things moving.

The oracle question is the one that decides whether the test can ever be
trusted. Answering "the label on screen" yields a run that can pass but reports
`verified: false`. Naming something independent — a record the test can query
outside the channel the agent drove — is what produces `verified: true`.

### 4. Approve, explicitly

When the agent needs to commit, run, or patch, it stops and names a pending
request:

```powershell
maui devflow approve --list --agent-port <port>
maui devflow approve <request-id> --agent-port <port>
maui devflow approve <request-id> --agent-port <port> --reject --reason scope-too-broad
```

Pass `--agent-port` whenever more than one app is connected; approvals bind to
one exact target and the command refuses to guess. This layer ships no editor
extension and no Inspector agent-request inbox, so this command is the whole
decision surface. **A chat reply approves nothing**; the skill is required to
say so and point at the request rather than proceed.

`maui devflow approve` is **operator convenience, not an authorization
boundary**. It reads the owner-only approval token from the local broker state
file and performs the same confirmation ceremony an editor extension would. An
MCP agent runs as the same OS user with the same file permissions, so it can
read that token and call the same routes. The command makes the decision
reachable for the person at the machine; it does not, and on a shared user
account cannot, prove that a human rather than a local agent process decided.

#### Decide promptly — there are two clocks

| Clock | Default | What it bounds |
|---|---|---|
| Authoring session | 30 min | How long the whole draft-and-review session stays alive |
| Approval decision window | 10 min | How long *you* have to decide on one request |
| Issued grant | 300 s (`--grant-seconds`, 1-900) | How long the approved action stays usable |

The decision window is **clamped to whatever remains of the authoring session**,
so it is often shorter than 10 minutes:

```csharp
expiresAt = request.ExpiresAt ??
    (defaultExpiresAt <= session.ExpiresAt ? defaultExpiresAt : session.ExpiresAt);
```

An agent that spends 25 minutes reading files, validating, and retrying leaves
you 5 minutes to approve, not 10. A request that is not decided in time moves to
`state: expired` with `reasonCode: decision-window-expired`, and
`maui devflow approve --list` then reports `pendingCount: 0`.

Nothing is lost when that happens — an unapproved draft writes no files — but
the work must be redone. Ask the agent to request approval again, and decide
while the request is fresh. `maui devflow approve --list` prints the `expires`
timestamp for exactly this reason.

### 5. Read the failure, review the repair

A failed run is classified as selector drift, an app regression, infrastructure,
or inconclusive. Only drift is repairable, and the proposal is inert until a
human approves it. An app regression must be reported, not "repaired": the test
was right. Thin evidence stays inconclusive rather than becoming a guess.

Two independent authorities must agree before a repair may even be proposed. The
failure classifier judges the *symptom*: only a pre-dispatch missing-selector
failure on a verified, matching checkpoint qualifies, and assertion-selector
drift never does, because re-pointing an assertion changes what the test checks.
Replay safety separately judges whether the run is a sound *basis* to repair
from, which requires the plan's required independent business oracle to have
actually verified the business outcome. `failure.repairEligible` is the
conjunction; `failure.classifierRepairEligible` records the first half alone, so
a refusal by the second is not mistaken for a misclassification.

That distinction has a practical consequence for where a run must come from. The plan's required
independent oracle has to have actually verified the business outcome, and that evidence is produced
out of band — over adb, outside the agent channel the flow drove.

`maui devflow flow run` gets that for free: it installs the app, so app-private storage is empty
when the run starts and anything read afterwards was written by that run. A run started through
`maui_test_run` attaches to an app that was already running with arbitrary prior state, so presence
alone proves nothing. Those runs are therefore evaluated against a **baseline** taken before the
run: a record only counts as this run's evidence if it was absent beforehand and present after. A
run whose declared record already existed stays unverified and repair-ineligible, as does one whose
device cannot be identified unambiguously.

Both kinds of run can now reach the repair pipeline. `maui_test_failure` reports the two authorities
separately — `classifierEligible` and `admissionEligible` — and names the admission's own reason
codes, so a refusal points at its actual cause instead of blaming the failure shape.

A legitimate repair re-points a test at what it always meant to check. Deleting
an assertion, flipping `verify: true` to `false`, relaxing an expected value to
match what happened, widening a selector until something matches, or adding
sleeps are all excluded.

`maui devflow flow triage --manifest <execution-manifest.json> --report
<flow-run.json>` classifies a completed run from its artifacts alone and lists
every reason repair is withheld. It treats those artifacts as imported evidence,
so it is deliberately diagnostic-only and always reports
`imported-evidence-diagnostic-only`.

### Operational notes

- **Close other instances of the app under test first.** A second instance takes
  the agent port; on Windows the next port may fall inside a Hyper-V/WSL reserved
  range, and the run fails as infrastructure rather than as a test result.
- **Local reproduction of a CI failure does not currently reach `matched: true`
  on Android.** Behaviour reproduces — step and runtime fingerprints agree — but
  package identity cannot, because Android packaging is not byte-reproducible
  across builds. Repair review therefore remains the end of the automated path.

## Conversational contract

The skill asks questions only when an answer is necessary to bind the work
safely. It asks when several projects, devices, agents, artifacts, or durable
selectors match; when a repeated item needs a stable key; when a required
business oracle or reset policy is absent; or when a requested platform/action
is unavailable. It must never silently choose the first candidate.

A chat response is not authorization. An owner-token approval host issues
opaque, short-lived grants for the exact process, plan/flow revision and digest,
selector/action scope, build/seed state, and operation after a digest-bound
confirmation. This layer ships exactly one such host, the `maui devflow approve`
command, and it is local operator convenience rather than an attested human
gate. Chat remains non-authoritative, so the skill must stop at inert work or a
pending request when no one decides it. The skill cannot manufacture, reuse, or
broaden grants.

## Authoring and execution

Restricted MCP authoring is broker-owned and inert. The agent can prepare a
complete plan, actions, assertions, and review request, but it cannot broadly
automate the app, edit source, or commit without a human-issued commit grant.
Only the resulting committed Markdown flow is executable, and it requires a
separate human-approved run grant. Until someone decides the pending request,
stop at the inert draft.

For a repeatable or mutating flow, the plan must accurately declare its
side-effect and reset/seed policy. `test-tenant-resettable` is valid only when
the host really provides and verifies that reset/seed contract. A
`non-replayable` flow needs a distinct one-shot grant; it cannot be retried,
continued, or used for repair validation automatically.

A UI transition, toast, screenshot, or report does not independently prove a
business result. Where a verified pass or repair requires one, declare and
check an independent business oracle. An ordinary policy-permitted replay
without such an oracle must be reported as not independently verified.

## Failures, repairs, and testability

The conversation verifies route and recorded checkpoint facts before calling a
missing element selector drift. Wrong route, modal, build, seed, locale,
window, capability, assertion, or ambiguity failures are not selector
repairable.

The agent can keep an inert selector proposal or preview through
`maui_test_patch`, but cannot approve or apply it. Any repair requires a fresh
current local reproduction (including for imported evidence), then a separate
human validation, approval, apply, and verification ceremony. Applying a repair
to the workspace is not part of this layer.

Recommendations such as adding a unique app-owned `AutomationId`, stable
collection item key, reset provider, or business oracle are **testability
improvements**, not repairs. They remain separate from a selector proposal and
from source modification.

## CI evidence

Imported CI `flow-run.json` reports and `.mauitrace` evidence are diagnostic
only. Even independently attested provenance cannot authorize execution,
repair, source changes, platform qualification, or a physical-device claim. A
new local run must match current flow, app, target, failure, and checkpoint
facts before imported failure evidence can enter the human repair path.

When the developer asks Copilot to take a `devflow-ci-failure` issue all the
way through a local source fix, use the separate `maui-devflow-ci-fix` skill.
That route uses the operator CLI for a fresh device-backed reproduction, then
normal Copilot workspace editing and `maui devflow flow run` for the post-fix
check. It leaves an uncommitted Source Control diff. This does not add source
authority to the restricted `test-agent` MCP profile or make imported evidence
authoritative.
