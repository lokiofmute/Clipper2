# State: triangulation performance work (2026-11-24, evening)

Handover from the session that optimized `Clipper.Triangulate` (following `handover-2026-11-24-triangulation-perf.md`) to a fresh session. Read this first; the older handover has the background.

## Where things stand

- All work is on `main` (frank asked for his current branch, not a feature branch), nothing pushed. Last commit `2102514`.
- `Clipper2 2.1.0-walrus.2` is packed in `CSharp/artifacts` (git-ignored) and **wired into NodeFlow** by session `walrus-nodeflow-v2-90` (NodeFlow commit `4c22c335`): its 22 inputs all succeed through NodeFlow's sketch-fill path, and plycheck shows nothing new failing. That session will report quiet-machine timings and frank's perforated-panel drag test. walrus.3 (`2f7da1f`, allocation fix) has been handed over since; the next package anyone restores must be `2.1.0-walrus.4`.
- Public API and contract unchanged: success only when the triangles cover the region exactly (else `fail`), positive triangles, locally Delaunay edges with `useDelaunay`, always terminates. N972 tests present.
- Gates at `2102514`: tests 117 plain / 125 USINGZ; fuzz `star 0..100000` and `region 0..50000` 0 bad, `grid 0..200000` 4 bad, all honest `fail` (seeds 70327 and 182727: the engine's Union output crosses itself by under a unit); `verify` exact on all 30 benchmark cases.

## Done: BenchmarkDotNet numbers (commit `715ffba`)

Quiet-machine BenchmarkDotNet results for walrus.1 vs walrus.2 are in `CSharp/Benchmarks/TriangulationBench/README.md` ("Results at 2.1.0-walrus.2"). With Delaunay on: perforated 83.7 -> 4.5 ms, slice-Plate 9.1 -> 3.7 ms, saved-Cad-test-002-1 20 -> 23 us (the only NodeFlow regression). The verdict went to the NodeFlow session, now named `walrus-nodeflow-v2-24` (was `-90`): keep walrus.2, no walrus.3 needed.

Gotcha: BenchmarkDotNet's generated project fails with "apphost.exe not found" (MAX_PATH) when built under the scratchpad path. Put worktrees under `.superpowers/` (git-ignored, short path).

## What was done (one commit per step, numbers in each message)

| Commit | Step |
|--------|------|
| `3d8961c` | Benchmark project (quick, BenchmarkDotNet, profile modes; 22 real NodeFlow inputs); exact coverage check in the fuzz harness (chain cancellation, replacing Union/Xor, whose engine drops sub-unit slivers) plus a `selftest` family |
| `858a3f7` | Hole bridging through a grid; a hole touching the outer at a vertex is joined there (fixed grid 6407, 14230, 27761) |
| `f13a15b` | `EarIndex`: exact grid index for `IsEar`, rings of 512+ nodes (smaller rings keep the scan, which is faster there) |
| `7eda2f5` | `Legalize` on a flat half-edge structure; benchmark `verify` mode |
| `a21a0ee` | `SplitPinches` with prefix sums (output bit-identical, checked with the benchmark `hash` mode); `quick` warms up for 400 ms |
| `145e38b` | Ear clipping continues at the vertex after next (no fans of slivers; changes which triangulation comes out) |
| `63e16aa` | In-circle sign decided in double when its error bound allows, else exact; randomized test against BigInteger |
| `dbc1c4b`, `2102514` | Package walrus.2; READMEs record baseline, results, fuzz state |

Headline (interleaved `quick`, Delaunay on): nodeflow-perforated 104.8 -> 5.6 ms, slice-Plate 11.4 -> 4.6, plate-500 9016 -> ~140, dense-circle 66 -> ~8. One small regression: saved-Cad-test-002-1 (48 pts) about 20 us slower.

## Open items, roughly by value

- ~~comb-10k allocation regression~~ fixed in `271c154` (CellLists free list + sweep before growing): comb-10k 215 -> 21 MB, plate-500 95 -> 58 MB, perforated 6.6 -> 5.3 MB, output bit-identical. Packed as `2.1.0-walrus.3` (`2f7da1f`) and sent to `walrus-nodeflow-v2-24`; the next pack anyone restores must be `walrus.4`.
- **Delaunay-off small inputs 10-20% slower** than walrus.1 (probably the clip order of `145e38b`); NodeFlow doesn't use this mode.
- **Skip the up-front Union for clean input**: it is 30% of star-10k. Needs a cheap, exact "already simple and nested" test, or it isn't worth it. The clipping engine itself is out of scope (byte parity with the C++).
- **Allocations**: nodeflow-perforated allocates about 6.6 MB per call (ClipEars 1.9 MB, Legalize 1.1 MB, Union 0.6 MB). Ideas: `Node` objects to index arrays; triangles as a struct list instead of `Point64[]` (Legalize currently rebuilds them all).
- **comb-10k** stays at 200..370 ms with ~215 MB allocated: once the teeth are clipped, the base is a strip with collinear top vertices, so any triangulation is a fan of long diagonals, each registered in hundreds of index cells. Synthetic; NodeFlow's comb (84 pts) is fine.
- **The two honest `fail` seeds** (70327, 182727) would need snap-rounding of the Union's output; out of scope for now.
- **saved-Cad-test-002-1**: 3 us slower than walrus.1 on a quiet machine (20 -> 23 us). Negligible.

## Tools and how to use them

- Gate after every change: `dotnet test CSharp/Clipper2.sln -c Release`, and `CSharp/Tools/TriangulationFuzz` (`star 0 20000`, `region 0 10000`, `grid 0 50000`, `review 0 -1`; wider ranges for anything that changes output). Run the three families in parallel; each takes 7-13 s.
- `CSharp/Benchmarks/TriangulationBench` (see its README): `quick [filter] [--seconds s]`, `verify [filter]` (exact checks on the large cases, which fuzz doesn't reach), `hash` (fingerprints of every output, to prove a change leaves results identical: build the previous commit in a worktree and diff), `profile <case> <true|false> <s>` for `dotnet-trace collect --format speedscope` (give dotnet-trace the absolute exe path).
- `TRI_CHECK` build (`-p:DefineConstants=TRI_CHECK`, plus `%3BUSINGZ` for the benchmark) makes every ring of 4+ nodes use `EarIndex` and throws if it ever disagrees with the whole-ring scan. Use it after touching the index.
- Comparing timings: on this machine run old and new interleaved, case by case, in one session; the absolute numbers drift with whatever else is running.

## NodeFlow facts (from `walrus-nodeflow-v2-90`)

One call site, `SketchPreview.Fill`: `Clipper.Triangulate(PathsD, 6, out tris, useDelaunay: true)`, once per material region per new sketch. During a drag, that's every mouse move. Frame budget ~16 ms in total, so 1-2 ms for a typical part; ~90 ms (perforated on walrus.1) stuttered. The NodeFlow session keeps its own files out of frank's repos: copy anything it provides into this repo yourself.

## Gotchas

- Files edited from Git Bash or Python come out LF; the repo stores LF and git warns about CRLF on checkout. Harmless.
- `quick` with fewer than ~30 warm-up calls measures tier-0 JIT code; that's why it warms up for 400 ms.
- Without Delaunay, perforated can measure slower than with it: garbage collection lands differently. Look at allocations, not just times.
