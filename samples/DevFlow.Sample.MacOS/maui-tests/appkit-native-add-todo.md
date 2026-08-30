# Scenario: add a seeded native AppKit todo

```json maui-test
{
  "schema": 2,
  "name": "appkit-native-add-todo",
  "app": "com.companyname.mauitodo.appkit",
  "platform": "macos",
  "preconditions": "Experimental AppKit fixture reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "fill",
      "args": {
        "selector": { "automationId": "NewTodoEntry" },
        "text": "AppKit Tier One"
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
