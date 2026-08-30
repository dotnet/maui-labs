# Scenario: fill a native todo field

```json maui-test
{
  "schema": 2,
  "name": "native-fill",
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
      },
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "NewTodoEntry" },
          "name": "Text",
          "expected": "Tier one item",
          "verify": true
        }
      ]
    }
  ]
}
```
