# Scenario: an action selector drifts after the todo is already committed

This is the repository's worked example of a **repair-eligible** failing run. It differs from
`drifted-add-todo.md` in the one way that decides eligibility: the drifted selector is on an
**action** that runs *after* the business outcome has already happened.

Three conditions must hold together, and only this shape satisfies all three:

- `drifted-add-todo.md` drifts the **add button**, so the tap never lands, the todo is never
  written to the ledger, and the independent oracle fails. Correctly **not** repair-eligible: when
  the business outcome did not happen, a drifted selector cannot be told apart from an application
  that is genuinely broken.
- Drifting an **assertion** selector is also not repair-eligible
  (`MauiFlowFailureClassifier.AssertionSelectorDrifted`). Re-pointing an assertion would change
  what the test checks, which is the one repair that must never be automatic.
- This flow taps the real `AddButton` and asserts the real `CountLabel`, so the todo is committed
  and the oracle verifies. Only the trailing action addresses `ShowModalButtonRenamed`, an
  AutomationId the app does not expose.

Business outcome independently verified, assertion intact, one **action** selector unresolved: that
is the exact input the selector self-repair pipeline is meant to act on.

```json maui-test
{
  "schema": 2,
  "name": "drifted-assert-after-commit",
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
