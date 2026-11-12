# Gemini Nano structured output

`GeminiNanoChatClient` supports explicit JSON-schema responses through the
ML Kit beta5 typed generation API. No Kotlin classes or KSP setup are needed
in the consuming app. Model readiness and
`ChatModelCapability.StructuredOutput` are reported by `GetModelStatusAsync`;
generation rechecks the native capability and never downloads or falls back.

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
unset. Explicit limits are preserved. If the limit prevents complete valid
JSON, the native error is surfaced rather than retrying, repairing, or
returning a partial object.

## Streaming and tools

Google exposes no typed streaming overload. `GetStreamingResponseAsync` therefore
delivers one complete, validated JSON text update after inference and native
cleanup, followed by normal finish/model/usage metadata. It does not fabricate
incremental tokens. Text-only requests retain native streaming.

Active native tools and full thinking output cannot be combined with structured
output in this adapter: the typed response does not expose function calls or
thought-process content. Requests for those combinations fail before inference.
Disabled tools (`ChatToolMode.None`) and replayed caller history remain supported.

In Chat Playground, select a ready Gemini Nano model and enable **Structured JSON**.
The existing playground response schema is used. Tool controls are temporarily
disabled for this combination without discarding their selections. JSON works
with either Streaming setting; with Streaming enabled it appears at completion.

## Supported schemas

The root must be a non-null, nonempty object. Supported property types are strings,
string enums, booleans, integers, finite IEEE-754 numbers, nested objects, arrays,
and a single type combined with null. Acyclic local JSON pointers are resolved.
Numeric minimum/maximum and array minItems/maxItems constraints are mapped and
checked again when projecting the parsed result.

The adapter supports up to 16 expanded object schema nodes per request.
Recursive/external references, multi-type unions, nested arrays, nullable array
items, numeric array-item bounds, and unrepresented keywords such as pattern,
format, and multipleOf are rejected. This is a native schema subset, not an
implementation of every JSON Schema keyword.

The SDK deserializes generic-map numbers as `Double`. Integer values and explicit
integer bounds are limited to -2^53+1 through 2^53-1 and checked for integerness
before projection to JSON integers. Arbitrary-precision decimal/Int64 semantics
outside those limits are not supported; use strings for exact larger values.

## Native implementation and packaging

The native code is in `src/AI/AndroidNative/EssentialsAI/src/main/kotlin/`.
`NativeResponseSchema` translates runtime schema metadata into Google's public
`GenerableDetail`; `NativeResponseProvider` implementations supply it through
the documented Java `ServiceLoader` interface. Map-backed carriers contain no
`@Generable` annotations or schema-specific fields. The typed request carries
the native response schema; there are no private SDK hooks or handwritten JSON
prompt fallbacks.

Google caches provider instances and queries metadata before and after suspension.
A coroutine mutex keeps one immutable schema active through model creation,
token counting, inference, SDK parsing, projection, and cleanup. Completion,
failure, and cancellation release that state. Different structured requests
queue, while ordinary text requests retain their existing execution path.

The provider registration resource must survive APK packaging. The NuGet's
`buildTransitive` target preserves the exact `META-INF/services/...GenerableProvider`
file and supplies it as a regular archive input. This bypasses older Android
SDK archive tasks that delete unchanged JAR entries during incremental packaging.
The target also passes consumer keep rules explicitly to R8: without them,
reflection-only carrier constructors can be removed and the classes made abstract.
Repository project-reference apps import the same target; external source
consumers should do so as well. NuGet consumers require no extra setup.

Build and run JVM tests normally, with Java 17+ and the Android SDK:

```sh
cd src/AI/AndroidNative/EssentialsAI
./gradlew --no-daemon testReleaseUnitTest assembleRelease exportMlKitRuntime
```

Managed integration coverage lives in the existing Android device-test assembly.
It binds the shared schema suite and adds interop, capability, history,
concurrency, cancellation, and error cases. Real model tests use `RequiresModel=true`;
model-free tests validate schema rejection without inference. Physical hardware
and a foreground app are required to verify inference and packaging.
