# BaristaNotes Android comparison tools

These tools support the planned comparison of the original MAUI/MauiReactor
BaristaNotes app with the Comet/Compose implementation. Both measurement
artifacts must be real Android arm64 Release Native AOT builds.

The full APK integration and timing runner are not yet complete. These
preparation tools do not produce a performance result.

## Read-only phone conditions

From the repository root:

```sh
python3 -B src/Comet/tools/perf/baristanotes/device_conditions.py \
  --serial 13041FDD4007MT \
  --output /path/to/new/conditions.json
```

This command reads the approved Pixel 5's identity, battery, temperature,
thermal status, active user, battery saver, and animation settings. It does
not change settings, stop apps, clear data, or clear logs.

Exit codes:

- `0`: the reported conditions meet this gate.
- `1`: a command, field, or output-file check failed.
- `2`: conditions were read, but at least one measurement condition failed.

The gate requires at least 80% charge, battery temperature at most 35 C,
thermal status NONE, battery saver off, user 0, and the observed animation
settings of 0. Use `--baseline-temperature` to check against an accepted
temperature baseline with a 2 C tolerance. A single passing sample does not
establish temperature stability, an unlocked screen, a correct app state,
or absence of background interference.

The output file must not exist. Retain condition snapshots alongside the
measurements rather than replacing earlier records.

## Shared fixture input

```sh
python3 -B src/Comet/tools/perf/baristanotes/fixtures.py \
  --output-directory /path/to/new/fixture-directory
```

This creates deterministic inputs for 100 and 1,000 saved drinks, five
coffees and bags, two machines, two grinders, and two people. The latest
100 drinks and all supporting entities match between the two datasets.
Dates, values, preferences, and names are fixed; no personal data is used.

Version 2 uses explicit JSON `null` for `preinfusionTime`. The original
`ShotService.CreateShotAsync` and `MapToDto` omit this nullable value, so
zero does not round-trip through that domain path. Version 1 requested zero
and must not be imported. Keep old files as evidence and generate version 2
in a new directory. Do not treat null and zero as equal or change the
original app to force a match. All other fixture values are unchanged.

The generator writes files only. It does not install an app or access a
database. App adapters must apply the inputs through approved domain
commands in separate test storage namespaces, then verify persisted
semantic values. Input hashes alone do not prove that an app stored or
rendered the expected data. Do not reset an existing namespace on failure.

## Tests

```sh
python3 -B -m unittest discover \
  -s src/Comet/tools/perf/baristanotes -p 'test_*.py' -v
```
