# Microsoft.Maui.DevFlow.Client

Portable client for the [Microsoft.Maui.DevFlow](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
agent wire protocol: `AgentClient`, the element and protocol DTOs, and their serialization.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

The package targets `netstandard2.0` as well as modern .NET, so a .NET Framework test harness (for
example Visual Studio Apex tests) drives a running app through exactly the same protocol code as a
.NET 10 `dotnet watch` or VS Code test run. The DTOs are defined once, here, so the harnesses cannot
drift in how they serialize or interpret the protocol.

Use [`Microsoft.Maui.DevFlow.Driver`](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Driver/)
instead if you also need platform driver functionality — app process management, UI Automation,
screenshot processing, and recording. It builds on this package and re-exports its types.

## Install

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Client" Version="0.1.0-preview" />
```

## Quick start

The app under test must be running the DevFlow agent (see `Microsoft.Maui.DevFlow.Agent` or
`Microsoft.Maui.DevFlow.Agent.Native`).

```csharp
using Microsoft.Maui.DevFlow.Driver;

using var client = new AgentClient("localhost", 9223);

var status = await client.GetStatusAsync();
Console.WriteLine($"{status?.AppName} on {status?.Platform}");

// Find an element and interact with it
var buttons = await client.QueryAsync(type: "Button", automationId: "SubmitButton");
await client.TapAsync(buttons[0].Id);

await client.FillAsync("NameEntry", "Ada Lovelace");

// Inspect the visual tree
var tree = await client.GetTreeAsync(maxDepth: 3);
```

## Requirements

- A consumer targeting `netstandard2.0` or later (including .NET Framework 4.6.2+)
- An app running a DevFlow agent, reachable over HTTP

## Files and live SQLite

File APIs resolve paths under an advertised storage root (`appData` by default).
They reject absolute paths, traversal, symbolic links, and mutations targeting the
root itself. Directory creation/deletion and `MoveAsync` stay within one root.
Delete is non-recursive unless explicitly requested.

Use streams for large transfers; the caller retains ownership of both streams:

```csharp
using var source = File.OpenRead("seed.db");
await client.UploadFileFromAsync("data/seed.db", source);
using var destination = File.Create("download.db");
await client.DownloadFileToAsync("data/seed.db", destination);
```

Uploads have a **64 MiB wire-body limit**. Raw uploads are staged on disk before
atomic replacement, so an incomplete upload does not truncate an existing file.
Base64 JSON remains supported, but downloads over **16 MiB** require raw streaming.
Seekable uploads can follow the client's retry policy; non-seekable uploads are
never retried automatically. A failed mutating request can have an indeterminate
outcome if its response was lost: do not blindly retry database writes.

SQLite uses the live file and the app's registered SQLitePCLRaw provider
(SQLite **3.35+**); it does not bundle or select a native provider. Check
`storage.sqlite` capabilities before opening a database. The host can explicitly
initialize its provider and configure agent-owned connections for encryption,
custom functions or collations; see the contributor README.

```csharp
var schema = await client.ReadDatabaseSchemaAsync("data/seed.db");
var page = await client.ReadDatabaseRowsAsync("data/seed.db", "people", maxRows: 100);
var edited = await client.UpdateDatabaseRecordAsync(
    "data/seed.db", "people", page.RowIds[0],
    new[] { new SqliteCellBody { Column = "name", Type = "text", Value = "Ada" } },
    expectedVersion: page.RowVersions[0]);
if (edited.ErrorCode == "conflict")
    Console.WriteLine("The app changed this row; refresh before saving.");
if (page.NextCursor is not null)
    page = await client.ReadDatabaseRowsAsync("data/seed.db", "people", 100, page.NextCursor);
```

`Rows` is display text; **`Cells` is the data contract**. Its type is `null`,
`integer`, `real`, `text` or `blob`; numbers are invariant strings and blobs are
base64. Null and empty values remain distinct. Values over **64 KiB** have
`Truncated=true` and no editable value. Never save display previews or truncated
cells. Generated/hidden columns are described but not writable.

Row IDs are signed **64-bit decimal strings**, safe to preserve in JavaScript.
Do not convert them to `Number`. Page responses include content versions; echo one
as `expectedVersion` on update/delete for atomic conflict detection. Omitting it
explicitly chooses last-write-wins. SQL errors and conflicts return typed results
with `Error`/`ErrorCode`; transport/HTTP failures on typed methods throw.

Results cap at **1000 rows and 1 MiB**, with 500 rows by default. Rowid tables use
ascending keyset pagination; views and tables without addressable rowids are
read-only in the grid and use visible-column ordering with offset pagination.
Cursors are scoped to the database/table/schema. Pages reflect live data, not
one long-lived snapshot; concurrent changes can shift later pages. Arbitrary SQL
has a capped first result set rather than a cursor: put paging in the SQL itself.

`ExecuteDatabaseAsync` runs complete SQL scripts, including writes and DDL. Earlier
statements can commit before a later error; use an explicit transaction when
needed. Only the first result set is returned, but all statements execute.
All mutating methods acquire the mutation lease, including SQL queries.

**Preview compatibility:** the new storage/SQLite DTOs live in this portable
package (the Driver forwards them), not the agent assembly. SQLite row IDs changed
from JSON integers to decimal strings. The original `JsonElement` convenience
methods remain available; use the typed methods above for new code.

## Links

- [DevFlow documentation](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
- [dotnet/maui-labs](https://github.com/dotnet/maui-labs)
