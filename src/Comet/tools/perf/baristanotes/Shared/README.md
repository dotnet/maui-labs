# Barista comparison fixture contract

This is source-only, unmeasured test support. It is not app domain code or a
benchmark runner. Compile the three `*.cs` files once into each comparison
app. Do not copy or redefine their JSON types inside an app adapter.

The original MAUI + MauiReactor + EF diagnostic has exercised this contract
under Android arm64 NativeAOT. Its first data gate stopped when the second
drink creation returned the first drink's native ID. Neither complete
dataset has passed persisted-data validation. Do not use this result as
comparison or timing acceptance.

## Inputs and adapter

`FixtureValidation.Parse(bytes, count)` accepts only the exact approved v2
100/1,000-record inputs. It checks SHA-256 before deserialization. Generated
`FixtureJson` metadata rejects unknown members and missing constructor
arguments. No JSON reflection switch is needed.

Implement `IFixtureAdapter` using the app's existing domain APIs:

- Each create method returns an opaque native ID as an invariant string.
  Check domain error results before returning.
- Create beans, bags, equipment, people, then drinks in input order.
- `ApplyPreferencesAsync` runs only during new provisioning. Set Celsius,
  Light through the theme service, and Auto for the four shared range
  metrics: `dose`, `grind`, `output`, `time`. Normalize every field in
  `RecentSelections` from the newest drink, including empty accessories
  and null preinfusion.
- `ReadAsync` must open a fresh domain scope and query persisted values.
  Do not return input values, create responses, or tracked write objects.
  Use existing list/by-ID/history APIs. Do not add ORM query expressions
  or write SQL in an adapter.

`FixtureIds` keeps separate logical-key/native-ID maps for each entity type.
An unknown persisted native ID is a mismatch, not a new logical key.
`FixtureReadback` is the actual-data output; it is distinct from the input
definition. It includes actual entities, relationships, counts, settings,
recent selections, rating-zero count, full newest-first history, and the
latest 100 keys. Compare list and by-ID results where both APIs exist.

Activity `All` is a fresh page-state requirement, not a persisted preference.
Check it separately in the app. Audit timestamps, sync IDs and native IDs
are not fixture semantic fields.

## Storage and refusal rules

Select storage before constructing app services or resolving the theme.
`FixtureStore.NamespacePath` accepts only
`fixture-v2-{100|1000}-{1..16 lowercase letters or digits}`. It derives:

```text
<appData>/barista-comparison/<namespace>/barista_notes.db
<appData>/barista-comparison/<namespace>/receipt.json
<appData>/barista-comparison/<namespace>/readback.json
preferences name: barista_comparison_<namespace>
```

The app owns `appData`; callers cannot supply paths. The platform adapter
must also reject orphaned preferences for a supposedly new namespace.
Default app storage must not be redirected or copied into fixture storage.

`new FixtureStore(appData, selection)` reserves a missing namespace only in
explicit `provision` mode. It writes a `provisioning` receipt before domain
initialization. Start `ExecuteAsync` once after normal database initialization.
Every operation is recorded before execution. Returned IDs are retained
after each successful create. Completion requires a fresh-scope readback.

An existing directory without a receipt, an incomplete/failed receipt, or
a mismatched input hash is refused. There is no reset, repair, deletion,
resume or duplicate-producing retry. A crash after a domain write but
before receipt update leaves the pending operation unresolved and must be
investigated, not repeated.

An existing complete namespace uses `ValidateAsync`, with no adapter
create/preference calls and no receipt/readback-file changes. Normal app
startup still uses its existing initialization and theme paths; this is
not a claim that those paths issue no internal statements.

`run` is for later ordinary app loading: no import or full record
validation may run during measured startup. Load fixture definitions and
invoke readback only in an explicit, unmeasured correctness operation.
The current original candidate is a blocked diagnostic prototype, not a
timing implementation; its eager fixture-definition load must be removed
from the `run` path before a timing variant can use it.

## Canonical values

The receipt keeps the input SHA-256 separately from `SemanticSha256`.
`FixtureValidation.Canonical` produces UTF-8 JSON with:

- Object keys sorted ordinally; beans, bags, equipment and people sorted
  by logical key.
- Drinks sorted by UTC timestamp then logical key; accessory keys sorted.
  History arrays retain their actual order so order errors remain visible.
- Decimal values written with invariant `G29`, independent of stored scale.
- Explicit nulls retained. Null, zero and an empty collection are different.
- UTC timestamps formatted `yyyy-MM-ddTHH:mm:ssZ`; calendar roast dates
  formatted `yyyy-MM-dd`.

Only in the fixture namespace, original SQLite `DateTimeKind.Unspecified`
digits mean UTC. `FixtureUtc` uses `DateTime.SpecifyKind`, not
`ToUniversalTime`. Local timestamps are refused. This does not change
the app's normal date model.

## Original diagnostic host protocol

This protocol belongs to the isolated comparison app, not DevFlow itself.
Use an exact approved device serial, package hash and signer, and a unique
explicit forward. Check lock/user-input guards before device changes.

The original diagnostic has a shell-permission receiver in the separate
`:fixturecontrol` process. That process does not initialize MAUI or a
database. After stopping only the owned diagnostic process:

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n com.simplyprofound.baristanotes.perf.diag/com.simplyprofound.baristanotes.perf.diag.FixtureControlReceiver \
  -a com.simplyprofound.baristanotes.perf.diag.SELECT_FIXTURE \
  --es namespace fixture-v2-100-example --ei count 100 --es mode provision
```

Require result `-1` and `Selection saved`. Stop the control process through
the owned package's normal force-stop, then launch the already installed
APK through the launch-only path. The main process reads the durable
selection at the start of `MauiProgram`, before the builder/service graph.
Use `validate` or `run` only with a complete namespace. The explicit
`namespace=review, count=0, mode=run` selection restores default storage
on the next process launch; it does not delete fixture data.

After explicit DevFlow wait and physical-device identity checks:

```text
GET  /api/v1/ext/com.baristanotes.fixture/status
POST /api/v1/ext/com.baristanotes.fixture/provision   body: {}
GET  /api/v1/ext/com.baristanotes.fixture/readback
```

The POST requires the normal claim/header/release lease protocol and
returns after scheduling the unmeasured job. Poll status for both
`runState=RanToCompletion` and `receipt.state=complete`. A failed job or
pending operation is a stop condition. GET readback opens fresh domain
services and returns canonical actual data. Restart in `validate` mode
and repeat the read to test process-independent persistence.

The Comet adapter can reuse all shared types, storage and validation.
Its bootstrap may differ, but it must establish the same storage boundary
before its own services resolve. No dependency on original app namespaces
is required.

## Tests

Use the approved isolated SDK wrapper and absolute project path:

```sh
BARISTA_FIXTURE_INPUTS=/absolute/path/to/fixtures-v2 \
  /absolute/path/to/toolchain.sh test \
  /absolute/path/to/Shared/Tests/Fixture.Tests.csproj -c Release
```

The tests disable reflection-based JSON. They cover exact parsing,
invalid inputs, null/zero distinctions, dates, canonical ordering,
storage separation, default-data preservation, completed validation and
failed/incomplete-state refusal. These tests do not replace the native
database gate.
