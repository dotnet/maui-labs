# Canonical Apple JSON Schema profile

`AppleIntelligenceChatClient` compiles the original
`ChatResponseFormatJson.Schema` or `AIFunction.JsonSchema` into the same
provider-specific JSON Schema subset before calling Swift. This is JSON Schema,
not a separate intermediate language. The managed compiler owns schema semantics;
Swift maps the canonical profile to Foundation Models `DynamicGenerationSchema`,
`GenerationSchema`, and `GenerationGuide`.

This profile is internal to `Microsoft.Maui.Essentials.AI`. It does not define
Windows or Android behavior or change the shared
[local chat provider contract](local-chat-provider.md).

## Sources and presence

Response schemas require an explicit schema. The response compiler deliberately
requires every named property, recursively, preserving the provider's existing
strict response policy. Tool parameter schemas retain each parent's `required`
list, including optional CLR parameters. Tool return schemas do not drive
generation and are not compiled.

Nullable is not optional: a nullable value is an `anyOf` containing a null schema;
only the containing object's `required` list controls omission. For example,
MEAI's schema for a required nullable integer and an optional nullable string:

```json
{
  "type": "object",
  "properties": {
    "count": { "type": ["integer", "null"] },
    "label": { "type": ["string", "null"], "default": null }
  },
  "required": ["count"]
}
```

compiles in tool mode to:

```json
{
  "type": "object",
  "title": "AppleSchema0",
  "properties": {
    "count": {
      "title": "AppleSchema1",
      "anyOf": [{ "type": "integer" }, { "type": "null" }]
    },
    "label": {
      "title": "AppleSchema2",
      "anyOf": [{ "type": "string" }, { "type": "null" }]
    }
  },
  "required": ["count"],
  "additionalProperties": false
}
```

Response mode changes `required` to `["count", "label"]`, not either value
schema. Swift creates named `anyOf` native schemas containing `Int`/`String` and
`.null`, and sets each native property's `isOptional` from `required`.

## Canonical shape and identities

Each node has either a single string `type` or an `anyOf` of complete canonical
nodes, never both. Type arrays become `anyOf`; nullable enum/const values become
string and null branches. Unions have no assertion siblings. Single surviving
branches collapse. Type-specific constraints apply only to matching branches.
Impossible branches may be removed; unsupported constraints are errors even in
otherwise impossible branches. An impossible required property makes its object
impossible; an impossible optional property can be omitted from a closed object.

Object properties and `required` names use ordinal ordering. Objects and unions
receive unique titles `AppleSchema0`, `AppleSchema1`, etc., in canonical preorder.
Input titles are not native identities: duplicate, adversarial, or missing input
titles cannot alias native definitions. The same source and mode always produce
the same profile, including after cache eviction. Descriptions remain descriptive
metadata, not identity. A cohesive compiler/cache serves both schema sources;
response and tool modes are distinct cache entries.

`AIJsonSchemaTransformCache` already provides weak caching and common
transformations, including a custom-node callback. The Apple compiler is not a
replacement for those general facilities: its added responsibilities are the
documented profile, whole-schema semantic validation, and completed-value checks.
It reads the original `JsonElement` directly and uses a small bounded
source-and-mode memoization cache; the cache itself adds no schema capability.

Named object schemas emit `properties`, `required`, and
`additionalProperties: false` explicitly. Omitted `additionalProperties` on a
named object is deliberately closed by provider policy. Explicit `true` or
schema-valued `additionalProperties`, dictionaries, and objects without a named
shape are rejected, not changed into empty objects or strings. An explicitly
closed empty object is supported; `{}` at the tool parameter root is the
no-argument-tool convention and becomes a closed empty object. Unconstrained
boolean schemas and unconstrained nested `{}` are not representable.

## Annotations, references, and compositions

Keep `description` where consumed by native objects, unions, or properties.
Remove `$schema`, `$id`, `$comment`, `title`, `default`, `examples`, and `format`
from the input before emitting the profile (generated identity titles replace
input titles). In particular, `format: "uuid"` and `format: "date-time"` from
`Guid` and `DateTimeOffset` remain plain string schemas; the provider does not
claim native format validation or replace those formats with regexes. Defaults
do not make a property required and are not injected into descriptions.

