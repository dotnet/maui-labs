# Scenario: seeded native AppKit baseline

```json maui-test
{
  "schema": 2,
  "name": "appkit-native-baseline",
  "app": "com.companyname.mauitodo.appkit",
  "platform": "macos",
  "preconditions": "Experimental AppKit fixture reset and devflow-sample-v1 seed have completed.",
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
