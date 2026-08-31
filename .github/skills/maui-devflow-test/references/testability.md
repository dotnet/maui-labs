# App Testability Improvements

Testability advice is separate from authoring and repair. It is a product
recommendation for a human developer to review; it does not alter an app,
selector, draft, committed flow, or source-proposal lifecycle.

Give the recommendation from the observed limitation without agent discovery
or target/device questions. Ask for an exact target only if the user later
asks to turn the recommendation into a local reproduction or executable draft.

## Recommend

- A unique, app-owned `AutomationId` for an important interactive control.
- A stable model key on a repeated item-template root so recorded children use
  the complete `AutomationId + collectionScope + stableItemKey` composite.
  Each field is required; a partial composite is not a durable selector.
- A documented reset/seed provider for repeatable, stateful test tenants.
- An independent business oracle for outcomes that must be verified beyond UI.
- Explicit route and hard postcondition coverage for meaningful transitions.
- A typed `notExists` assertion when dismissal or removal is the intended UI
  postcondition; do not substitute existence of an underlying page element.

## Do Not Recommend as a Fix

- Reusing a duplicate AutomationId.
- Text, position, type/index, bounds, runtime IDs, or screenshots as a
  substitute durable identity.
- Static IDs on template, repeater, virtualized, generated, or dynamic elements
  without an item scope/key design.
- A code edit presented as an automatic repair.

Describe the observed limitation, the proposed app-owned design, its scope, and
why it improves future tests. State that source review and application are a
separate human-controlled process.
When the complete composite is unavailable, hand this recommendation to the
app developer and keep the test draft inert; do not substitute text, index, or
coordinates.
