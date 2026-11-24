# TriangulationBench

Benchmarks for `Clipper.Triangulate`. It isn't part of `Clipper2.sln` or the package. It builds against the USINGZ project (`USINGZ/Clipper2Lib_Z.csproj`), which is what the `Clipper2` package ships, and calls the `PathsD` overload with `decPlaces` 6, as NodeFlow does.

```bash
cd CSharp/Benchmarks/TriangulationBench
dotnet run -c Release -- quick [nameFilter] [--seconds 2]      # median and min ms, allocations, both modes
dotnet run -c Release -- --filter '*' --join                    # BenchmarkDotNet, with [MemoryDiagnoser]
dotnet run -c Release -- verify [nameFilter]                    # exact checks of every case
dotnet run -c Release -- profile plate-500-holes true 20        # a hot loop to attach dotnet-trace to
```

`quick` warms up, then times each case for about `--seconds` (at least 5 runs). It's for checking a step quickly (the step commits quote it); the recorded results below come from BenchmarkDotNet.

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

BenchmarkDotNet 0.15.4 on a quiet machine (CPU load about 2%, build servers stopped), .NET 10.0.15, i9-12900HX, run one after the other on 2026-11-24. walrus.2 is commit `2102514` with the default job; walrus.1 is commit `4f89ab8` (this project copied in) with `--job short` (3 iterations, enough for the large gaps; read its small-case numbers as ±10%). Mean per call and allocated per call. NodeFlow calls with Delaunay on.

