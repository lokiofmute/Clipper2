# Clipper2 for C# (Walrus fork)

A pure managed C# port of this repository's C++ Clipper2, kept in step with the fork's C++ engine. It started from the C# that upstream Clipper2 2.0.0 shipped, and adds the fork's engine and offset changes. On the shared test data (`Tests/Polygons.txt`, `Tests/Lines.txt`, and offsets of the same inputs) its output is byte-identical to the C++ library, Z values included.

No native code: the library targets `net8.0` and needs nothing else.

## Layout

| Path | What it is |
|------|------------|
| `Clipper2Lib/` | The library sources and `Clipper2Lib.csproj` (plain build, not packed) |
| `USINGZ/Clipper2Lib_Z.csproj` | The same sources built with `USINGZ`; this is the `Clipper2` NuGet package |
| `Tests/` | MSTest suites, see [Tests/README.md](Tests/README.md) |
| `Utils/ClipFileIO/` | Test-data loader, compiled into the test projects |
| `NuGet/` | Package readme, license and icon |

## Build, test, pack

```bash
dotnet test CSharp/Clipper2.sln -c Release
dotnet pack CSharp/USINGZ/Clipper2Lib_Z.csproj -c Release -o CSharp/artifacts
```

The package is `Clipper2` (assembly and namespace `Clipper2Lib`, strong-named with `Clipper2Lib/Clipper2.snk`), version `2.1.0-walrus.N`. It is always built with `USINGZ`: `Point64` and `PointD` carry a Z, and the clippers and `ClipperOffset` have a `ZCallback`. Only the Z build is packaged.

`CSharp/artifacts/` is a local feed folder and is not committed. NuGet caches packages by id and version, so bump the prerelease number (`walrus.2`, `walrus.3`, ...) for every pack that a consumer will restore again.

## Differences from upstream Clipper2 2.0.0

- The public API is that of 2.0.0 built with `USINGZ`; nothing else was added.
- Offset output is always wound positively (outers positive, holes negative; the other way round with `ReverseSolution`), whatever the input's orientation.
- Results follow the fork's C++ engine, which differs from 2.0.0 in edge cases (rounded intersections, stable ordering, exact predicates).

## Triangulation

`Clipper.Triangulate` is maintained by this fork only (the C++ removed it upstream). The sweep it inherited from 2.0.0 returned overlapping triangles on many polygons with collinear vertices, and could flip Delaunay edges forever (Walrus NodeFlow N972), so it was replaced:

1. The input is normalized with a `Union` (even-odd) into outers and holes, whatever their orientation. Paths that cross each other (beyond rounding) return `pathsIntersect`.
2. Rings that touch are made to share vertices, and are split where they pass a point twice.
3. Each outer is ear clipped with its holes bridged in, using exact (Int128) predicates.
4. The triangles' areas must add up exactly to the region's area. If they don't, the result is `fail`, never wrong triangles.
5. With `useDelaunay`, edges are flipped (Lawson) using an exact in-circle test (Int128, BigInteger for large coordinates), which always terminates.

Tested on 20,000 random polygons, 10,000 random regions with holes and ~50,000 grid-aligned CAD-like regions (each result checked for exact coverage and local Delaunay edges). A handful of the grid regions, with holes touching their outer at several points in a row, return `fail`.
