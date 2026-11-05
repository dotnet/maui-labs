---
name: maui-networking-offline-data
description: >-
  Build MAUI networking and offline data. USE FOR: typed `HttpClient`, JSON serialization, Android `10.0.2.2`, iOS simulator localhost, LAN/dev-tunnel fallback, debug cleartext, offline-first screens, SQLite/EF Core sync metadata, queues, encryption decisions, retries, cancellation. DO NOT USE FOR: auth redirects, Aspire service discovery, UI layout.
---

# MAUI Networking and Offline Data

Use this skill when a MAUI feature calls backend APIs, works against local
services during development, persists local data, or must behave well offline.

## Workflow

1. Inspect `MauiProgram.cs`, existing API clients, model serialization, and data
   storage packages.
2. Register API clients through DI. Prefer typed clients or named clients over
   constructing `HttpClient` in pages.
3. Define request/response DTOs and serialization options once. Prefer
   `System.Text.Json` source generation for trimmed or NativeAOT-sensitive apps.
4. Decide the local development address by runtime platform and environment.
5. Keep cleartext HTTP exceptions debug-only and platform-scoped.
6. Define offline boundaries before schema/code: name the read-only cached
   reference data, the entities users can edit while offline, and the fields
   that remain server-authoritative.
7. For SQLite/offline sync answers, show the storage initialization API before
   the entity schema: explicitly name `SQLiteAsyncConnection` plus
   `CreateTableAsync<T>` for sqlite-net, or an EF Core `DbContext` setup. Do not
   only show POCO models, sqlite-net attributes, or `db.Table<T>()` queries.
8. In every offline-sync design, include a security line: if cached data is
   sensitive, regulated, or contains customer/business PII, encrypt the local
   SQLite store, for example with SQLCipher/provider encryption, and keep the
   database key in `SecureStorage` rather than source code or `Preferences`.
9. In every offline-sync design, include a background performance line: drain
   queued sync work in bounded batches/chunks off the UI thread, and marshal only
   UI updates through `MainThread` when needed.
10. Store local data in SQLite or app data files behind a repository/service
   abstraction.
11. Add cancellation, timeout, retry, and user-facing error states.

## HttpClient DI Pattern

```csharp
builder.Services.AddHttpClient<IProductsApi, ProductsApi>(client =>
{
    client.BaseAddress = new Uri("https://api.contoso.dev/");
});
```

Keep auth headers in a delegating handler or typed client boundary. Do not add
bearer tokens in every page or ViewModel.

## Local Development Networking

| Runtime | Local backend address |
| --- | --- |
| Android emulator | Use `10.0.2.2` to reach the host machine. |
| iOS simulator | `localhost` usually reaches the Mac host. |
| Windows/Mac Catalyst app | `localhost` reaches the same machine. |
| Physical device | Use a LAN-reachable hostname/IP, reverse tunnel, dev proxy, or deployed endpoint. |

For cleartext HTTP during development:

- Android needs a debug-only network security config such as
  `networkSecurityConfig` or `UsesCleartextTraffic`. Gate it with `#if DEBUG`,
  `IsDevelopment`, or an MSBuild `Condition` on the Debug configuration so it is
  never present in Release builds.
- iOS/Mac Catalyst need an App Transport Security (`NSAppTransportSecurity`)
  exception for debug HTTP, also guarded by a Debug-only condition.
- Release builds should use HTTPS and remove broad cleartext exceptions.

## Connectivity Check

Use MAUI connectivity APIs to decide when to show offline UI and when to drain
sync queues:

```csharp
public sealed class SyncService(IConnectivity connectivity)
{
    public bool IsOnline => connectivity.NetworkAccess == NetworkAccess.Internet;
}
```

Subscribe to `ConnectivityChanged` from a long-lived service when the app should
resume sync on reconnect, and unsubscribe when the owner is disposed. Only
`NetworkAccess.Internet` indicates a routable connection. In migrated apps,
replace `Xamarin.Essentials.Connectivity` with `Microsoft.Maui.Networking`
APIs or injected `IConnectivity`.

