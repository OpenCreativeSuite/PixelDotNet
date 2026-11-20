using Xunit;

// The editor exposes a handful of process-wide statics: the pointer modifier
// snapshot, the viewport pan gesture and the font catalogue. Running component
// tests in parallel would let one test press Ctrl while another is selecting.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