Reject every `$ref` and dynamic/recursive reference, whether local, external, or
cyclic, with instructions to inline the referenced schema. Unused `$defs` and
`definitions` can be discarded because they impose no constraint without a
reference. No reference expansion occurs in either language.

Support only simple `anyOf` with annotation siblings. Reject assertion siblings,
`oneOf`, `allOf`, `not`, conditionals, and dependent schemas. Unknown keywords and
recognized unsupported constraints fail rather than being silently removed.

## Strings and regex limitations

Strings support string `const` and nonempty string `enum`. Null
literals are split into their own canonical null branch; other literal types
are rejected. Intersect enum and const using exact ordinal/scalar values, not
Unicode canonical equivalence. Empty intersections are impossible. Preserve
distinct NFC and NFD choices.

Reject every JSON Schema `pattern` with its source path and instructions to use
string choices or validate in application code. Apple exposes
[`GenerationGuide.pattern`](https://developer.apple.com/documentation/foundationmodels/generationguide/pattern(_:)),
but API existence does not establish an equivalent JSON Schema regex contract.
On the tested system model a bare Swift literal pattern worked, anchored patterns
failed, and Swift scalar-mode patterns all failed with `UnsupportedGuide`.
JSON Schema uses search semantics and an ECMAScript regex dialect; changing
anchors, Unicode semantics, or dialect to make a guide accepted is not a faithful
translation. This provider therefore has no regex parser, regex-subset validator,
or canonical/native pattern mapping. It neither ignores patterns nor substitutes
an unconstrained string.

## Numbers and arrays

Read every numeric bound from the original `JsonElement.GetRawText()` before
any floating-point/decimal conversion. Compare normalized decimal coefficients
and exponents exactly; do not round through `decimal`, `double`, or a JSON node
and then pretend to validate the original token.

For integers, calculate `ceil(minimum)` and `floor(maximum)` exactly, including
negative fractions and arbitrarily precise fractional tokens. Emit integral
Int64 bounds only; reject out-of-range results. Also reject bounds outside
`[-9007199254740991, 9007199254740991]`: the native generated-content transport
rounded larger integers despite encoding their schema bounds exactly.
An empty integral interval is impossible. For numbers, require finite Double values whose shortest round-trip
decimal token has the same exact decimal value as the source token. Equivalent
spellings such as `1.2500e0` are accepted and normalized to `1.25`; high-precision
values that would round and underflow/overflow are rejected. This is decimal
token fidelity, not a claim that binary floating point represents `0.1` exactly.

Swift decodes integer bounds as `Int` and number bounds as `Double`, constructs
native min/max/range guides, then verifies the actual encoded native schema
against the canonical bound. Encoding fidelity does not prove runtime guide
enforcement. Managed canonical-profile validation checks completed response
values and tool arguments before success/invocation, including exact numeric
comparisons and array counts. A generated zero cannot satisfy a positive
`minimum`, even when the native guide allowed it. Streaming may already have
emitted partial progress; a final constraint violation faults the operation,
never returns successful invalid JSON, and does not repair or regenerate it.
The native implementation can impose additional representability limits; those
failures propagate rather than weakening the profile.

Arrays require one complete canonical `items` schema, recursively. `minItems`
and `maxItems` must be exact nonnegative Int64 integers, with
`minItems <= maxItems`. Emit only supplied bounds. Tuple/prefix items,
`uniqueItems`, `contains`, `minContains`, `maxContains`, and unevaluated-item
constraints are unsupported. `exclusiveMinimum`, `exclusiveMaximum`,
`multipleOf`, string lengths, object cardinality, pattern properties, property
names, dependencies, and unevaluated-property constraints are also unsupported.

For example, a nested MEAI property with
`{"type":"array","minItems":1,"items":{"type":"integer","minimum":1.25}}`
becomes the same array with item `minimum: 2`. Swift maps it to an array schema
with `minimumElements: 1` and an `Int` item guide `.minimum(2)`.
A generated `Dictionary<string,string>` instead fails at its
`additionalProperties` path before native setup or tool invocation.

## Errors and native boundary

Managed failures include the source schema path, unsupported keyword/value,
and corrective action. Paths use JSON property/index context (with escaping for
ambiguous property names); tool errors also identify the function. Neither a
bad response schema nor a bad tool parameter schema falls back to String, free
text, prompt strengthening, or an unconstrained schema.

Swift decodes only this canonical shape. Boundary checks may reject malformed
canonical input but must not reimplement input type inference, keyword policy,
enum intersections, regex-subset validation, integer rounding, name allocation,
or arbitrary JSON Schema normalization. Managed output validation consumes only
the canonical profile; it does not create another input-schema interpreter.

Native-only responsibilities are Foundation Models type/guide construction,
explicit-null OS availability (26.4+), native encoding
fidelity verification, and Foundation Models errors. Text/tools otherwise retain
their OS 26 baseline. A model can reject an API-representable guide at runtime;
return that error without retries using weaker constraints.
See [Apple Foundation Models](apple-foundation-models.md).

## Native feature verification

Direct live probes on macOS 27.0.1, Xcode 27, and the available default system
model on 2026-10-20 used native schemas, conflicting prompts, greedy sampling,
and checked generated JSON, not merely encoded schema metadata:

| Feature | Observed native behavior | Provider policy |
|---------|--------------------------|-----------------|
| String constant / enum | Generated the allowed literal/choice despite a conflicting prompt | Map to native constant/anyOf guides |
| Integer minimum / maximum / range | Ordinary bounds enforced; exact safe-integer endpoints also enforced in 3/3 probes each | Exact managed rounding, representability checks, native guides, completed-value validation |
| Large Int64 bounds | `9007199254740993` generated `9007199254740992`; Int64 extrema also lost precision, 3/3 each | Reject unsafe integer bounds; do not advertise full Int64 guide fidelity |
| Double minimum / maximum / range | Ordinary bounds and `0.1...0.2` enforced | Preserve exact source decimal tokens, map representable guides, validate completed values |
| Extreme Double ranges | `1e-200` and `5e-324` generated zero; `1e200` and Double maximum failed natively, 3/3 each | Never treat native schema encoding as runtime proof; surface native errors and reject violating output |
| Array minimum / maximum / exact count | Minima 0 through 4, range 2 through 4, maximum zero/two, and exact count three enforced with the provider's default schema-in-prompt setting, 3/3 each | Map count guides and validate completed counts |
| Array minimum without schema in prompt | A `minItems: 2` probe produced one item | Retain default schema inclusion; do not claim prompt-independent native enforcement |
| Array item constraints / nested objects | Item constants, recursive object types, and closed object shapes enforced | Recursive canonical mapping and output validation |
| Required / optional properties | Required field retained and optional field omitted on request | Parent `required` controls presence independently of nullable values |
| Null / nullable / mixed unions | Null-only, nullable string branches, integer union branch, and distinct object alternatives worked | Explicit canonical `anyOf`; OS 26.4 null gate |
| Regex | Bare `A` worked; `^A$`, anchored classes/quantifiers failed 3/3; scalar-mode patterns also failed | Reject JSON Schema `pattern` before native invocation |
| Descriptions / format / defaults | Descriptions are generation hints; formats/defaults are not native assertions | Keep consumed descriptions, strip annotations, never claim format/default enforcement |

These observations are runtime/version-specific evidence, not a promise that
every model enforces every guide. The compiler, native mapping tests, live
generation checks, and completed-value validation have distinct responsibilities.

## Testing layers

1. Portable C# tests assert exact canonical JSON and exact rejection paths,
   numeric lexical fidelity, both required modes, annotations, generated MEAI
   schemas, unions, names, pattern rejection, cache behavior, and native-output
   violations.
2. Model-free Swift tests compare canonical-profile mappings against schemas
   constructed directly with Foundation Models, including native numeric
   encoding, null availability, and malformed boundaries.
3. Catalyst integration checks the actual MEAI-to-managed-to-native boundary for
   nullable/optional/nested values, `Guid`, `DateTimeOffset`, and dictionary
   rejection. Provider-neutral contracts stay in existing shared test bases.
4. Native live and full Apple/OpenAI Catalyst suites exercise generation and
   regression behavior. Strict PublicAPI Release builds cover portable, iOS,
   Mac Catalyst, and macOS targets without adding public API.
