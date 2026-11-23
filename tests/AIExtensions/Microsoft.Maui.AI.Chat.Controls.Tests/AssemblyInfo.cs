using Xunit;

// The tests share Application.Current and the current dispatcher provider.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
