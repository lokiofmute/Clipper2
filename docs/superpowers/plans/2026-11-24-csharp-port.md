# Clipper2 C# Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A pure C# Clipper2 (`USINGZ`) that matches this fork's C++ engine, packaged as `Clipper2` 2.1.0-walrus.1 for Walrus NodeFlow.

**Architecture:** Restore the C# tree deleted in `27a64a6`, retarget to net8.0, and compile one shared MSTest source set twice (plain and `USINGZ`), like the C++ suite. Then port each library-changing C++ commit after `27a64a6`: its C++ tests first (they fail), then its library diff. The C++ commit diff is the spec for each port; `Tests/*.txt` is shared test data.

**Tech Stack:** C# 12 / .NET 8 (SDK 10.0.404 installed), MSTest, `git show` for the C++ sources.

**Spec:** `docs/superpowers/specs/2026-11-24-csharp-port-design.md`

## Global Constraints

- Library target `net8.0`, `LangVersion` latest, `Nullable` enable; no native code, no P/Invoke.
- Namespace `Clipper2Lib`; the public API keeps the NuGet 2.0.0 signatures. Add no public overloads.
- Every test runs in both test projects (plain + `USINGZ`) unless it is Z-only; test names equal the C++ test names (`TestOffsetsSquareJoinKeepsZ`, …) so parity can be grepped.
- Skip `CLIPPER2_HI_PRECISION`-only code and tests, the C export API (`clipper.export.h`, `TestExportHeaders`, `TestWindows`), and the 7 build/CI-only commits.
- Do not modify anything under `CPP/`; after every task the C++ suite still passes (`ctest --test-dir CPP/build -C Release` → 194/194).
- To port a commit, read `git show <hash> -- CPP/Clipper2Lib CPP/Tests Tests`. C++ file → C# file: `clipper.core.h` → `Clipper.Core.cs`, `clipper.engine.*` → `Clipper.Engine.cs`, `clipper.offset.*` → `Clipper.Offset.cs`, `clipper.rectclip.*` → `Clipper.RectClip.cs`, `clipper.h` → `Clipper.cs`.
- Work on branch `csharp-port`; one commit per task, message ending with the `Co-Authored-By` line from the session.

## Review Focus

