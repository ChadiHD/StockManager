using Xunit;

// UI Automation drives a single shared desktop; running these in parallel would fight
// over focus and the app window. Force serial execution.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
