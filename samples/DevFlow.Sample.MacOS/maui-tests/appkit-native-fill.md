# Scenario: fill a native AppKit entry

```json maui-test
{
  "schema": 2,
  "name": "appkit-native-fill",
  "app": "com.companyname.mauitodo.appkit",
  "platform": "macos",
  "preconditions": "Experimental AppKit fixture reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "fill",
      "args": {
        "selector": { "automationId": "NewTodoEntry" },
        "text": "AppKit text"
      },
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "NewTodoEntry" },
          "name": "Text",
          "expected": "AppKit text",
          "verify": true
        }
      ]
    }
  ]
}
```
