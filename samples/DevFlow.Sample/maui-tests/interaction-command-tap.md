# Scenario: invoke a command-bound gesture

```json maui-test
{
  "schema": 2,
  "name": "interaction-command-tap",
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
        "selector": { "automationId": "CommandTapGrid" }
      },
      "asserts": [
        {
          "kind": "propEquals",
          "selector": { "automationId": "StatusLabel" },
          "name": "Text",
          "expected": "last action: Command tap: CommandTap",
          "verify": true
        }
      ]
    }
  ]
}
```
