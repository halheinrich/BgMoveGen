using BenchmarkDotNet.Running;
using BgMoveGen.Benchmarks;

// Entry point for the benchmark harness. Run the whole set with
//   dotnet run -c Release --project BgMoveGen.Benchmarks
// or filter, e.g.
//   dotnet run -c Release --project BgMoveGen.Benchmarks -- --filter *Doubles*
BenchmarkSwitcher.FromAssembly(typeof(MoveGenerationBenchmarks).Assembly).Run(args);
