# Native AOT logging smoke check

This executable tests the actual DevFlow logging library with Native AOT and
reflection-based JSON serialization disabled. It covers buffered and
persisted reads, the existing JSONL field names and null values, console
capture, and `ILogger`.

From the repository root on an arm64 Mac:

```sh
dotnet publish src/DevFlow/tools/LoggingAotSmoke/LoggingAotSmoke.csproj \
  -c Release -r osx-arm64 -o /path/to/new/logging-smoke-output
/path/to/new/logging-smoke-output/LoggingAotSmoke
```

Use the host's supported RID on other systems. The native compiler required
by .NET Native AOT must be installed.

The process fails if dynamic code or reflection JSON is enabled, or if a
round-trip/format assertion fails. It writes only to its own unique temporary
directory and removes that directory on exit.

This is a logging check, not proof of Native AOT support for the complete
DevFlow agent. The Android agent must also pass its broker, HTTP, visual-tree,
screenshot, and interaction checks.
