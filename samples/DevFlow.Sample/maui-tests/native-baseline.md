# Scenario: seeded native baseline

The deterministic sample seed starts on the native route with three incomplete todos.

```json maui-test
{
  "schema": 2,
  "name": "seeded-native-baseline",
  "app": "com.companyname.mauitodo",
  "platform": "android,windows",
  "preconditions": "Android or Windows lifecycle reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "assert",
      "asserts": [
        {
          "kind": "exists",
          "selector": { "automationId": "AddButton" },
          "verify": true
        },
        {
          "kind": "propEquals",
          "selector": { "automationId": "CountLabel" },
          "name": "Text",
          "expected": "3 items, 0 completed",
          "verify": true
        }
      ]
    }
  ]
}
```
