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

`Clipper.Triangulate` is maintained by this fork only (the C++ removed it upstream). Its Delaunay in-circle test is exact (Int128, with a BigInteger fallback for large coordinates), which fixes a hang in `Delaunay.ForceLegal` on near-cocircular points (Walrus NodeFlow N972).
