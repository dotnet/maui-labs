# Scenario: AppKit Shell navigation and native button handler

```json maui-test
{
  "schema": 2,
  "name": "appkit-shell-navigation",
  "app": "com.companyname.mauitodo.appkit",
  "platform": "macos",
  "preconditions": "Experimental AppKit fixture reset and devflow-sample-v1 seed have completed.",
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
