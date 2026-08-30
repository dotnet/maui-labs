# Scenario: adding a todo commits it to the app's durable ledger

This is the repository's worked example of a **verified** DevFlow run. The UI assertion below is
not what makes it verified: the plan sidecar declares an independent business oracle that reads
the app's private storage over adb, outside the DevFlow agent channel this flow drives.

```json maui-test
{
  "schema": 2,
  "name": "verified-add-todo",
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
        "selector": { "automationId": "AddButton" }
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
