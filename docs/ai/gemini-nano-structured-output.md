# Gemini Nano structured output

`GeminiNanoChatClient` supports JSON-schema responses through ML Kit's native
response schema, which AICore enforces with constrained decoding. Apps don't
need Kotlin classes or KSP.

ML Kit's public API only accepts structured output as Kotlin `@Generable`
classes. The client passes runtime JSON schemas through ML Kit internals
instead, so it depends on the bundled `genai-prompt` 1.0.0-beta5 runtime. An
incompatible runtime fails with an explicit error.

`GetModelStatusAsync` reports `ChatModelCapability.StructuredOutput` when the
selected model supports it. Generation checks the capability again and never
downloads a model.

## Use from Microsoft.Extensions.AI

Use the typed-response extension. In trimmed or AOT apps, pass source-generated
serializer options:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using System.Text.Json.Serialization;

using var client = new GeminiNanoChatClient();
var response = await client.GetResponseAsync<WeatherResponse>(
    "Return location Seattle, temperature 72, and condition sunny.",
    AppJsonContext.Default.Options);
Console.WriteLine(response.Result.Location);

public sealed record WeatherResponse(string Location, int Temperature, string Condition);

[JsonSerializable(typeof(WeatherResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
```

You can also set `ChatOptions.ResponseFormat` to
`ChatResponseFormat.ForJsonSchema<T>()` or a runtime schema. The client applies
MEAI's schema transform, like the other local providers: every declared property
becomes required, additional properties are disallowed, and `default` values
move into descriptions. The caller's format and options are not changed.

Schema-free `ChatResponseFormat.Json`, typed prompt fallback
(`useJsonSchemaResponseFormat: false`) and schemas ML Kit can't represent throw
`NotSupportedException` before inference.

Structured requests default to 512 output tokens when `MaxOutputTokens` is
unset. If the limit cuts the JSON short, the request fails and says so.

## How it works

ML Kit's typed API, `generateTypedContentRequest(request, Output::class)`, does
three things. The client does the same from a JSON schema:

1. Builds ML Kit's internal schema object. ML Kit derives it from the Kotlin
   class; the client builds it from the JSON schema.
2. Appends ML Kit's schema prompt to the last text part of the request (the
   typed API's `includeSchemaInPrompt = true` default).
3. Sets the schema on the AICore request, which constrains decoding. ML Kit only
   sets this field for a Kotlin class, so the bridge uses its own copy of ML Kit's
   typed-output processor that sets it from the JSON schema.

AICore returns text. The bridge parses it as strict JSON (RFC 8259) and checks
it against the schema before the response completes. Output that doesn't match
fails the request; it is never repaired.

## Streaming

`GetStreamingResponseAsync` streams JSON text as AICore generates it, followed
by the finish reason, model ID and usage. The complete text is validated at the
end, so a stream can fail after partial JSON. The concatenated updates always
equal the validated text. Non-object roots arrive as one update after
validation.

## Thinking and tools

Full thinking output works with a schema in one request: thoughts stream as
reasoning content and the answer is constrained JSON.

Tools also work with a schema, including through `UseFunctionInvocation` and
`GetResponseAsync<T>`. AICore never emits a function call while a schema
constrains decoding, and ML Kit's typed result has no function calls. On Nano V3
and V4 the model invented tool results instead of calling the tool. So with
tools, each model turn runs in two steps:

1. The request runs with the tools and without the schema. If the model calls
   tools, the calls are returned without text, and function invocation runs them
   and sends the results back.
2. If the model answers instead, that text is discarded and the answer is
   generated again under the schema, with the same history and tools.

The final turn therefore costs one extra inference (about 0.4–1 s on Nano V4
fast), and usage includes the input tokens of both steps. Step 1 always runs to
completion, because the model can write text before a call. If it hits the
output token limit without a call, the request fails rather than answering
without the tool result.

Reasoning that function invocation sends back in assistant history is dropped,
because ML Kit has no input for earlier thoughts.

In Chat Playground, select a ready Gemini Nano model and turn on **Structured
JSON**. The playground's response schema is used together with any selected
tools.

## Supported schemas

The root can be any non-null type. AICore returns array, string, number,
integer and boolean roots as `{"wrapper": value}`; the bridge checks the wrapper
and returns the bare value. `GetResponseAsync<T>` already wraps non-object types
(such as the `string[]` and `string[][]` used by Microsoft.Extensions.DataIngestion
enrichers) in a required `data` property.

| Type | Keywords |
|---|---|
| Any | `type` (one type, optionally with `"null"`), `description`, `title`, local `$ref`, `$defs`/`definitions`, `$schema`, `$comment` |
| object | `properties` (at least one), `required`, `additionalProperties` (boolean) |
| array | `items`, `minItems`, `maxItems` |
| string | `enum`, `format`, `pattern` |
| integer, number | `minimum`, `maximum` |
| boolean | — |

ML Kit's schema has no field for other keywords, so the client rejects them
instead of ignoring them. That includes `anyOf`, `const`, `minLength`,
`maxLength`, `exclusiveMinimum` and `multipleOf`, multi-type unions, non-string
enums, and dictionaries (schema-valued `additionalProperties`). Other limits:

- Schemas nest up to 64 levels, matching System.Text.Json's default maximum depth.
- Local references are resolved; recursive and external references are rejected.
- `minimum`/`maximum` must be exactly representable as a `double`, because ML Kit
  stores bounds as doubles.

AICore enforces types, `enum`, `required`, `minimum`/`maximum` and
`minItems`/`maxItems` while decoding. It doesn't enforce `pattern`, so the bridge
rejects output that doesn't match. `format` is sent to AICore but not validated.

## Native implementation

The Kotlin code is in `src/AI/AndroidNative/EssentialsAI/src/main/kotlin/`:

- `NativeResponseSchema` parses the JSON schema, builds ML Kit's schema object and
  validates generated text.
- `NativeStructuredOutput` holds the processor, the schema prompt and the AICore
  request field. It is the only code that uses ML Kit internals.

ML Kit's internal names are obfuscated and change between releases. The runtime
is pinned and bundled, a JVM test checks the AICore request field against the
real runtime, and a consumer R8 rule keeps that field. Recheck these internals
when updating the SDK.

Run the JVM tests with Java 17+ and the Android SDK:

```sh
cd src/AI/AndroidNative/EssentialsAI
./gradlew --no-daemon testReleaseUnitTest assembleRelease exportMlKitRuntime
```

Device tests are in the Android device-test assembly. Tests that need a model
are marked `RequiresModel=true` and need physical hardware with the app in the
foreground; the rest check schema handling without inference.
