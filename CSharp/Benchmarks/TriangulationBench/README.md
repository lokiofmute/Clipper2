# TriangulationBench

Benchmarks for `Clipper.Triangulate`. It isn't part of `Clipper2.sln` or the package. It builds against the USINGZ project (`USINGZ/Clipper2Lib_Z.csproj`), which is what the `Clipper2` package ships, and calls the `PathsD` overload with `decPlaces` 6, as NodeFlow does.

```bash
cd CSharp/Benchmarks/TriangulationBench
dotnet run -c Release -- quick [nameFilter] [--seconds 2]      # median and min ms, allocations, both modes
dotnet run -c Release -- --filter '*' --join                    # BenchmarkDotNet, with [MemoryDiagnoser]
dotnet run -c Release -- verify [nameFilter]                    # exact checks of every case
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

## Verify

`verify [filter]` runs every case through the `Paths64` overload in both modes and checks the result exactly, as the fuzz harness does: success, positive triangles, exact coverage (the triangles' edges minus the region's cancel as chains), and with Delaunay, every shared edge locally Delaunay (BigInteger in-circle). It exits with 1 if any case fails. The fuzz inputs are small, so this is the check for large rings.

## Results at 2.1.0-walrus.2

`quick`, median ms with Delaunay, the walrus.1 code (`4f89ab8`) and the walrus.2 code (`dbc1c4b`) interleaved in one session while Visual Studio was installing (absolute numbers inflated by about 30%, ratios reliable):

| Case | walrus.1 | walrus.2 | |
|------|---------:|---------:|-:|
| nodeflow-perforated | 104.8 | 5.6 | x18.8 |
| nodeflow-slice-Plate | 11.4 | 4.6 | x2.5 |
| nodeflow-slice-base-tube-plate-001 | 2.1 | 1.3 | x1.6 |
| nodeflow-disc-ellipses | 2.0 | 1.4 | x1.4 |
| nodeflow-saved-Cad-tube-plate-001-0 | 0.67 | 0.50 | x1.3 |
| nodeflow-plate | 0.44 | 0.33 | x1.3 |
| nodeflow-n972 | 0.066 | 0.054 | x1.2 |
| nodeflow-saved-Cad-test-002-1 | 0.03..0.05 | 0.056 | slower by ~20 us |

Synthetic, from the step commits: plate-500-holes 9016 -> ~140, plate-100-holes 151 -> ~17, dense-circle 66 -> ~8, big-circle-bigint 37 -> ~5, star-10k 248 -> ~140 (30% of it the up-front Union). comb-10k stays at 200..370 ms with ~215 MB allocated: its base is a strip with collinear top vertices, so any triangulation is a fan of long diagonals, each registered in hundreds of index cells.

Steps: `858a3f7` bridging grid and touching holes, `f13a15b` ear index, `7eda2f5` half-edge Legalize, `a21a0ee` SplitPinches prefix sums, `145e38b` clip order, `63e16aa` in-circle filter. Each commit message has its own before/after numbers.
