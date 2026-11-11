using Xunit;

// The chat views share Application.Current, dispatchers and synchronization contexts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
