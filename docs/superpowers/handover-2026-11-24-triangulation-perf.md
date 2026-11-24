# Handover: benchmark and optimize Clipper.Triangulate (2026-11-24)

From session `clipper2-6b`, which finished the C# port (merged to `main` at `ae9133d`, package `Clipper2 2.1.0-walrus.1`, wired into NodeFlow), to the session that benchmarks and optimizes the triangulator. frank decided on this work; he wants to get to the bottom of it.

## Scope

Only `CSharp/Clipper2Lib/Clipper.Triangulation.cs` (736 lines). Upstream removed triangulation from the C++ (`1d16960`), so this module is ours alone. It doesn't need to mirror any C++ code, and can be restructured freely.

The rest of the library is out of scope: the clipping engine and offsets must keep their byte-identical parity with the fork's C++ (see "Other tools" below). Arena allocation, Span overloads and similar ideas in `CSharp/TODO.txt` were discussed and set aside: their gain is small or unproven, and they would make porting future C++ commits harder.

## Constraints

- Public API unchanged: `Clipper.Triangulate(Paths64, out Paths64, bool useDelaunay = true)`, `Clipper.Triangulate(PathsD, int decPlaces, out PathsD, bool useDelaunay = true)` and `enum TriangulateResult { success, fail, noPolygons, pathsIntersect }`.
- Correctness contract, which must keep holding:
  - `success` only when the triangles cover the region exactly (otherwise `fail`, never wrong triangles),
  - every triangle has positive area,
  - with `useDelaunay`, every interior edge is locally Delaunay,
  - always terminates.
- Tests stay green in both projects: `dotnet test CSharp/Clipper2.sln -c Release`. At handover: 113 plain, 121 USINGZ.
- Commit once per step on a feature branch (not on `main`), ending with the Co-Authored-By line from the session's system reminder.
- NodeFlow (session `walrus-nodeflow-v2-90`, ticket N974) consumes the package. When a new package is needed, bump to `2.1.0-walrus.2` (NuGet caches by id and version) and tell that session the feed path `E:\git\Walrus\Clipper2\CSharp\artifacts` and the exact version.

## How the triangulator works now

`Delaunay.Execute` (line 43):

1. It strips duplicate points and drops paths without area. A `Clipper64` Union (EvenOdd, `PreserveCollinear`) then builds a PolyTree, which gives outers and holes whatever the input's orientation.
2. `HasCrossings` (113) returns `pathsIntersect` when the union created a vertex more than 1 unit from every input vertex.
3. For each outer, with its holes (islands are separate outers), it calls `EarClip` (253):
   - `Conform` (162) inserts into each edge the vertices lying strictly inside it (T-touches).
   - `Decompose` (210) splits rings at repeated points into simple loops. Positive loops are outers, negative loops holes. Each hole goes to the smallest outer around it (`HoleInside`, 239, point-in-polygon).
   - `EarClipSimple` (290) bridges each hole, from left to right, to the nearest visible vertex (`FindBridge` 443, `SegmentBlocked` 408, `LocallyInside` 397, `SplitRing` 473).
   - `SplitPinches` (342) splits pieces that only touch at a point.
   - `ClipEars` (589) clips ears. `IsEar` (486) uses exact predicates, rejects edges crossing the diagonal, and handles edges entering a corner from a twin vertex (`EntersCorner` 520). `RemoveDegenerate` (577) runs after each clip, and `FilterRing` (538) runs when a full pass finds no ear.
4. It checks that the triangles' twice-areas add up exactly (Int128) to the region's twice-area; otherwise it returns `fail`.
5. With `useDelaunay`, `Legalize` (630) does Lawson flips with the exact `InCircleSign` (711): Int128, or BigInteger when differences reach 2^30.

All predicates are exact: `InternalClipper.CrossProductSign` (Int128 compare) and `InCircleSign`.

## Where the time likely goes (unmeasured, from reading the code)