## JSON Guidance

- Use one `JsonSerializerOptions` instance for casing, enum conversion, and null
  handling.
- Prefer source-generated `JsonSerializerContext` when the app is trimmed,
  NativeAOT-sensitive, or serializes many known DTOs.
- Separate DTOs from database entities when local schema and API shape evolve at
  different speeds.

## SQLite and Offline Sync

Use SQLite for structured local state, offline queues, and cached server data.
Start offline-sync guidance with an explicit boundary list or table so the
answer does not imply every local row is editable. For example:

| Boundary | Examples | Sync behavior |
|----------|----------|---------------|
| Read-only cached reference data | product catalog, feature flags, lookup values | pull/refresh from server; do not enqueue local edits |
| Offline-editable user data | draft orders, notes, inspection forms | local writes get dirty state and outbox entries |
| Server-authoritative data | payment status, inventory counts, account roles | display cached values but require online server confirmation before mutation |

For sqlite-net, this usually means a `SQLiteAsyncConnection` and
`CreateTableAsync<T>` setup before any `db.Table<T>()` queries. For EF Core, keep
the local schema behind a `DbContext`.

Always include a short initialization snippet in offline-sync guidance so the
storage layer is concrete, not just the row shape or sqlite-net attributes.
Immediately after the boundary table or storage initialization, include a
one-sentence security decision for sensitive offline data, naming encryption
such as SQLCipher and a `SecureStorage`-protected key when applicable.
Also include a one-sentence sync performance decision using the words
`background` and `batch` or `chunk`, for example: "Drain the outbox on a
background worker in small batches/chunks and use `MainThread` only to update
progress UI."

```csharp
var db = new SQLiteAsyncConnection(databasePath);
await db.CreateTableAsync<Order>();
await db.CreateTableAsync<SyncOperation>();
```

Common fields for syncable rows:

- Server ID and local ID.
- `UpdatedAt`, `ETag`, row version, or another concurrency token.
- Dirty state such as `PendingCreate`, `PendingUpdate`, or `PendingDelete`.
- Tombstone/deleted marker when deletes must sync.

Keep sync boundaries explicit:

- Cache read-only reference data separately from editable offline data and say
  which local tables are never enqueued for upload.
- Retry idempotent operations automatically; ask the user before replaying
  non-idempotent actions.
- Resolve conflicts with a documented policy: server wins, client wins, field
  merge, or user decision.
- Persist queued operations before sending them so app termination does not lose
  offline edits.
- Encrypt the local SQLite database when it stores sensitive business data.
  Options include SQLCipher-based packages or provider-specific SQLite
  encryption. Store the database key in `SecureStorage`, not in source code or
  `Preferences`.
- Page or chunk large sync operations and write in bounded batches so the app
  stays responsive and avoids mobile memory spikes.
- Drain sync queues on background work, not the UI thread; marshal only progress
  or completion updates back with `MainThread.BeginInvokeOnMainThread` when UI
  needs to change.

## Retry and Error Handling

- Pass `CancellationToken` from UI commands and lifecycle shutdown paths.
- Use short connection timeouts and clear user messages for no network,
  unauthorized, validation, and server errors.
- Retry only transient failures such as timeouts and 5xx responses.
- Use exponential backoff with jitter for background sync.
- Do not silently swallow sync failures; surface pending state and retry status.

## Validation Checklist

- API clients are registered in DI and are not created directly in UI code.
- Local dev base addresses are platform-aware.
- Cleartext HTTP exceptions are debug-only.
- SQLite state has an explicit sync/conflict boundary and names the concrete
  storage API, such as `SQLiteAsyncConnection` with `CreateTableAsync<T>` or an
  EF Core `DbContext`.
- Sensitive local data has an encryption decision, such as SQLCipher plus a key
  stored in `SecureStorage`.
- Requests support cancellation and distinguish transient from permanent errors.