- Z with the high 32 bits set (NodeFlow encodes `(loop+1) << 32 | vertex`) must survive every op unchanged; nowhere may Z pass through an `int` or a `double`. → Task 3 test `TestZHighBitsSurviveBooleans`.
- With no ZCallback set, new crossing points get Z = 0 and input points keep theirs. → Task 3 test `TestZWithoutCallback`.
- Offsetting a **clockwise** outer with a hole (NodeFlow's hatching input) yields a positive outer and a negative hole. → Task 8 test `TestOffsetClockwiseOuterWithHoleWinding`.
- `Triangulate(PathsD, 6, …)` on mm-scale input far from the origin (differences > 2^30 after scaling) takes the BigInteger path and still terminates. → Task 10 test `TestTriangulateLargeCoordsUsesExactFallback`.
- A Clipper64 reused after `Clear()` with a ZCallback set gives the same paths and Z as a fresh one. → Task 7 test `TestReusedClipperMatchesFreshClipper` (Z variant asserts Z too).

---

### Task 1: Restore the C# tree, retarget, shared test projects

**Files:**
- Restore from `27a64a6^`: `CSharp/Clipper2Lib/*` (incl. `Clipper2.snk`, `Clipper.Triangulation.cs`), `CSharp/USINGZ/Clipper2Lib_Z.csproj`, `CSharp/Utils/ClipFileIO/*`, `CSharp/NuGet/*`, `CSharp/.editorconfig`, `CSharp/.gitignore`, the five old test `.cs` files.
- Create: `CSharp/Clipper2.sln`, `CSharp/Tests/Shared/` (old tests move here), `CSharp/Tests/Shared/TestData.cs`, `CSharp/Tests/Clipper2Lib.Tests/Clipper2Lib.Tests.csproj`, `CSharp/Tests/Clipper2Lib.Tests.Z/Clipper2Lib.Tests.Z.csproj`, `CSharp/Tests/Directory.Build.props`.
- Not restored: Benchmark, Examples, Utils/SVG, the old `Tests1`/`Tests2` csproj/sln files, `Clipper2Lib.sln`.

**Interfaces:**
- Produces: `TestData.Path(string fileName) : string`, the absolute path of `Tests/<fileName>`, found by walking up from `AppContext.BaseDirectory` to the directory that contains `Tests/Polygons.txt` (replaces the `..\\..\\..` paths). `Clipper2Lib.Tests.Z` defines `USINGZ` and references `Clipper2Lib_Z.csproj`; Z-only test files live in `CSharp/Tests/Shared/Z/` and only the Z project includes them.

- [ ] **Step 1:** `git switch -c csharp-port`, then `git checkout 27a64a6^ -- <paths above>`; `git mv` the test `.cs` files into `CSharp/Tests/Shared/` (`TestZCallback1.cs` into `Shared/Z/`).
- [ ] **Step 2:** Retarget `Clipper2Lib.csproj`, `Clipper2Lib_Z.csproj` and `Clipper.FileIO.csproj` to `net8.0`, `LangVersion latest`. Drop `GeneratePackageOnBuild` and the x86 platform (packaging is Task 11). Test projects: `net10.0`, MSTest current (`MSTest` 3.x metapackage); they include `../Shared/**/*.cs` (and `Shared/Z/**` only in Z). `Directory.Build.props` holds the shared test settings.
- [ ] **Step 3:** Replace every relative test-data path with `TestData.Path("Polygons.txt")` etc.
- [ ] **Step 4:** Build: `dotnet build CSharp/Clipper2.sln -c Release` → 0 errors. Fix only what the retarget breaks (e.g. the `HashCode.cs` shim colliding with `System.HashCode`: delete the shim if net8.0 has the API).
- [ ] **Step 5:** Run `dotnet test CSharp/Clipper2.sln -c Release`. Tests that fail because `Tests/*.txt` already holds post-`27a64a6` expectations (`f774daf`, `2b32367` changed `Polygons.txt`) get `[Ignore("Task 6")]` with a reason; any other failure is a restore bug, fix it. Expected: all non-ignored pass in both projects.
- [ ] **Step 6:** Commit `Restore the C# port on net8.0 with shared plain and Z test projects`.

### Task 2: Port the C++ tests that existed at the split (baseline parity)

**Files:**
- Create in `CSharp/Tests/Shared/`: `TestIsCollinear.cs`, `TestOffsetOrientation.cs`, `TestOrientation.cs`, `TestPolytreeHoles.cs`, `TestPolytreeIntersection.cs`, `TestPolytreeUnion.cs`, `TestRandomPaths.cs`, `TestRect.cs`, `TestRectClip.cs`, `TestSimplifyPath.cs`, `TestTrimCollinear.cs`; extend `TestLines.cs`, `TestOffset.cs`, `TestPolygons.cs`, `TestPolytree.cs` with C++ `TEST`s they lack.
- Modify: library files only where a test exposes pre-split drift.

**Interfaces:** Consumes `TestData.Path`. Source = `git show 27a64a6^:CPP/Tests/<file>.cpp` (the **split-time** versions; later tests come with their tasks).

- [ ] **Step 1:** Port each `TEST(...)` as an MSTest `[TestMethod]` with the same name; `#ifdef USINGZ` blocks become `#if USINGZ`; `EXPECT_*` → `Assert.*` with the same values.
- [ ] **Step 2:** Run both test projects. Every failure is drift between the C# and the split-time C++. For each, find the C++ code at `27a64a6^` the test exercises and mirror it in C#.
- [ ] **Step 3:** Run both projects → all pass (Task 1 ignores excepted). Commit `Port the split-time C++ tests to C# and fix the drift they expose`.

### Task 3: Engine fixes, scanline speed-ups, Z guards

Commits: `f97d65d`, `9b460cb`, `7813a71`, `b496b45`, `32b0a72` → `Clipper.Engine.cs`.

- [ ] **Step 1:** Port tests `TestPolyTreeCyclicSplits` (into `TestPolytreeIntersection.cs`) and `TestExecuteAfterZCallbackThrows` (into `Shared/Z/`; ZCallback throws `InvalidOperationException`; a second `Execute` on the same clipper must succeed with correct output). Add the Review Focus tests in `Shared/Z/TestZRetention.cs`:
  - `TestZHighBitsSurviveBooleans`: two overlapping squares at ±5_000_000 with Z = `(2L << 32) | i` (subject) and `(3L << 32) | i` (clip); for Union, Intersection, Difference and Xor, under both NonZero and EvenOdd, every output point equal to an input vertex has exactly that vertex's Z.
  - `TestZWithoutCallback`: same squares, no callback: crossing points have Z == 0, vertex points keep their Z.
- [ ] **Step 2:** Run them → `TestPolyTreeCyclicSplits` hangs or overflows the stack (set `[Timeout(10000)]`), and the throw test fails.
- [ ] **Step 3:** Port the five commits' library diffs. The speed-ups (`9b460cb`, `7813a71`, `b496b45`) change no results: the full suite must stay green.
- [ ] **Step 4:** Both projects green. Commit.

### Task 4: Offset per-path state, empty paths, Z on square joins

Commits: `6c7d083`, `ca30beb`, `f770e28` → `Clipper.Offset.cs`.

- [ ] **Step 1:** Port tests `TestOffsetsJoinedTwoPointPathOrder`, `TestOffsetsDegenerateGroupKeepsDeltaSign`, `TestOffsetsDeltaCallbackRoundPointOrder`, `TestOffsetsSinglePointDeltaCallbackNormals`, `TestOffsetsEmptyOpenPath`, `TestOffsetsEmptyGroupOrientation` (Shared) and `TestOffsetsSquareJoinKeepsZ` (Shared/Z). Run → fail.
- [ ] **Step 2:** Port the three diffs. Run both projects → green. Commit.

### Task 5: Exact rounding and products

Commits: `872bfa0`, `90f96cc` → `Clipper.Core.cs`.

- [ ] **Step 1:** Port `TestRoundToInt64` (new `TestRounding.cs`; the C++ rounding-mode loop has no .NET equivalent, so port only the fixed-value assertions), `TestCompareProducts` (into `TestIsCollinear.cs`) and `TestIntersectPointIsNotFused` (into `TestPolygons.cs`). Run → fail.
- [ ] **Step 2:** Mirror the C++ semantics, not its mechanism: `RoundToInt64` must give exactly the C++ results (check how C++ rounds ties against the test values and pick the matching `MidpointRounding`). Exact 128-bit product comparison uses `Math.BigMul` / `Int128`. `872bfa0` needs no code: .NET never contracts `a*b+c` into an FMA on its own; record that in a comment where C++ relies on it.
- [ ] **Step 3:** Green. Commit.

### Task 6: Intersection placement and edge ordering

Commits: `ec5f2d4`, `f774daf`, `2b32367` (non-HI_PRECISION parts only), `4a3bf64` → `Clipper.Engine.cs`, `Clipper.Core.cs`, `Clipper.RectClip.cs`.

- [ ] **Step 1:** Port `TestXorDivergingSharedEdges`, `TestDifferenceRoundsIntersections`, `TestDifferenceClampsIntersectionToSegment`, `TestIntersectInt32SpanBeyondIntMax`, `TestUnionKeepsIntersectionInScanbeam` (TestPolygons.cs) and `TestRectClipSharedEdge` (TestRectClip.cs). Skip `TestHiPrecisionIntersectNearSegmentEnd`. Remove every `[Ignore("Task 6")]` from Task 1. Run → fail.
- [ ] **Step 2:** Port the four diffs. Run both projects → green, including the full `Polygons.txt` set. Commit.

### Task 7: Clipper reuse (arena commit, observable part)

Commit: `0812644`. In C++ it moves engine objects into a per-clipper arena. Port only what changes behaviour: state reset on `Clear()` and reuse. Do not add an allocator; the C# GC plus the existing `PooledList` already cover allocation.

- [ ] **Step 1:** Port `TestReusedClipperMatchesFreshClipper` (Shared). Under `#if USINGZ`, also set a ZCallback that writes a marker and assert that the reused clipper's Z values equal the fresh clipper's. Run; if it already passes, record that in the commit message.
- [ ] **Step 2:** Port any reset/reuse logic from the diff that the C# lacks. Green. Commit.

### Task 8: Offset output with positive winding

Commit: `64a9ecc` (largest, +543 C++ lines) → `Clipper.Offset.cs`, `Clipper.cs`.

- [ ] **Step 1:** Port its 16 new tests (`TestOffsettingOrientation3`…`8`, `TestOffsettingStrayHoles`, `TestOffsettingStrayHoleDoesNotOrient`, `TestOffsettingStrayHoleChain`, `TestOffsettingHoleSizesCompareExactly`, `TestOffsettingZeroDeltaNormalizes`, `TestOffsetsOpenPathBesideClockwisePolygon`, `TestOffsetsReversedGroupSinglePoint`, `TestOffsetsDegeneratePathsFollowDeltaSign`, `TestOffsetsDegeneratePathCallbackKeepsGrowth`, `TestOffsetsInsignificantDeltaDropsOpenPaths`) and the commit's edits to existing tests in `TestOffsets.cpp`/`TestOffsetOrientation.cpp`. Add `TestOffsetClockwiseOuterWithHoleWinding`: a clockwise 0..1000 outer square with a counter-clockwise 400..600 hole, `InflatePaths(…, 10, JoinType.Miter, EndType.Polygon)` → exactly one path with `Area > 0` and one with `Area < 0`. Run → fail.
- [ ] **Step 2:** Port the diff. Green. Commit.

### Task 9: Y-stripe point-in-polygon accelerator

Commit: `884ba82` → new `CSharp/Clipper2Lib/Clipper.StripeIndex.cs` (internal), wired in wherever the C++ diff wires it; add the file to `Clipper2Lib_Z.csproj`'s linked list.

- [ ] **Step 1:** Port `TestStripeIndex.cpp` (all 15 tests, same names, classes `StripeIndexTests` and `StripeIndexRealWorld`) using `Tests/clipper_inflate.txt` and `Tests/clipper_subtract.txt`. The tests reach internals through `InternalsVisibleTo` both test assemblies. **Strong-naming:** the library is signed, so `InternalsVisibleTo` needs the test assemblies' public key: sign the test projects with the same `Clipper2.snk`. Run → fail (type missing).
- [ ] **Step 2:** Port the index and its use. Green. Commit.

### Task 10: Triangulation terminates (N972)

**Files:** Modify `CSharp/Clipper2Lib/Clipper.Triangulation.cs`. Create `CSharp/Tests/Shared/TestTriangulation.cs`.

**Interfaces:** `Clipper.Triangulate(PathsD paths, int precision, out PathsD triangles, bool useDelaunay = true) : TriangulateResult` is unchanged (and so are the `Paths64` overload and the enum).

- [ ] **Step 1:** Write `TestTriangulateN972Circle(bool useDelaunay)` with `[DataRow(true)]` and `[DataRow(false)]` and `[Timeout(10000)]`. Input: one `PathD` circle with centre (11.411, 28.589), radius 21.411, n = `(int)Math.Round(2 * Math.PI * 21.411 / 0.02306)` points at angles `2πi/n`. Call `Triangulate(paths, 6, out var tris, useDelaunay)`; assert `success`, `tris.Count == n - 2`, and the triangle areas sum to the polygon area within 1e-6 relative. Also add `TestTriangulateLargeCoordsUsesExactFallback`: the same circle centred at (5000, 5000) (differences > 2^30 after ×1e6), with `useDelaunay: true`, same asserts.
- [ ] **Step 2:** Run → the `true` cases time out. If they don't hang, message the NodeFlow session (`walrus-nodeflow-v2-90`) for its exact N972 points before going on, and use those.
- [ ] **Step 3:** Make `InCircleTest` exact: compute on the differences `p - d` as `long`; if every |difference| < 2^30, evaluate the determinant in `Int128` (worst case < 2^125, cannot overflow), otherwise in `System.Numerics.BigInteger`. Return the sign (`int`). Callers keep their sign logic. Use exact `CrossProductSign` from `Clipper.Core.cs` for the collinearity check if `Triangulation.cs` has its own double version.
- [ ] **Step 4:** Green (both projects; no Z assertions needed: triangles carry no Z). Commit `Make Delaunay in-circle test exact so ForceLegal terminates (N972)`.

### Task 11: Package, CI, docs

**Files:** Modify `CSharp/USINGZ/Clipper2Lib_Z.csproj`, `CSharp/Clipper2Lib/Clipper2Lib.csproj`, `README.md`; create `.github/workflows/actions_csharp.yml`, `CSharp/README.md`.

- [ ] **Step 1:** `Clipper2Lib_Z.csproj` → `PackageId Clipper2`, `AssemblyName Clipper2Lib`, `RootNamespace Clipper2Lib`, `Version 2.1.0-walrus.1`, signed with `../Clipper2Lib/Clipper2.snk`, the NuGet README/license/icon metadata moved over from `Clipper2Lib.csproj`, release notes listing the ported fork changes plus N972. `Clipper2Lib.csproj` (plain) → `IsPackable false`, so only one `Clipper2` package exists.
- [ ] **Step 2:** `dotnet pack CSharp/USINGZ/Clipper2Lib_Z.csproj -c Release -o CSharp/artifacts` → `CSharp/artifacts/Clipper2.2.1.0-walrus.1.nupkg`. Verify with `unzip -l` that it holds `lib/net8.0/Clipper2Lib.dll`, and that the assembly is strong-named and defines `USINGZ` (reflection: `Point64` has a field `Z`). Add `CSharp/artifacts/` to `CSharp/.gitignore`.
- [ ] **Step 3:** CI workflow: windows-latest + ubuntu-latest, `actions/setup-dotnet` 10.0.x, `dotnet test CSharp/Clipper2.sln -c Release`.
- [ ] **Step 4:** Docs: `CSharp/README.md` (what this is, build/test/pack commands, Z build only in the package, that Triangulate is fork-maintained with the N972 fix). In the root `README.md`, replace the line about the C# version living only in the archived repo with a pointer to `CSharp/`.
- [ ] **Step 5:** Final check: `dotnet test CSharp/Clipper2.sln -c Release` (both projects green, no `[Ignore]` left: `git grep -n "Ignore(" CSharp/Tests` → nothing) and `ctest --test-dir CPP/build -C Release` → 194/194. Commit.
- [ ] **Step 6:** Report to `walrus-nodeflow-v2-90`. Never re-pack a version NodeFlow has already restored: NuGet caches by id + version in `%USERPROFILE%\.nuget\packages\clipper2\<version>`, so each pack after a fix bumps the prerelease number (`walrus.2`, `walrus.3`, …). Include: package path, version, test counts, any API differences (expected: none beyond the winding change they already know about).
