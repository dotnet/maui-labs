# Apple RecognizeDocuments implementation experiment

**Status:** Non-shipping prototype in #450.
**Contract:** [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) at
main `f8ee27151b21e33793164b39de5bcebfdced81a4` (merged November 5, copied November 10 while packages await publication).
**Native API:** Swift Vision `RecognizeDocumentsRequest`, iOS/Mac Catalyst/macOS 26+.

## Pipeline

```text
caller-owned image/PDF stream
  -> internal AppleVisionRecognizeDocumentsProcessor / PDFKit
  -> RecognizeDocumentsRequestNative / bounded native snapshot
  -> completed ExtractionDocument with local AIContent, source references and table spans
      -> explicit rich playground inspection
      -> local AppleVisionRecognizeDocumentsReader projection
```

The final shared model is a non-disposable exchange document, not a page-operation/finalization framework.
One canonical Apple mapping is used; the reader does not interpret raw snapshots independently.
All three upstream projects are copied exactly and file-linked through separate local adapters. There is no private substitute contract.

## Fidelity and extensibility

Use exact typed source polygons/coordinate frames, independent cell sources, spans and mixed-content sections.
Do not abuse arbitrary metadata or source-reference strings as native payload transport.
Unknown native capabilities remain provider-specific or absent, not inferred normalized fields.
Apple does not return figure crops/captions, standardized cell roles or a model deployment ID.

Native details use the borrowed document/payload raw properties and explicit source-reference inspection. Confidence, generalized
annotations and structured-extension envelopes are absent; do not smuggle them through metadata.
The Apple JSON bridge snapshot is not an original response body or a live Swift object. No standard capture opt-out, native/body
statuses or response-content ownership remain. Operation output is never stored as mutable client last-response.

## Native safety

The Swift bridge uses Apple terminology, preserves cancellation and orientation, and invokes completion directly rather than redispatching
onto an operation queue blocked by managed page processing. PDFKit rendering remains internal with finite bounds and bounded scale.
Apple's synthesized Codable can recurse through framework-produced list/table content; ancestor re-entry detection and traversal limits
remain necessary. A bounded snapshot is a deliberate safety boundary, not a promise that every future native field is automatically captured.

## Application

Documents retains the same services/view-model/page-area-composer-settings composition as Chat, Embeddings and Images.
The playground exercises completed-document extraction, the concrete ingestion reader, per-call extractor/native settings and explicit
native inspection. It displays payload/frame/polygon/span/role data; raw objects are not implicitly serialized into normalized output.
Page streaming and capture controls are removed rather than emulated.

Azure Document Intelligence and Foundry Mistral remain explicit sample-only providers using existing configured resources/transports.
No client fallback, extra cloud resource, credentials in source or distribution of secret-embedded Debug binaries is introduced.

See [corrected adoption details](document-extraction-corrected-api.md) and [feature gaps](apple-document-extraction-gaps.md).
The old investigations in [historical feedback](apple-document-extraction-feedback.md) are retained as history, not current mapping rules.
