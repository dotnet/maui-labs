# Chat provider implementation guide

This guide defines the shared behavior and provider-specific implementation targets for `Microsoft.Maui.Essentials.AI` chat providers.

## How to use this file

Each reviewed topic has four parts:

- **Common/API rule:** what Microsoft.Extensions.AI represents or exposes.
- **Provider patterns:** observed behavior in external providers, with separate bullets when providers or API variants differ.
- **Essentials.AI rule:** the shared behavior required across Essentials.AI providers.
- **Essentials.AI implementation:** the target behavior for Apple; Windows and Android remain **To be determined** until reviewed.

The Guiding principles apply to the whole document. Area headings group related topics; neither repeats the four parts.

## Contents

- [Guiding principles](#guiding-principles)
- [Provider and session](#provider-and-session)
  - [Availability and capabilities](#availability-and-capabilities)
  - [Model identity](#model-identity)
  - [Conversation state and context](#conversation-state-and-context)
- [Message mapping](#message-mapping)
  - [System instructions](#system-instructions)
  - [Blank instructions and messages](#blank-instructions-and-messages)
  - [Unsupported roles](#unsupported-roles)
  - [Content and capability support](#content-and-capability-support)
- [Request features](#request-features)
  - [Generation options](#generation-options)
  - [Tools](#tools)
  - [Structured output](#structured-output)
- [Response behavior](#response-behavior)
  - [Response metadata](#response-metadata)
  - [Streaming](#streaming)
  - [Token usage](#token-usage)
  - [Telemetry and timing](#telemetry-and-timing)
- [Lifecycle and reliability](#lifecycle-and-reliability)
  - [Cancellation](#cancellation)
  - [Errors](#errors)
  - [Ownership](#ownership)

## Guiding principles

Implement the standard `IChatClient` behavior when the native API can represent it.

When the native API cannot represent a feature, the provider must choose one of these explicit outcomes:

- preserve the supported parts and omit only unrecognized optional content;
- reject the request with a clear exception when the requested behavior cannot be honored;
- expose the feature only on the OS/model versions that support it.

Never silently switch to a cloud provider or claim that an unsupported feature was applied.

- Base provider tests follow the OpenAI behavior where applicable.
- A platform deviation overrides the relevant base test and includes a comment explaining why the native API cannot match it.

## Provider and session

### Availability and capabilities

**Common/API rule**

- `IChatClient` does not define a readiness or capability-discovery API.
- Provider-specific helpers can be exposed through the concrete client or `GetService`.

**Provider patterns**

- **OpenAI:** the root SDK has a separate model client for listing and retrieving model metadata, but concrete chat/response clients do not expose readiness or detailed capability discovery; requests remain authoritative.
- **Google Gemini:** the MEAI adapter exposes the SDK's models helper through `GetService`; it can list/get model metadata such as versions, token limits, supported actions, sampling defaults, and thinking support, but does not guarantee request readiness.
- **AWS Bedrock:** a separate control-plane API reports authorization, entitlement, agreement, and regional availability; the MEAI adapter wraps only the runtime client, so callers need the control-plane client separately.

**Essentials.AI rule**

- Expose provider-specific helpers for native availability and capabilities when the platform supplies them.
- Return a shared status shape containing availability, an optional provider-native reason, and effective MEAI-facing capabilities.
- `ChatModelStatus` is a sealed immutable class with reference identity; compare its properties explicitly when comparing snapshots.
- Map native states to the most appropriate shared availability value without losing the provider reason.
- Treat reported capabilities as immutable, best-effort guidance; generation remains authoritative.
- Refresh status rather than caching it.
- Generation must recheck availability and fail clearly when the model is not ready; it must never initiate model setup or download.
- Model preparation, where supported by Windows or a future Android provider, must be an explicit awaited provider API, separate from generation.

**Essentials.AI implementation**

- **Apple:** expose current status and effective capabilities, with no preparation API because model assets are OS-managed.
- **Windows:** To be determined.
- **Android:** To be determined.

### Model identity

**Common/API rule**

- `ChatClientMetadata` identifies the provider and configured default model when known.
- `ChatOptions.ModelId` may request a different model for one call.
- Responses and streaming updates can identify the model that executed the request.

**Provider patterns**

- **OpenAI:** metadata reports the configured default, while responses and updates report the model returned by the service.
- **Google Gemini:** metadata reports the configured default; responses use the returned model version when available, otherwise the requested/default model.
- **AWS Bedrock:** metadata reports the configured default and requests can override it; responses currently do not report a model ID.

**Essentials.AI rule**

- Report a truthful provider and configured/default model identifier without guessing a runtime-resolved value.
- Use the native executed model identifier in responses and updates when available; otherwise use a documented stable identifier or `null`.
- A fixed-model provider may ignore `ChatOptions.ModelId`.

**Essentials.AI implementation**

- **Apple:** report provider `apple` and stable model ID `apple-intelligence` in metadata, responses, and updates; ignore model overrides.
- **Windows:** To be determined.
- **Android:** To be determined.

### Conversation state and context

**Common/API rule**

- Chat history is supplied as messages for each request unless a provider supports `ConversationId` or continuation tokens.
- Native context limits may reject an otherwise valid conversation.

**Provider patterns**

- **OpenAI Responses:** supports server-side conversation state, previous responses, background operations, and continuation tokens.
- **OpenAI Chat Completions:** uses caller-supplied message history.
- **Google Gemini:** uses caller-supplied history; automatic continuation completes a long response rather than storing the conversation.
- **AWS Bedrock:** uses caller-supplied message history.

**Essentials.AI rule**

- Use caller-supplied history when the native provider has no durable conversation state.
- Reject unsupported non-null conversation IDs and continuation tokens, and explicit background requests; preserve null/default foreground behavior.
- Never silently truncate history; report context-window failures clearly.

**Essentials.AI implementation**

- **Apple:** create a fresh native session from the supplied transcript for each request; reject conversation IDs, continuation tokens, and explicit background requests; report context overflow.
- **Windows:** To be determined.
- **Android:** To be determined.

## Message mapping

### System instructions

**Common/API rule**

- Microsoft.Extensions.AI defines `System`, `User`, `Assistant`, and `Tool` as its standard roles.
- `ChatOptions.Instructions` and System messages both carry instructions; the abstraction does not require one native representation.

**Provider patterns**

- **OpenAI:** passes System messages as System input items in conversation order; they are not converted to User prompts. A conversation may end with System and still generate the next Assistant response.
- **Google Gemini:** moves options and System messages into `SystemInstruction`, leaving User and Assistant messages in conversation history.
- **AWS Bedrock:** moves options and System messages into the top-level `System` blocks, leaving User and Assistant messages in conversation history.

**Essentials.AI rule**

- Process `ChatOptions.Instructions` first.
- When native generation requires User/Assistant prompt content, reject instructions/System-only input before generation; never synthesize a placeholder. Preserve explicitly supplied blank User turns.
- Preserve System-message order and repeated instructions.
- If the native API supports ordered instruction entries, preserve the System messages in conversation order.
- If the native API has one instruction slot, combine options first and then System messages in encounter order, separate the parts with a blank line, and remove those messages from conversation history.
- Preserve User and Assistant message order.

**Essentials.AI implementation**

- **Apple:** prepend options and preserve System messages as ordered transcript instructions. Reject instructions-only input and any trailing non-User message before native generation; the current prompt must be User.
- **Windows:** To be determined.
- **Android:** To be determined.

### Blank instructions and messages

**Common/API rule**

- Blank instructions and blank conversation messages are separate cases.
- Blank instruction handling follows the native representation.

**Provider patterns**

- **OpenAI Responses (preferred):** preserves blank options, omits blank System messages, and preserves blank User and Assistant turns.
- **OpenAI Chat Completions:** omits blank options, preserves blank inline System messages, and preserves blank User and Assistant turns.
- **Google Gemini:** preserves blank options, omits blank System messages, and preserves blank User and Assistant turns.
- **AWS Bedrock:** sends blank instruction blocks even though the API requires system text to contain at least one character, and uses native-safe placeholder text for blank User and Assistant turns.

**Essentials.AI rule**

- Omit blank `ChatOptions.Instructions`.
- Omit blank System messages when hoisting or combining instructions.
- Preserve blank inline System messages unless the native API rejects them.
- Preserve blank User messages as empty conversation turns.
- Preserve blank Assistant messages as empty conversation turns.

**Essentials.AI implementation**

- **Apple:** omit blank options; preserve blank inline System messages and blank User and Assistant turns.
- **Windows:** To be determined.
- **Android:** To be determined.

### Unsupported roles

**Common/API rule**

- `ChatRole` supports custom values in addition to the standard System, User, Assistant, and Tool roles.

**Provider patterns**

- **OpenAI:** ignores unknown roles.
- **Google Gemini:** maps unknown/custom roles to User.
- **AWS Bedrock:** maps unknown/custom roles to User.

**Essentials.AI rule**

- Ignore unknown roles; never reinterpret them as User.

**Essentials.AI implementation**

- **Apple:** ignores unknown roles.
- **Windows:** To be determined.
- **Android:** To be determined.

### Content and capability support

**Common/API rule**

- Messages can contain multiple extensible `AIContent` items.
- Support for recognized content and capabilities may depend on the provider, model, and runtime.

**Provider patterns**

- **OpenAI:** omits unrecognized content, forwards recognized content and requested features, and lets the selected model/API accept or reject them.
- **Google Gemini:** omits unrecognized content, forwards recognized content and requested features, and lets the selected model/API accept or reject them.
- **AWS Bedrock:** omits unrecognized content, forwards Converse-supported content and requested features, and lets the selected model accept or reject them.

**Essentials.AI rule**

- Omit unknown/custom `AIContent` items individually; map standard MEAI content in order.
- If no recognized content remains, apply the blank-message rule for that role.
- Reject recognized content with a clear `NotSupportedException` when its role, media type, representation, or OS/model capability is unsupported; never silently drop it.
- Preserve tool calls and results in history as content, even when native tool registration is unavailable.
- Honor or reject required capabilities such as structured output and Required tool invocation; optional Auto tool registration follows the tool-mode policy below.
- Reject malformed or empty payloads for supported media with `ArgumentException`.
- Enable conditional features only after runtime OS/model capability checks, with no silent cloud fallback.

**Essentials.AI implementation**

- **Apple:** omit unknown/custom content; support text and images in System/User/Assistant, calls in Assistant, and results in Tool. Reject other standard content and media, including PDF/audio and hosted files. Image input requires OS 27+ and model vision support; preserve tool history; apply the Tools and Structured output rules.
- **Windows:** To be determined.
- **Android:** To be determined.

## Request features

### Generation options

**Common/API rule**

- `ChatOptions` exposes optional generation controls; native support depends on the provider and model.
- An unset option leaves the provider's native default in effect.

**Provider patterns**

- **OpenAI Responses:** maps maximum output tokens, temperature, TopP, and reasoning; options without a Responses equivalent are omitted.
- **OpenAI Chat Completions:** maps maximum output tokens, temperature, TopP, penalties, seed, reasoning, and stop sequences; TopK is omitted.
- **Google Gemini:** maps the standard sampling, penalty, seed, stop, token-limit, and reasoning options supported by its request API.
- **AWS Bedrock:** maps maximum output tokens, temperature, TopP, and stop sequences; options without a Converse equivalent are omitted.

**Essentials.AI rule**

- Map options that have a native equivalent and ignore options that do not.
- Preserve native defaults when an option is unset.
- Validate known native ranges and unsafe numeric conversions before passing mapped values through.
- Do not invent a mapping for an unsupported option.

**Essentials.AI implementation**

- **Apple:** map temperature, TopK, TopP, seed, and maximum output tokens on OS 26+. TopK takes precedence over TopP because native sampling modes are exclusive. Validate temperature and TopP from 0 through 1, positive TopK and token limits, and nonnegative seed conversion; ignore other options.
- **Windows:** To be determined.
- **Android:** To be determined.

### Tools

**Common/API rule**

- `ChatOptions.Tools` and `ToolMode` control tools available for a new response.
- `FunctionCallContent` and `FunctionResultContent` represent tool history.

**Provider patterns**

- **OpenAI:** registers tools and maps None, Auto, Required, and named Required; execution is handled outside the provider.
- **Google Gemini:** registers tools and maps None, Auto, Required, and named Required; execution is handled outside the provider.
- **AWS Bedrock:** registers tools and maps Required/named Required; Auto uses the service default and None is not explicitly mapped.

**Essentials.AI rule**

- Preserve tool names, call IDs, arguments, results, and encounter order.
- Preserve tool parameter/output types and required versus optional properties; reject unrepresentable schemas clearly during setup, never silently substitute a generic schema.
- None omits tools. Without native tool registration, Auto definitions may explicitly degrade to no tools because invocation is optional; Required and named Required must be honored or rejected.
- Required and named Required must be honored or rejected; never downgrade them to Auto.
- A provider may execute tools natively or return calls for middleware execution, but each call must execute exactly once with request cancellation and services.

**Essentials.AI implementation**

- **Apple:** on OS 26, use native default behavior for Auto, omit tools for None, and reject Required/named Required. On OS 27+, map Auto, None, and Required to native tool-calling modes; named Required exposes only the named tool. Apple executes tools natively.
- **Windows:** To be determined.
- **Android:** To be determined.

### Structured output

**Common/API rule**

- `ChatOptions.ResponseFormat` requests text, JSON, or a JSON schema.
- Typed `GetResponseAsync<T>` can use native schema support or a caller-selected prompt fallback.

**Provider patterns**

- **OpenAI:** supports native JSON and JSON-schema response formats; the selected model/API rejects unavailable formats.
- **Google Gemini:** supports native JSON mode and optional JSON schema; the selected model/API rejects unavailable formats.
- **AWS Bedrock:** uses native structured output on supported models or an explicit synthetic-tool strategy; unsupported modes return errors.

**Essentials.AI rule**

- Honor a supplied JSON schema natively or reject it; never silently downgrade it to prompt instructions.
- Map schema constraints with native equivalents; reject unrepresentable constraints rather than silently drop them.
- Support JSON only with an explicit schema. Reject schema-free `ChatResponseFormat.Json` and `GetResponseAsync<T>(useJsonSchemaResponseFormat: false)` before generation; never prompt-strengthen, strip fences, repair output, or parse ordinary text into JSON.
- Reject unsupported schema shapes clearly.

**Essentials.AI implementation**

- **Apple:** use built-in schema-constrained generation.
- **Windows:** To be determined.
- **Android:** To be determined.

## Response behavior

### Response metadata

**Common/API rule**

- Responses and updates can carry response/message IDs, timestamps, finish reasons, raw native objects, and provider-specific additional properties.

**Provider patterns**

- **OpenAI:** maps service-supplied IDs, timestamps, finish reasons, raw responses, and additional service metadata.
- **Google Gemini:** maps service-supplied response IDs, timestamps, finish reasons, and raw responses.
- **AWS Bedrock:** maps native finish/additional data and generates request-scoped response/message IDs and timestamps.

**Essentials.AI rule**

- Map native metadata when available.
- Leave unavailable metadata `null`; do not invent IDs, timestamps, finish reasons, or provider fields.

**Essentials.AI implementation**

- **Apple:** map native metadata only when Foundation Models supplies it; otherwise leave it `null`.
- **Windows:** To be determined.
- **Android:** To be determined.

### Streaming

**Common/API rule**

- `GetStreamingResponseAsync` returns ordered `ChatResponseUpdate` items that can carry content and response metadata.

**Provider patterns**

- **OpenAI:** streams incremental text, tool, and metadata updates.
- **Google Gemini:** streams incremental content chunks and cumulative token usage metadata.
- **AWS Bedrock:** streams incremental text and tool deltas plus metadata events.

**Essentials.AI rule**

- Expose new append-only content, not cumulative snapshots.
- Preserve order across text, tool calls, tool results, and structured output.
- Suppress empty updates unless they carry meaningful metadata.
- Buffer until stable or fail if a native producer revises already emitted content.
- Reconcile native final content with emitted progress: emit nothing if equal, only the missing suffix if extended, and fail explicitly if divergent.

**Essentials.AI implementation**

- **Apple:** convert cumulative plain-text snapshots into deltas in the native bridge; forward native structured deltas; preserve text/tool ordering.
- **Windows:** To be determined.
- **Android:** To be determined.

### Token usage

**Common/API rule**

- Non-streaming responses report token usage metadata through `ChatResponse.Usage`.
- Streaming responses report token usage metadata as `UsageContent` inside a `ChatResponseUpdate`.
- `UsageDetails` can contain input, output, total, cached, reasoning, text/audio, and provider-specific additional counts; every count is optional.

**Provider patterns**

- **OpenAI:** `ChatTokenUsage` (Chat Completions) and `ResponseTokenUsage` (Responses) report core token counts and may include cached, reasoning, modality-specific, and provider-specific counts.
- **Google Gemini:** `UsageMetadata` reports cumulative token usage metadata in streaming chunks; its adapter emits only the final value.
- **AWS Bedrock:** `TokenUsage` reports token usage metadata in stream metadata and completed responses.

**Essentials.AI rule**

- Map every native count to the closest standard field and preserve other summable counts in `AdditionalCounts`.
- Emit streamed token usage metadata once at completion.
- Leave unavailable counts `null`; never manufacture zeroes or estimates.

**Essentials.AI implementation**

- **Apple:** on OS 27+, map native input, output, total, cached, and reasoning counts when available; report no token usage metadata on earlier OS versions.
- **Windows:** To be determined.
- **Android:** To be determined.

### Telemetry and timing

**Common/API rule**

- Timing is measured by the optional MEAI OpenTelemetry wrapper, not stored in chat responses.
- The wrapper measures total operation duration, time to the first emitted streaming update, and time between later emitted updates.
- An emitted update is not necessarily a token; it may contain text, tools, metadata, or token usage metadata.

**Provider patterns**

- **OpenAI:** supplies response metadata and token usage metadata when available; client-observed timing comes from the shared MEAI wrapper.
- **Google Gemini:** supplies response metadata and token usage metadata when available; client-observed timing comes from the shared MEAI wrapper.
- **AWS Bedrock:** supplies response metadata and token usage metadata when available; client-observed timing comes from the shared MEAI wrapper.

**Essentials.AI rule**

- Do not invent provider timing fields or add local timings to response metadata.
- Remain compatible with shared MEAI operation, first-update, inter-update, token usage, and model telemetry.
- Preserve the request activity/trace context when native callbacks invoke managed tools or logging.
- Keep prompt, response, tool arguments, and tool results out of telemetry unless sensitive-data capture is explicitly enabled.

**Essentials.AI implementation**

- **Apple:** use shared MEAI timing and telemetry; capture and restore the managed execution context for native tool callbacks so tool execution and logs remain correlated with the chat activity.
- **Windows:** To be determined.
- **Android:** To be determined.

## Lifecycle and reliability

### Cancellation

**Common/API rule**

- Chat requests accept a `CancellationToken`, and streaming enumeration can end early.

**Provider patterns**

- **OpenAI:** forwards request and stream cancellation to the service.
- **Google Gemini:** forwards request and stream cancellation to the SDK.
- **AWS Bedrock:** forwards request and stream cancellation to the SDK.

**Essentials.AI rule**

- Do not start generation for a pre-cancelled request.
- Forward in-flight cancellation and early stream disposal to native generation.
- Keep callback and request resources alive until native work terminates.

**Essentials.AI implementation**

- **Apple:** cancel the Swift task through the native cancellation token and retain callback resources until terminal completion.
- **Windows:** To be determined.
- **Android:** To be determined.

### Errors

**Common/API rule**

- Chat operations report request, provider, and generation failures through exceptions.

**Provider patterns**

- **OpenAI:** surfaces service errors with HTTP status and provider diagnostics.
- **Google Gemini:** surfaces SDK/model validation and generation errors.
- **AWS Bedrock:** surfaces service validation and generation exceptions.

**Essentials.AI rule**

- Reject invalid requests before native generation when the provider can validate them.
- Preserve native failure details in a clear managed exception.
- Fault a streaming operation rather than returning a successful partial completion.
- Treat null or malformed native success results as errors, not successful empty responses.

**Essentials.AI implementation**

- **Apple:** translate native Foundation Models errors to managed exceptions with their descriptions; reject null or empty native success results.
- **Windows:** To be determined.
- **Android:** To be determined.

### Ownership

**Common/API rule**

- `IChatClient` is disposable, but supplying an underlying client, model, or service does not implicitly transfer ownership.

**Provider patterns**

- **OpenAI:** adapters do not dispose caller-supplied OpenAI clients.
- **Google Gemini:** the adapter does not dispose caller-supplied Gemini clients.
- **AWS Bedrock:** the adapter does not dispose the caller-supplied Bedrock runtime client.

**Essentials.AI rule**

- Dispose retained models and clients only when the provider created and owns them.
- Never dispose caller-supplied models or services.
- Keep per-request resources alive through native termination, then dispose them deterministically.

**Essentials.AI implementation**

- **Apple:** retain no caller-supplied model; create and release native request/session resources per operation; client disposal is a no-op.
- **Windows:** To be determined.
- **Android:** To be determined.
