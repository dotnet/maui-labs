# Scenario: invoke a button event

```json maui-test
{
  "schema": 2,
  "name": "interaction-button",
  "app": "com.companyname.mauitodo",
  "platform": "android,windows",
  "preconditions": "Android or Windows lifecycle reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "navigate",
      "args": { "route": "//interactions" }
    },
    {
      "seq": 2,
      "action": "tap",
      "args": {
        "selector": { "automationId": "TestButton" }
      },
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "StatusLabel" },
          "name": "Text",
          "expected": "last action: button: TestButton",
          "verify": true
        }
      ]
    }
  ]
}
```
