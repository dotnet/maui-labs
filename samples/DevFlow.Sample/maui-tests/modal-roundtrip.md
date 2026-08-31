# Scenario: modal-roundtrip

<!-- Recorded by MAUI DevFlow. The fenced ```json maui-test block below is the source of
     truth for replay; edit the prose freely but keep that block valid. -->

- **App:** com.companyname.mauitodo
- **Platform:** android,windows
- **Recorded:** (unknown)
- **Preconditions:** (none)
- **Steps:** 2

## Steps

1. Tap
   - Expect target still present
2. Tap
   - Expect target no longer present
   - Expect target still present

## Replay (machine-readable — source of truth)

```json maui-test
{
  "schema": 2,
  "name": "modal-roundtrip",
  "app": "com.companyname.mauitodo",
  "platform": "android,windows",
  "recordedAt": null,
  "preconditions": null,
  "steps": [
    {
      "seq": 1,
      "action": "tap",
      "label": null,
      "intent": null,
      "acceptanceCriterionIds": null,
      "target": null,
      "value": null,
      "args": {
        "selector": {
          "automationId": "ShowModalButton",
          "text": null,
          "id": null,
          "typeIndex": null,
          "type": null,
          "index": null,
          "selectorKind": null,
          "matchCount": null,
          "quality": null,
          "fragilityReasons": null
        },
        "text": null,
        "name": null,
        "value": null,
        "route": null,
        "theme": null,
        "valueSource": null,
        "secretEnvironmentVariable": null,
        "element": null,
        "dx": null,
        "dy": null,
        "itemIndex": null,
        "position": null,
        "animated": null
      },
      "page": null,
      "navigated": false,
      "fragile": false,
      "screenshot": null,
      "asserts": [
        {
          "kind": "exists",
          "selector": {
            "automationId": "ModalTitle",
            "text": null,
            "id": null,
            "typeIndex": null,
            "type": null,
            "index": null,
            "selectorKind": null,
            "matchCount": null,
            "quality": null,
            "fragilityReasons": null
          },
          "name": null,
          "expected": null,
          "verify": true,
          "note": null
        }
      ],
      "selectorEvidence": null
    },
    {
      "seq": 2,
      "action": "tap",
      "label": null,
      "intent": null,
      "acceptanceCriterionIds": null,
      "target": null,
      "value": null,
      "args": {
        "selector": {
          "automationId": "CloseModalButton",
          "text": null,
          "id": null,
          "typeIndex": null,
          "type": null,
          "index": null,
          "selectorKind": null,
          "matchCount": null,
          "quality": null,
          "fragilityReasons": null
        },
        "text": null,
        "name": null,
        "value": null,
        "route": null,
        "theme": null,
        "valueSource": null,
        "secretEnvironmentVariable": null,
        "element": null,
        "dx": null,
        "dy": null,
        "itemIndex": null,
        "position": null,
        "animated": null
      },
      "page": null,
      "navigated": false,
      "fragile": false,
      "screenshot": null,
      "asserts": [
        {
          "kind": "notExists",
          "selector": {
            "automationId": "ModalTestPage",
            "text": null,
            "id": null,
            "typeIndex": null,
            "type": null,
            "index": null,
            "selectorKind": null,
            "matchCount": null,
            "quality": null,
            "fragilityReasons": null
          },
          "name": null,
          "expected": null,
          "verify": true,
          "note": null
        },
        {
          "kind": "exists",
          "selector": {
            "automationId": "CountLabel",
            "text": null,
            "id": null,
            "typeIndex": null,
            "type": null,
            "index": null,
            "selectorKind": null,
            "matchCount": null,
            "quality": null,
            "fragilityReasons": null
          },
          "name": null,
          "expected": null,
          "verify": true,
          "note": null
        }
      ],
      "selectorEvidence": null
    }
  ]
}
```
