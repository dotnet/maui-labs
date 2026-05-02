# Comet Todo App

A simple todo list app demonstrating Comet's reactive state management.

Ported from the [MauiReactor TodoApp](https://github.com/nicwise/mauireactor-samples/tree/main/TodoApp), replacing ReactorData+SQLite persistence with in-memory `SignalList<T>` reactive state.

## What It Showcases

- **SignalList&lt;T&gt;** — reactive observable list for the todo collection
- **Component&lt;TState&gt;** — SetState for two-way TextField binding
- **CheckBox** — toggle completion state with reactive list update
- **Delete button** — per-item removal from the SignalList
- **Batch operations** — "Clear Completed" uses `SignalList.Batch()` for a single UI update
- **Reactive summary** — live counts of total/done/pending items

## Build & Run

```bash
# Prerequisites: build Comet first
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release

# Build the sample
dotnet build sample/CometTodoApp/CometTodoApp.csproj -f net10.0-ios -c Debug

# Run on Mac Catalyst
dotnet build sample/CometTodoApp/CometTodoApp.csproj -t:Run -f net10.0-maccatalyst
```

## Platforms

| Platform | TFM |
|----------|-----|
| iOS | net10.0-ios |
| Mac Catalyst | net10.0-maccatalyst |