1. **Hole bridging, the biggest risk.** For each hole, `FindBridge` builds and sorts a list of every vertex on the outer ring (which grows as holes merge into it). `SegmentBlocked` then scans every edge of every ring for each candidate. That is roughly holes × vertices × log, which would be seconds on, say, a plate with 500 round holes of 64 points. A likely fix: an exact version of earcut's ray-cast-left bridge (Eberly's method: the nearest edge hit by a leftward ray, then the reflex vertices inside the triangle), plus a spatial grid for blocking tests.
2. **The ear test is O(n) per candidate.** `IsEar` walks the whole remaining ring with a bounding-box check, so a 5,834-point circle makes ~17M node visits. It also checks every vertex, not just reflex ones (earcut's shortcut was dropped for pinch safety). Possible fixes: a z-order index (as in earcut); restoring the reflex-only check for rings without twin vertices.
3. **Exact predicates in inner loops.** Every orientation test is an Int128 multiply, and the in-circle test is a full Int128 determinant. A floating-point filter with an error bound, with the exact fallback, is typically 3-5x faster on predicate-heavy code.
4. **Allocations in `Legalize`.** It uses tuple-keyed dictionaries and a HashSet, and allocates new arrays on every flip. A flat triangle array with neighbour links (as in the delaunator library), updated in O(1) per flip, would avoid that.
5. **Constant factors.**
   - The ear clipper allocates a `Node` class per vertex; it could use index arrays instead.
   - `Conform` scans every vertex in an edge's x-range, which is quadratic for long edges with many holes.
   - The near-vertex fallback in `HasCrossings` is brute force; it could be a 3×3 cell lookup.
   - The up-front Union re-runs the whole clipping engine even on input that is already clean; measure before changing.
6. **Robustness to fix along the way.** Grid fuzz seeds 6407 and 27761 return `fail`: holes touch their outer at several points in a row, and bridging plus pinches tangle. A cleaner way of attaching touching holes may fix both.

## Plan

1. **Benchmark first.** Add a BenchmarkDotNet project, for example `CSharp/Benchmarks/TriangulationBench`, outside `Clipper2.sln`, with `[MemoryDiagnoser]`. Include:
   - the N972 ring (68 points; `N972Ring` in `CSharp/Tests/Shared/TestTriangulation.cs`),
   - the dense circle (5834 points),
   - a plate with many round holes (e.g. 100 and 500 holes of 64 points),
   - a large concave star and comb (10k points),
   - grid CAD regions,
   - real inputs from NodeFlow: ask `walrus-nodeflow-v2-90` to capture a few representative `PathsD` it triangulates (sketch fills, hatching regions, anything large), with useDelaunay and precision. Ask, don't guess.
   Record a baseline for each, separately for `useDelaunay` false and true, and profile where the time goes (dotnet-trace or the VS profiler) before choosing an order.
2. **Gate every step** with the test suite and the fuzz harness. `CSharp/Tools/TriangulationFuzz` checks every result for exact coverage, positive triangles, local Delaunay edges and a 5 s limit; see its README for usage and the baseline (star 0..20000 and region 0..10000: 0 bad; grid 0..50000: only 2 seeds return `fail`). Wrong results and hangs must stay at 0, and `fail` must not increase. Add every new failure found as a test in `CSharp/Tests/Shared/TestTriangulationShapes.cs`, whose `CheckTriangulation` already does these checks.
3. **Probable order** (re-rank after profiling): bridging (1), ear index (2), flat Delaunay structure (4), filtered predicates (3), allocations (5). Measure after each step and record the numbers in the commit message.
4. **Ship.** Re-pack as `2.1.0-walrus.2` and hand off to NodeFlow (feed path, version, before/after numbers).

## Other tools

These live in `.superpowers/sdd/2026-11-24-csharp-port/`. That folder is git-ignored but present on this machine.

- `oracle/` holds C++ dumpers of the fork's library (booleans and offsets, plain and USINGZ), built with `oracle/build.bat` against `CPP/build/Release/*.lib`.
- `cmp.sh` (211 Boolean cases) and `cmpoffs.sh` (19,500 offset cases) prove byte-identical output from the C# engine. They are only needed if something outside the triangulator changes.
- `progress.md` is the port's ledger, with every ruling.

A useful debugging technique: copy `Clipper.Triangulation.cs` into a scratch console project, change the namespace, make `Delaunay` public, and add logging (ears clipped, bridges chosen, pieces). Minimize failing inputs by deleting vertices while the input stays simple and still fails, then coarsening its coordinates.
