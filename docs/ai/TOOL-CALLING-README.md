# Windows Language Model Structured Output and Tool Calling

## Current API support

Windows App SDK 2.3.1 promoted schema-constrained JSON generation to the
stable `Microsoft.Windows.AI.Text.LanguageModel` API:

```csharp
GenerateStructuredJsonResponseAsync(string prompt, string jsonSchema)
GenerateStructuredJsonResponseAsync(string prompt, string jsonSchema, LanguageModelOptions options)
```

`PhiSilicaChatClient` maps `ChatResponseFormatJson` requests with a schema to
this API. The previous schema-prompt injection and code-fence cleanup are no
longer needed for ordinary structured output.

The native API uses constrained decoding and returns
`GenerateStructuredJsonResponseResult`. A successful result has
`GenerateStructuredJsonResponseStatus.Complete`; invalid or unsupported output
is reported explicitly instead of being accepted as arbitrary text.

The supported JSON Schema subset is:

- `type`
- `properties`, including nested objects
- `required`
- `enum`
- `items`

The API does not support `$ref`, `oneOf`, `anyOf`, `patternProperties`, or
`additionalProperties`. It also has no `LanguageModelContext` overload, so
structured requests are self-contained. `PhiSilicaChatClient` includes the
system prompt and flattened conversation in the request text when structured
output is requested.

## Tool calling remains prompt based

The public Windows AI API still has no tool or function-calling abstraction.
There is no API for passing tool definitions or receiving a typed tool call.
`PhiSilicaToolCallingClient` therefore remains a sample-level adapter:

```text
User code
  -> FunctionInvokingChatClient
  -> PhiSilicaToolCallingClient
  -> PhiSilicaChatClient
  -> Microsoft.Windows.AI.Text.LanguageModel
```

The adapter:

1. Converts `AIFunction` definitions to prompt instructions.
2. Requests one JSON tool-call description at a time.
3. Converts the response to `FunctionCallContent`.
4. Lets `FunctionInvokingChatClient` invoke the function and continue.

When tools and a final response schema are requested together, the tool
adapter still uses a prompt-defined protocol. The stable structured-output API
cannot describe the required tool-call-or-final-response union because it does
not support `oneOf` or `anyOf`.

## Model identity and hidden capabilities

`LanguageModel` exposes readiness, generation, embeddings, structured output,
and vector-space APIs, but no model name, version, or capability metadata.
`AICapabilities.HasAICapability` is device-level capability detection, not model
identity.

The device tests include probes that ask the model which Phi generation it
believes it is and whether it supports function calling. These responses are
useful for experiments only. A model can reproduce formats learned during
training, but its self-reported identity and capabilities are not authoritative.
Application behavior must be based on the public WinRT API surface.

The underlying Phi model family documents special tool tokens such as
`<|tool_call|>`, but the Windows API does not expose a native typed tool-call
result. Prompt translation is still required even if the model can generate a
tool-call-shaped string.

## Model transition

Microsoft has announced that Phi Silica will be replaced by Aion Instruct:

- Early October 2026: sideloadable package for testing and LoRA training.
- October 2026: rollout to Windows Insider devices.
- November 2026: retail rollout and removal of Phi Silica.

The documentation does not yet state whether Aion Instruct will use the same
`LanguageModel` activation API or expose model identity. Keep the Windows
adapter isolated and avoid relying on model-specific prompt self-identification.

## References

- [Windows App SDK 2.0 release notes](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)
- [LanguageModel.GenerateStructuredJsonResponseAsync](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.generatestructuredjsonresponseasync?view=windows-app-sdk-2.0)
- [Generate structured JSON output with Phi Silica](https://learn.microsoft.com/windows/ai/apis/phi-silica-structured-output)
- [LanguageModel API reference](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel?view=windows-app-sdk-2.0)
- [Get started with Phi Silica](https://learn.microsoft.com/windows/ai/apis/phi-silica)
- [Phi-4-mini-instruct model card](https://huggingface.co/microsoft/Phi-4-mini-instruct)
