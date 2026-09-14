# Microsoft.Maui.AI.Chat.Recording

`Microsoft.Maui.AI.Chat.Recording` records deterministic `IChatClient` request and streaming-response fixtures, then replays them without contacting a provider.

> Experimental API. Recording is intentionally strict: only safe, canonical request and content data are persisted.

## Record, save, and replay

```csharp
var recording = new ChatRecording();
using var recorder = new RecordingChatClient(provider, new ChatRecordingOptions
{
    Mode = ChatRecordingMode.Record,
    Recording = recording,
});

await recorder.GetResponseAsync(messages);
ChatRecordingStore.Save(recording, "fixtures/chat.json");

using var replay = new ReplayChatClient(new ChatRecordingOptions
{
    FixturePath = "fixtures/chat.json",
});
var response = await replay.GetResponseAsync(messages);
```

## Provider request extensions

`ChatOptions.RawRepresentationFactory` is denied by default; neither the delegate nor its provider result is reflected or written to a fixture. A provider integration may deliberately opt into deterministic request matching with an `IChatRecordingRequestCodec` registered in `ChatRecordingOptions.RequestCodecs`. The codec must emit and read a canonical, JSON-only extension. Extensions pass the same strict sanitizer as the rest of the request.

## Sanitization and supported data

The strict sanitizer rejects unsafe request/content values and URI forms (credentials, queries, fragments, and recognized provider endpoints). Provider exceptions with bearer tokens, API keys, headers, cookies, connection strings, or URLs are saved only as the stable `[REDACTED provider error]` message. Content supports the built-in safe MEAI content forms, rich text with safe HTTP(S) links/images/definitions, registered content codecs, and registered raw codecs.

Content envelopes preserve allowlisted `AIContent.AdditionalProperties` (`AllowedContentAdditionalProperties`) and base/citation annotations, including text-span regions. Annotation raw representations and unknown annotation/region types are deliberately rejected: no reflection or provider-native raw objects are persisted. `TextReasoningContent.ProtectedData` is never recorded.
