# Demo-only scenario: a trailing action selector that does not exist

**This flow is demo-only and is intended to fail.** It exists to drive the
`demo-ci-fix` lane of `.github/workflows/devflow-integration.yml` end to end, so a
manager-facing walkthrough can show a real red CI run, a real bounded evidence upload, a real
nonqualified demo issue, and the local `maui-devflow-ci-fix` route. Nothing about a failure of
this flow says the sample application regressed.

The `demo-` filename prefix is load-bearing. `AndroidFlowPilotTests.LoadTierOneFlowsAsync` never
loads a `demo-`-prefixed flow into the ordinary Android Tier-1 pilot, so this file cannot turn the
required `devflow-flow-gate` red. Only the explicit opt-in environment filter
`DEVFLOW_FLOW_PILOT_DEMO_FLOW=demo-ci-fix-drift.md` selects it, and when it is set that is the
only flow the pilot loads.

The shape is copied from `drifted-assert-after-commit.md`, the repository's worked example of a
**repair-eligible** failing run, because that is the only shape that makes the demo honest:

- The flow taps the real `AddButton`, so the business outcome really happens.
- The flow asserts the real `CountLabel`, so the assertion is intact and unchanged.
- The independent `android-app-storage` oracle reads the app's private todo ledger over adb, so
  the outcome is verified outside the UI the flow drove.
- Only the trailing **action** addresses `ShowModalButtonRenamed`, a drifted AutomationId whose
  real counterpart is the app's `ShowModalButton`.

Business outcome independently verified, assertion intact, one trailing **action** selector
unresolved: that is a `locator-not-found` failure the selector self-repair pipeline is meant to
act on, and it is exactly the failure the local CI-fix route is meant to diagnose. It is not an
application defect and it is not infrastructure.

Do not "fix" this flow by re-pointing the trailing selector at a real control. The failure is the
point. If this file ever stops failing, the demo lane stops demonstrating anything.

```json maui-test
{
  "schema": 2,
  "name": "demo-ci-fix-drift",
  "app": "com.companyname.mauitodo",
  "platform": "android",
  "preconditions": "The app is freshly installed, so its todo ledger holds only the three seeded records.",
  "steps": [
    {
      "seq": 1,
      "action": "fill",
      "args": {
        "selector": { "automationId": "NewTodoEntry" },
        "text": "Ledger verified item"
      }
    },
    {
      "seq": 2,
      "action": "tap",
      "acceptanceCriterionIds": ["todo-committed"],
      "args": {
        "selector": { "automationId": "AddButton" }
      },
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "CountLabel" },
          "name": "Text",
          "expected": "4 items, 0 completed",
          "verify": true
        }
      ]
    },
    {
      "seq": 3,
      "action": "tap",
      "args": {
        "selector": { "automationId": "ShowModalButtonRenamed" }
      }
    }
  ]
}
```
