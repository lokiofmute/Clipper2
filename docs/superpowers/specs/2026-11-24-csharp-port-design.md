# Clipper2 C# port of the fork's engine changes — design

Date: 2026-11-24. Agreed between frank, this repo's session, and the Walrus NodeFlow V2 session (ticket N974; Delaunay hang = N972).

## Goal

A pure managed C# Clipper2 (no native DLL) that matches this fork's C++ engine, built with `USINGZ`, to replace the NuGet `Clipper2` 2.0.0 in NodeFlow.

## Approach

Restore the C# tree deleted in `27a64a6` ("Drop C# and Delphi ports"), retarget it to modern .NET, port the C++ test suite to MSTest, then port the 18 library-changing C++ commits after `27a64a6` (the 7 build/CI-only commits are skipped). The C++ tests and the shared data in `Tests/*.txt` are the oracle: a C# behaviour is correct when the ported C++ test passes.

## Requirements

1. **Z through Booleans** (Union, Intersection, Difference, Xor; NonZero and EvenOdd): input points keep their Z exactly. Same rules as C++ `ClipperBase::SetZ`: an output point equal to an input vertex gets that vertex's Z (subject before clip); the `ZCallback64` gets `(bot1, top1, bot2, top2, ref ip)` with the subject edge first.
2. **Z through offsets**: every point a join makes (round, miter, square) takes the Z of the source vertex, as in the current `clipper.offset.cpp`. In the internal union, new crossings take a shared non-zero Z of the endpoints, otherwise go to `ClipperOffset.ZCallback`.
3. **API equal to upstream 2.0.0**: namespace `Clipper2Lib`; same public signatures; no new overloads. Behaviour changes adopted from the C++ fork, e.g. offset output always wound positively (`64a9ecc`), are intended.
4. **Triangulation stays** (upstream C++ deleted it in `1d16960`), as this fork's own module. Fix N972: `Delaunay.ForceLegal` must terminate. The in-circle test becomes exact (Int128, with a BigInteger fallback when the operands are too large for Int128). NodeFlow calls `Clipper.Triangulate(PathsD, 6, out triangles, useDelaunay: false)`; triangles need no Z.
5. **Package**: id `Clipper2`, assembly `Clipper2Lib`, version `2.1.0-walrus.1`, built with `USINGZ`, strong-named with the original `Clipper2.snk`. Produced by `dotnet pack`; how NodeFlow consumes it is frank's call later, so nothing is wired into NodeFlow.
6. **Target**: `net8.0` (NodeFlow is `net10.0`; needs `Int128`, `Math.FusedMultiplyAdd`, `Math.BigMul`).

## Out of scope

C# examples and benchmark projects, Delphi, `CLIPPER2_HI_PRECISION` (the C# never had it), the C export/DLL API, C++ allocation details (the arena in `0812644`) beyond their observable behaviour.
