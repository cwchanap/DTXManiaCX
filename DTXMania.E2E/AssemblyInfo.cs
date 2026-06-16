using Xunit;

// E2E gameplay tests each launch a real game process bound to an ephemeral API port.
// Running two such tests in parallel would start two game processes at once, which is
// flaky on CI. Serialize all collections in this assembly so the gameplay smokes run
// one at a time. (The in-process E2E-Support tests are fast, so serializing is cheap.)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
