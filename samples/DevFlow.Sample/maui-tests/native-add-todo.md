# Scenario: add a seeded native todo

```json maui-test
{
  "schema": 2,
  "name": "native-add-todo",
  "app": "com.companyname.mauitodo",
  "platform": "android,windows",
  "preconditions": "Android or Windows lifecycle reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "fill",
      "args": {
        "selector": { "automationId": "NewTodoEntry" },
        "text": "Tier one item"
      }
    },
    {
      "seq": 2,
      "action": "tap",
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
    }
  ]
}
```
