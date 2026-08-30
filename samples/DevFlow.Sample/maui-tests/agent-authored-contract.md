# Scenario: reviewed agent-authored baseline

This flow is a committed, human-reviewed semantic artifact used to verify that agent-authored
drafts use the same runner and selector contract as human-authored Tier-1 flows.

```json maui-test
{
  "schema": 2,
  "name": "agent-authored-contract",
  "app": "com.companyname.mauitodo",
  "platform": "android,windows",
  "preconditions": "Clean lifecycle reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "assert",
      "intent": "Verify the reviewed target has one stable native identity.",
      "asserts": [
        {
          "kind": "exists",
          "selector": { "automationId": "AddButton" },
          "verify": true
        }
      ]
    }
  ]
}
```
