# Scenario: AppKit modal equivalent round trip

```json maui-test
{
  "schema": 2,
  "name": "appkit-modal-roundtrip",
  "app": "com.companyname.mauitodo.appkit",
  "platform": "macos",
  "preconditions": "Experimental AppKit fixture reset and devflow-sample-v1 seed have completed.",
  "steps": [
    {
      "seq": 1,
      "action": "tap",
      "args": {
        "selector": { "automationId": "ShowModalButton" }
      },
      "asserts": [
        {
          "kind": "exists",
          "selector": { "automationId": "ModalTitle" },
          "verify": true
        }
      ]
    },
    {
      "seq": 2,
      "action": "tap",
      "args": {
        "selector": { "automationId": "CloseModalButton" }
      },
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
