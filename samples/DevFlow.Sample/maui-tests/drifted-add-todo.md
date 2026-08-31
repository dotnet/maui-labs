# Scenario: adding a todo fails because the add button's AutomationId drifted

This is the repository's worked example of a **deliberately failing** DevFlow run. It is a copy of
`verified-add-todo.md` with one change: the add button is addressed as `AddButtonRenamed`, an
AutomationId the app does not expose. The run is expected to end `locator-not-found` at that step,
which is the input the selector self-repair pipeline is meant to act on.

Because the tap never lands, the plan's independent business oracle also fails: the todo is never
written to the app's durable ledger. That is correct and is why this run is not repair-eligible —
repair eligibility requires the business outcome to have verified independently, so that a drifted
selector is never confused with an app that is genuinely broken.

```json maui-test
{
  "schema": 2,
  "name": "drifted-add-todo",
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
      "args": {
        "selector": { "automationId": "AddButtonRenamed" }
      },
      "acceptanceCriterionIds": ["todo-committed"],
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "CountLabel" },
          "name": "Text",
          "expected": "4 items, 0 completed",
          "verify": true
        }
      ]
    }
  ]
}
```
