# Experimental AppKit flow corpus

These are the dedicated Tier-1 flows for `DevFlow.Sample.MacOS`, the native AppKit fixture.
They intentionally use only stable AutomationIds from the shared DevFlow sample and declare
`platform: "macos"` only. They are **not** Mac Catalyst coverage.

The corpus covers the AppKit fixture's managed/native identity, Shell navigation, in-app modal
equivalent, button and text-entry handlers, and deterministic in-memory seed. Native system dialogs,
multi-window automation, and WebView/CDP are capability-gated contracts rather than Tier-1 claims.
