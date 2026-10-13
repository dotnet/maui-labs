# Paging, HTTP and database sources

Load through the app's repository; bind/layout only requests bounded demand.
[Sources B6, W5, D1-D7](sources.md)

## Choose the data contract

| Reality | Policy |
|---|---|
| Small resident list | Indexed source; no unnecessary paging abstraction |
| Unknown-total HTTP feed | Cursor append, explicit load state and bounded retention |
| Chat/history | Bidirectional cursors, prepend anchor and explicit follow-tail |
| Large local database | Projected indexed pages; bounded query worker/cache |
| Offline-first | Database is one UI source of truth; network commits rows/keys |
| Known-count random access | Nonblocking placeholder indexer plus source-supported seek/cache |

Pagination bounds one response, not retained memory. Choose eviction/refetch
policy separately from offline persistence. A stream still needs an indexed/
windowed presentation buffer.

## Scheduler

Per query epoch and direction:

```text
Idle -> Loading -> Ready / End / Error
                 -> Cancelled after source change
```

Deduplicate by query/epoch/cursor/page key; bound metadata and decode concurrency
independently. Prioritize visible demand over speculative prefetch, share work,
and release each consumer's demand correctly.

Use native prefetch or viewport distance as a hint, not an unlimited task launcher.
Tune from measured speed/latency and memory budgets. Initial viewport fill can
need several bounded pages; stop on end/error/repeated cursor/no progress.

Reject old epochs even after successful completion. Errors are not empty-success
pages; preserve shown data on append failure, expose retry, and stop spinners.
Cancellation is not end-of-data. Use the app's retry/rate-limit/auth policy.

## HTTP

Reuse the existing authenticated HttpClient/client infrastructure. Use server
ordering, opaque continuation tokens and snapshot/freshness semantics.
Request IDs, dimensions, thumbnail keys and revisions in lightweight metadata.
Keep response size/parsing bounded and propagate cancellation.

Separate metadata from asset requests. Key thumbnails by asset revision, output
pixel size and crop. Decode through documented thread-safe APIs, then publish
ready content on the native dispatcher. Bound decoded-byte memory, not only count.

## Database

Project only displayed fields and metadata; do not trigger lazy-loading getters
in a binder. Index filters plus unique ordering and continuation; inspect the
actual provider query plan. Add a unique ID tie-breaker to timestamps/group keys.

Prefer keyset continuation for next/previous scrolling. Deep OFFSET may process
the skipped prefix and can duplicate/skip data under mutations. Keyset does not
provide free arbitrary page/index access or a cross-page consistent snapshot.

Microsoft.Data.Sqlite async APIs execute synchronously. An Async name is not
permission to query on the UI thread. Use a bounded repository-owned worker and
correct connection/context ownership, not Task.Run per cell.

EF Core contexts are not thread-safe; use short unit-of-work ownership/factories,
never indiscriminate sharing across parallel loads. Dispose readers/transactions
promptly. WAL helps concurrency but retains one writer/checkpoint/durability
constraints; do not hold a transaction for a page lifetime or weaken durability
just for scrolling performance.

## Offline-first and groups

Use the RemoteMediator architecture, not necessarily its Kotlin APIs:

```text
native demand -> HTTP -> transaction: rows + query-scoped remote keys
                      -> database invalidation/projection -> native updates
```

Do not append the HTTP response and append it again from a database observer.
Refresh invalidates older append/prepend work; commit remote keys with the rows.

Pages can split groups. Carry stable section key/continuation/completeness so
subsequent pages extend one group and do not duplicate headers or final footers.
Collapsed groups should not force all children to load.

**Test:** duplicate demand, concurrency cap, late refresh race, errors/retry,
repeated cursor, no progress, source replacement, bounded eviction/refetch,
duplicate sort keys and DB/network double publication.
