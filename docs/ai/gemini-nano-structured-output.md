# Gemini Nano structured output

`GeminiNanoChatClient` supports explicit JSON-schema responses using ML Kit's
native response schema, which AICore enforces with constrained decoding.
No Kotlin classes or KSP setup are needed in the consuming app.

> [!WARNING]
> ML Kit beta5 only exposes structured output through Kotlin classes
> (`@Generable`). This client passes runtime JSON schemas through ML Kit SDK
> internals instead, so the feature is experimental and tied to the bundled
> `genai-prompt` 1.0.0-beta5 runtime. An incompatible runtime fails with an
> explicit error rather than generating unconstrained text.

Model readiness and `ChatModelCapability.StructuredOutput` are reported by
`GetModelStatusAsync`; generation rechecks the native capability and never
downloads or falls back.

## Use from Microsoft.Extensions.AI

Use the standard typed-response extension with source-generated serialization
metadata in trimmed/AOT apps:

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

Alternatively, set `ChatOptions.ResponseFormat` to
`ChatResponseFormat.ForJsonSchema<T>()` or a runtime schema. The client uses
MEAI's schema transformer to require every declared property and disallow
additional properties, consistent with the other local providers. Defaults
move into descriptions rather than being interpreted as output guarantees.
The original format and caller options are not mutated.

Schema-free `ChatResponseFormat.Json` and typed prompt fallback
(`useJsonSchemaResponseFormat: false`) throw `NotSupportedException` before
inference. Unsupported schema keywords and combinations also fail explicitly.

Structured requests default to 512 output tokens when `MaxOutputTokens` is
unset. Explicit limits are preserved. If the limit truncates the JSON, the
request fails with a message saying so rather than retrying, repairing, or
returning a partial object.

## How it works

ML Kit's typed API (`generateTypedContentRequest(request, Output::class)`) does
three things, all of which this client reproduces from a JSON schema:

1. Derives an internal native schema object from the Kotlin class. Here it is
   built directly from the JSON schema, so there is no fixed set of classes and
   no limit on nesting depth or object count.
2. Appends ML Kit's own schema prompt (the typed API's default
   `includeSchemaInPrompt = true`) to the last text part of the request.
3. Places the native schema in the AICore request, where AICore constrains
   decoding to the schema. ML Kit only sets that field when the request has a
   Kotlin class, so the native bridge uses a processor equivalent to ML Kit's
   typed-output processor and sets the field from the JSON schema.

AICore returns ordinary text. The bridge validates the complete text as strict
RFC 8259 JSON against the original schema before the response completes.
Validation failures surface as errors; output is never repaired.

## Streaming and tools

`GetStreamingResponseAsync` streams JSON text deltas as AICore generates them,
followed by normal finish/model/usage metadata. The complete text is validated
when generation ends, so a stream can end with an error after partial JSON.
Concatenated updates always equal the validated final text.

Active native tools and full thinking output cannot be combined with structured
output: the request fails before inference. Disabled tools (`ChatToolMode.None`)
and replayed caller history remain supported.

In Chat Playground, select a ready Gemini Nano model and enable **Structured JSON**.
The existing playground response schema is used. Tool controls are temporarily
disabled for this combination without discarding their selections. With
Streaming enabled, JSON appears incrementally.

## Supported schemas

The root must be a non-null object: AICore does not constrain array or scalar
roots. `GetResponseAsync<T>` already wraps other result types (for example the
`string[]` and `string[][]` used by Microsoft.Extensions.DataIngestion enrichers)
in a required `data` property, so those work unchanged.

| Type | Keywords |
|---|---|
| Any | `type` (one type, optionally with `"null"`), `description`, `title`, local `$ref`, `$defs`/`definitions`, `$schema` |
| object | `properties` (at least one), `required`, `additionalProperties` (boolean) |
| array | `items` (any supported schema, including nested arrays and nullable items), `minItems`, `maxItems` |
| string | `enum`, `format`, `pattern` |
| integer, number | `minimum`, `maximum` |
| boolean | — |

Objects, arrays and references can nest to any depth. Local references are
resolved; recursive and external references are rejected. Every other keyword
(for example `anyOf`, `const`, `minLength`, `exclusiveMinimum`, `multipleOf`),
multi-type unions, non-string enums, and dictionary schemas (schema-valued
`additionalProperties`) are rejected before inference rather than ignored.
`format` is passed to AICore but is not validated by the bridge.

## Native implementation

The native code is in `src/AI/AndroidNative/EssentialsAI/src/main/kotlin/`:

- `NativeResponseSchema` parses the JSON schema, builds ML Kit's native schema
  object, and validates generated text.
- `NativeStructuredOutput` holds the processor, the schema prompt, and the AICore
  request field update. It is the only code that depends on ML Kit internals.

Internal ML Kit names are obfuscated and change between SDK releases. The
runtime is pinned and bundled, a JVM test asserts the AICore request contract
against the real runtime, and a consumer R8 rule keeps the schema field. Each
SDK update must re-verify these internals before shipping.

Build and run JVM tests normally, with Java 17+ and the Android SDK:

```sh
cd src/AI/AndroidNative/EssentialsAI
./gradlew --no-daemon testReleaseUnitTest assembleRelease exportMlKitRuntime
```

Managed integration coverage lives in the existing Android device-test assembly.
It binds the shared schema suite and adds interop, capability, history,
collection, deep-schema, streaming, concurrency, cancellation, and error cases.
Real model tests use `RequiresModel=true`; model-free tests validate schema
rejection without inference. Physical hardware and a foreground app are required
to verify inference.
