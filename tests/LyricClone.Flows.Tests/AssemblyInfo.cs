// All UI tests share one dispatcher thread; running them in parallel would let one test's dialog be found by another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
