using Xunit;

// The tool writes through process-global state: Console.Out/Error and Spectre's
// AnsiConsole.Console. Any test that redirects those to inspect what a run produced is
// therefore not isolated from a test running beside it — on CI, a manifest-fallback banner
// rendered by another class landed, ANSI escapes and all, in a captured stdout that was
// asserted to be pure JSONL. Collections only serialize the classes inside them, so the fix
// has to cover the whole assembly. The suite runs in seconds; determinism is worth more here
// than the parallelism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
