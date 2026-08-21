# Apple Vision document corpus results

**Request:** `RecognizeDocumentsRequest` revision 1

**Baseline:** macOS 26.5 SDK, Mac Catalyst 26 and iOS 26 simulator, Xcode 26.6
**Image size:** 1600 x 1200 pixels

This corpus separates what Apple Vision detects from what the managed mapper
normalizes. The tests assert both the raw node model and the public document
elements where the behavior is stable.

## Capability summary

| Fixture | Raw Vision result | Normalized assertion |
|---|---|---|
| Headings and paragraphs | One title and seven paragraphs | Title and body blocks retain expected text |
| Table | One 4 x 3 table with 12 cells | Row, column, cell text, and geometry map to `DocumentTable` |
| Detected data | Link, email, phone, postal address, calendar event, and money amount | All six semantic types remain in Apple metadata |
| Barcodes | QR and Code 128 with expected payloads | Two `AppleBarcodeElement` values retain symbology, payload, confidence, and geometry |
| Mixed document | Title, paragraphs, 3 x 2 table, list, link, email, and calendar events | Heterogeneous top-level elements remain available together |

## List behavior

### Flat multi-item list

Vision recognizes the three bullets as one list with three items. For every
item, however, `item.content` reports:

- two identical paragraph nodes for the same marked line;
- one list with the same polygon as the item;
- two identical list items containing the same text and marker;
- descendant content that re-enters the same semantic container.

The three-item fixture projects 26 raw nodes and prunes six ancestor re-entries.
The normalized result contains one three-item list and no nested lists.

### Visually nested list

The fixture uses top-level bullets and indented hyphen children. Revision 1
returns one flat seven-item list:

```text
Documents, Passport, Visa, Packing, Jacket, Shoes, Charger
```

Marker types and item polygons preserve the bullet/hyphen and indentation
signals, but Vision does not expose parent/child list hierarchy. Each item also
contains the same self-projection described above. The fixture projects 54 raw
nodes and prunes 14 ancestor re-entries.

### Single-item and table-contained bullets

Vision recognizes the text but does not classify either fixture as a list:

- a standalone one-item bullet is returned as text;
- bullets in separate table cells are returned as cell text.

Consumers must not assume every visible bullet becomes
`DocumentObservation.Container.List`.

## Mapper decision

`AppleListItemElement` is treated as the normalized semantic leaf for its own
line. The native snapshot remains available for inspection, but the mapper
filters a child paragraph or list only when all of the following identify an
exact self-projection:

- polygon matches the owning list item;
- text or item string matches the owning item;
- marker matches for list children;
- every child item describes that same owning item.

Different nested tables, lists, barcodes, or paragraphs still flow through.
Ancestor fingerprint detection plus depth and node limits remain final safety
guards.

Barcode assertions run on Mac Catalyst and physical iOS devices. The iOS
simulator test returns early because Vision barcode detection is not consistently
available there.

## Expected evolution

These tests intentionally capture revision-1 behavior. If a future Apple
revision starts exposing true nested lists, recognizing single-item lists, or
stops returning self-projections, the corpus should fail visibly so the mapper
and this baseline can be revised rather than silently changing behavior.
