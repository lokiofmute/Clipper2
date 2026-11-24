# TriangulationBench

Benchmarks for `Clipper.Triangulate`. It isn't part of `Clipper2.sln` or the package. It builds against the USINGZ project (`USINGZ/Clipper2Lib_Z.csproj`), which is what the `Clipper2` package ships, and calls the `PathsD` overload with `decPlaces` 6, as NodeFlow does.

```bash
cd CSharp/Benchmarks/TriangulationBench
dotnet run -c Release -- quick [nameFilter] [--seconds 2]      # median and min ms, allocations, both modes
dotnet run -c Release -- --filter '*' --join                    # BenchmarkDotNet, with [MemoryDiagnoser]
dotnet run -c Release -- profile plate-500-holes true 20        # a hot loop to attach dotnet-trace to
```

`quick` warms up, then times each case for about `--seconds` (at least 5 runs). It's for checking a step quickly; the numbers in commit messages come from BenchmarkDotNet.

## Cases

| Case | Input |
|------|-------|
| `n972-ring` | NodeFlow's N972 sketch circle, 68 points |
| `dense-circle` | the same circle with 5834 points |
| `plate-100-holes`, `plate-500-holes` | a plate with 10x10 / 25x20 round holes of 64 points |
| `star-10k` | a concave star of 10000 points, every other vertex reflex |
| `comb-10k` | a comb of 2500 teeth (10002 points) |
| `grid-cad` | 400 grid-aligned rectangles, a quarter of them subtracted: collinear runs, T-touches, pinches |
| `big-circle-bigint` | a 2 m circle of 4000 points, where in-circle tests use the BigInteger fallback |
| `inputs/*.txt` | real inputs captured from NodeFlow (see below) |

## Input files

`inputs/<name>.txt` becomes a case named `<name>`. Format: an optional header `# name decPlaces=6 delaunay=true`, then one path per line as `x,y x,y ...` (invariant culture, in mm). Blank lines are ignored.

## Baseline

`quick`, commit `4f89ab8` (before the optimization work), Release, .NET 10, on frank's workstation (shared with other work, so ±30% on the long cases). Median ms, `useDelaunay` false / true:

| Case | Points | false | true | Alloc (true) |
|------|-------:|------:|-----:|-------------:|
| n972-ring | 68 | 0.036 | 0.063 | 152 KB |
| dense-circle | 5834 | 58 | 66 | 11 MB |
| plate-100-holes | 6404 | 131 | 151 | 44 MB |
| plate-500-holes | 32004 | 8776 | 9016 | 763 MB |
| star-10k | 10000 | 240 | 248 | 21 MB |
| comb-10k | 10002 | 239 | 250 | 21 MB |
| grid-cad | 848 | 0.92 | 1.32 | 1.9 MB |
| big-circle-bigint | 4000 | 26 | 37 | 16 MB |
| nodeflow-perforated | 2582 | 92 | 93 | 26 MB |
| nodeflow-slice-Plate | 1088 | 1.8 | 9.8 | 5.7 MB |
| nodeflow-disc-ellipses | 547 | 1.2 | 1.9 | 1.6 MB |
| nodeflow-plate | 187 | 0.18 | 0.41 | 466 KB |

Profiles (dotnet-trace, sampled): perforated spends 77% in hole bridging (`FindBridge`, 58% in `SegmentBlocked`); slice-Plate with Delaunay 78% in `Legalize`; dense-circle 90% in `IsEar`; star-10k 59% in `IsEar` and 30% in the up-front Union (the clipping engine, out of scope).
