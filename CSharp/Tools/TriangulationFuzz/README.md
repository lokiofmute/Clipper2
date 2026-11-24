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
| `selftest` | checks the checker: moves one triangle of each grid result by one unit (area and winding unchanged) and counts how many the coverage check catches; all must be caught |

Each input is triangulated with `useDelaunay` false and true, and each result is checked:

- the result is `success`, within 5 s (otherwise `HANG`),
- every triangle has 3 vertices and positive area,
- the triangle areas add up exactly (Int128) to the region's area,
- exact coverage, decided with integer predicates only: as 1-chains, the triangles' directed edges minus the region's (its EvenOdd union) cancel out, once every edge is split at every vertex lying on it. Then the triangles' winding numbers add up to the region's at every point, and since every triangle is positive, they cover the region exactly once. (Until 2026-11-24 this was an empty XOR of the triangles' union and the region, but the clipping engine drops slivers under a unit wide, which made grid 92063 and 100238 look wrong when they weren't.)
- with Delaunay, every edge shared by two triangles is locally Delaunay (exact BigInteger in-circle test).

The output lists the first 15 bad cases and a summary line. `fail results` counts honest `fail` returns. Any other bad result is a wrong triangulation or a hang, and must stay at 0.

Baseline on 2026-11-24 (commit `ae9133d`, before the exact coverage check; the same with it):

| Run | Result |
|-----|--------|
| `star 0 20000` | 39718 runs, 0 bad |
| `region 0 10000` | 20002 runs, 0 bad |
| `grid 0 50000` | 99592 runs, 4 bad, all `fail` (seeds 6407 and 27761, both modes) |

Wider runs with the exact coverage check, at commit `4f89ab8` (before the optimization work): `star 0 100000` and `region 0 50000` 0 bad; `grid 0 200000` 18 bad, all `fail` (9 seeds).

An honest `fail` can come from the input: the up-front Union rounds intersections to integers, which can leave a ring crossing itself by less than a unit (grid 70327: an edge passes 0.4 units beside a vertex and cuts the next edge). No exact triangulation exists then.

At `2.1.0-walrus.2` (commit `dbc1c4b`): `star 0 100000` and `region 0 50000` 0 bad; `grid 0 200000` 4 bad, all `fail` (seeds 70327 and 182727, both modes; see above). Grid 6407, 14230 and 27761 are tests now.

A `TRI_CHECK` build (`dotnet build -c Release -p:DefineConstants=TRI_CHECK -o <dir>`) uses the ear index for every ring of 4 vertices and more, and throws if an answer differs from the whole-ring scan. Run the fuzz families with it after changing the index.
