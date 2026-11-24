# TriangulationFuzz

A fuzz harness for `Clipper.Triangulate`. It isn't part of `Clipper2.sln` or the package.

```bash
dotnet run -c Release --project CSharp/Tools/TriangulationFuzz -- <family> <firstSeed> <lastSeed>
```

| Family | Inputs |
|--------|--------|
| `star` | random simple polygons, star-shaped around a centre, some far from the origin; self-intersecting ones are skipped |
| `region` | random unions/xors of rectangles, triangles and stars: several outers with holes |
| `grid` | CAD-like: unions, differences and xors of grid-aligned rectangles and diamonds, full of collinear runs, T-touches and pinches |
| `all` | all three families per seed |
| `review` | the two polygons from the 2026-11-24 branch review (seed range is ignored; use `0 -1`) |

Each input is triangulated with `useDelaunay` false and true, and each result is checked:

- the result is `success`, within 5 s (otherwise `HANG`),
- every triangle has 3 vertices and positive area,
- exact coverage: the XOR of the triangles' union and the region is empty, and the triangle areas add up exactly (Int128) to the region's area,
- with Delaunay, every edge shared by two triangles is locally Delaunay (exact BigInteger in-circle test).

The output lists the first 15 bad cases and a summary line. `fail results` counts honest `fail` returns. Any other bad result is a wrong triangulation or a hang, and must stay at 0.

Baseline on 2026-11-24 (commit `ae9133d`):

| Run | Result |
|-----|--------|
| `star 0 20000` | 39718 runs, 0 bad |
| `region 0 10000` | 20002 runs, 0 bad |
| `grid 0 50000` | 99592 runs, 4 bad, all `fail` (seeds 6407 and 27761, both modes) |