| Case | Delaunay | walrus.1 | walrus.2 | speedup | alloc walrus.1 | alloc walrus.2 |
|------|:--------:|---------:|---------:|--------:|---------------:|---------------:|
| nodeflow-comb | off | 29.8 us | 34.5 us | x0.9 | 115 KB | 118 KB |
| nodeflow-comb | on | 45.8 us | 45.3 us | x1.0 | 169 KB | 148 KB |
| nodeflow-disc-ellipses | off | 1.0 ms | 983.0 us | x1.0 | 1.1 MB | 1.1 MB |
| nodeflow-disc-ellipses | on | 1.7 ms | 1.3 ms | x1.3 | 1.6 MB | 1.3 MB |
| nodeflow-n972 | off | 22.5 us | 20.4 us | x1.1 | 85 KB | 89 KB |
| nodeflow-n972 | on | 55.5 us | 35.4 us | x1.6 | 152 KB | 115 KB |
| nodeflow-perforated | off | 80.3 ms | 5.1 ms | x15.8 | 23.3 MB | 5.4 MB |
| nodeflow-perforated | on | 83.7 ms | 4.5 ms | x18.5 | 25.6 MB | 6.5 MB |
| nodeflow-plate | off | 159.1 us | 163.1 us | x1.0 | 287 KB | 270 KB |
| nodeflow-plate | on | 359.9 us | 267.3 us | x1.3 | 466 KB | 341 KB |
| nodeflow-saved-Cad-fillet-twice-007-0 | off | 1.52 us | 1.49 us | x1.0 | 8 KB | 8 KB |
| nodeflow-saved-Cad-fillet-twice-007-0 | on | 1.84 us | 1.74 us | x1.1 | 9 KB | 9 KB |
| nodeflow-saved-Cad-sketch-assembly-001-0 | off | 7.95 us | 8.11 us | x1.0 | 35 KB | 36 KB |
| nodeflow-saved-Cad-sketch-assembly-001-0 | on | 20.8 us | 13.6 us | x1.5 | 57 KB | 46 KB |
| nodeflow-saved-Cad-test-002-0 | off | 1.51 us | 1.45 us | x1.0 | 8 KB | 8 KB |
| nodeflow-saved-Cad-test-002-0 | on | 1.82 us | 1.73 us | x1.1 | 9 KB | 9 KB |
| nodeflow-saved-Cad-test-002-1 | off | 14.1 us | 14.1 us | x1.0 | 63 KB | 65 KB |
| nodeflow-saved-Cad-test-002-1 | on | 20.1 us | 23.4 us | x0.9 | 90 KB | 82 KB |
| nodeflow-saved-Cad-test-003-0 | off | 2.40 us | 2.34 us | x1.0 | 10 KB | 11 KB |
| nodeflow-saved-Cad-test-003-0 | on | 3.97 us | 3.30 us | x1.2 | 14 KB | 13 KB |
| nodeflow-saved-Cad-test-003-1 | off | 1.72 us | 1.88 us | x0.9 | 9 KB | 9 KB |
| nodeflow-saved-Cad-test-003-1 | on | 2.41 us | 2.25 us | x1.1 | 11 KB | 11 KB |
| nodeflow-saved-Cad-test-004-0 | off | 2.04 us | 2.20 us | x0.9 | 10 KB | 10 KB |
| nodeflow-saved-Cad-test-004-0 | on | 2.69 us | 2.57 us | x1.0 | 12 KB | 12 KB |
| nodeflow-saved-Cad-test-005-0 | off | 2.13 us | 2.38 us | x0.9 | 10 KB | 10 KB |
| nodeflow-saved-Cad-test-005-0 | on | 2.73 us | 2.60 us | x1.1 | 12 KB | 12 KB |
| nodeflow-saved-Cad-test-005-1 | off | 83.9 us | 98.8 us | x0.8 | 227 KB | 236 KB |
| nodeflow-saved-Cad-test-005-1 | on | 239.4 us | 183.1 us | x1.3 | 388 KB | 294 KB |
| nodeflow-saved-Cad-test-006-0 | off | 24.5 us | 30.2 us | x0.8 | 91 KB | 100 KB |
| nodeflow-saved-Cad-test-006-0 | on | 45.0 us | 42.3 us | x1.1 | 143 KB | 126 KB |
| nodeflow-saved-Cad-test-007-0 | off | 83.4 us | 97.3 us | x0.9 | 204 KB | 212 KB |
| nodeflow-saved-Cad-test-007-0 | on | 221.3 us | 163.6 us | x1.4 | 355 KB | 268 KB |
| nodeflow-saved-Cad-test-007-1 | off | 34.9 us | 33.7 us | x1.0 | 127 KB | 129 KB |
| nodeflow-saved-Cad-test-007-1 | on | 95.1 us | 57.3 us | x1.7 | 218 KB | 163 KB |
| nodeflow-saved-Cad-test-008-0 | off | 47.3 us | 51.1 us | x0.9 | 141 KB | 152 KB |
| nodeflow-saved-Cad-test-008-0 | on | 119.2 us | 78.1 us | x1.5 | 224 KB | 190 KB |
| nodeflow-saved-Cad-tube-plate-001-0 | off | 235.7 us | 243.1 us | x1.0 | 370 KB | 346 KB |
| nodeflow-saved-Cad-tube-plate-001-0 | on | 543.6 us | 394.8 us | x1.4 | 619 KB | 437 KB |
| nodeflow-saved-Cad-tube-plate-001-1 | off | 87.3 us | 103.8 us | x0.8 | 230 KB | 248 KB |
| nodeflow-saved-Cad-tube-plate-001-1 | on | 289.4 us | 195.1 us | x1.5 | 411 KB | 309 KB |
| nodeflow-slice-Plate | off | 1.6 ms | 1.6 ms | x1.0 | 1.5 MB | 2.3 MB |
| nodeflow-slice-Plate | on | 9.1 ms | 3.7 ms | x2.4 | 5.6 MB | 2.6 MB |
| nodeflow-slice-base-tube-plate-001 | off | 640.8 us | 832.3 us | x0.8 | 850 KB | 1.1 MB |
| nodeflow-slice-base-tube-plate-001 | on | 1.7 ms | 1.2 ms | x1.4 | 1.6 MB | 1.3 MB |
| big-circle-bigint | off | 30.3 ms | 2.4 ms | x12.7 | 4.3 MB | 4.6 MB |
| big-circle-bigint | on | 36.4 ms | 4.1 ms | x8.9 | 15.3 MB | 6.0 MB |
| comb-10k | off | 260 ms | 259 ms | x1.0 | 14.6 MB | 209.8 MB |
| comb-10k | on | 234 ms | 219 ms | x1.1 | 20.8 MB | 213.1 MB |
| dense-circle | off | 58.8 ms | 3.6 ms | x16.5 | 6.9 MB | 7.6 MB |
| dense-circle | on | 59.1 ms | 5.9 ms | x10.0 | 11.2 MB | 9.6 MB |
| grid-cad | off | 824.3 us | 854.8 us | x1.0 | 1.3 MB | 1.2 MB |
| grid-cad | on | 1.2 ms | 1.1 ms | x1.1 | 1.9 MB | 1.6 MB |
| n972-ring | off | 22.3 us | 20.9 us | x1.1 | 85 KB | 89 KB |
| n972-ring | on | 55.1 us | 35.7 us | x1.5 | 152 KB | 115 KB |
| plate-100-holes | off | 140 ms | 14.6 ms | x9.6 | 36.7 MB | 16.0 MB |
| plate-100-holes | on | 141 ms | 13.6 ms | x10.4 | 43.9 MB | 18.4 MB |
| plate-500-holes | off | 10,282 ms | 89.7 ms | x114.7 | 711.0 MB | 80.5 MB |
| plate-500-holes | on | 9,013 ms | 111 ms | x81.0 | 745.0 MB | 92.3 MB |
| star-10k | off | 262 ms | 108 ms | x2.4 | 14.2 MB | 26.2 MB |
| star-10k | on | 251 ms | 110 ms | x2.3 | 20.2 MB | 29.4 MB |

What NodeFlow sees (Delaunay on): perforated x18.5 and slice-Plate x2.4, every other real input as fast or faster except saved-Cad-test-002-1 (20 -> 23 us). Allocations drop on all of them.

Known costs of walrus.2:

- With Delaunay off, several small real inputs are 10-20% slower (slice-base-tube-plate-001 641 -> 832 us, saved-Cad-test-006-0 24.5 -> 30.2 us). Probably the clip order of `145e38b`; NodeFlow doesn't use this mode.
- comb-10k allocates about 210 MB per call against 15-21 MB on walrus.1, at the same time. Once the teeth are clipped, the base is a strip with collinear top vertices, so any triangulation is a fan of long diagonals, and the ear index registers each of them in hundreds of cells. Synthetic; NodeFlow's comb (84 points) is fine.
- star-10k: 30% of the time is the up-front Union.

Steps: `858a3f7` bridging grid and touching holes, `f13a15b` ear index, `7eda2f5` half-edge Legalize, `a21a0ee` SplitPinches prefix sums, `145e38b` clip order, `63e16aa` in-circle filter. Each commit message has its own before/after numbers (from `quick`, on a loaded machine).
