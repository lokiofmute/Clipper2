using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  // Triangulate on shapes other than circles, each checked by CheckTriangulation:
  // (a) success, and every triangle wound positively with non-zero area,
  // (b) the triangle areas add up to the region's area (outers minus holes),
  // (c) with Delaunay, every edge shared by two triangles is locally Delaunay,
  // (d) a time limit, so a flip cycle fails the case instead of hanging the run.
  [TestClass]
  public class TestTriangulationShapes
  {
    private const int Precision = 6;
    private const double Scale = 1e6;

    private static Point64 ToInt(PointD pt) =>
      new((long) Math.Round(pt.x * Scale), (long) Math.Round(pt.y * Scale));

    private static void CheckTriangulation(PathsD region, bool useDelaunay, string name)
    {
      PathsD tris = new();
      TriangulateResult result = TriangulateResult.fail;
      Task run = Task.Run(() => result = Clipper.Triangulate(region, Precision, out tris, useDelaunay));
      Assert.IsTrue(run.Wait(TimeSpan.FromSeconds(5)), $"{name}: no result within 5 s (useDelaunay {useDelaunay})");
      Assert.AreEqual(TriangulateResult.success, result, name);
      Assert.AreNotEqual(0, tris.Count, name);

      // the triangles, back in the integer space Triangulate worked in
      List<Point64[]> triangles = new();
      Int128 twiceTriangleArea = 0;
      foreach (PathD tri in tris)
      {
        Assert.AreEqual(3, tri.Count, name);
        Point64[] t = { ToInt(tri[0]), ToInt(tri[1]), ToInt(tri[2]) };
        // (a) wound positively, with area
        Assert.AreEqual(1, InternalClipper.CrossProductSign(t[0], t[1], t[2]), $"{name}: triangle winding/area");
        twiceTriangleArea += TwiceArea(t);
        triangles.Add(t);
      }

      // (b) exact area, in the integer space
      Int128 twiceRegionArea = 0;
      foreach (PathD path in region)
      {
        Path64 p = new();
        foreach (PointD pt in path) p.Add(ToInt(pt));
        twiceRegionArea += TwiceArea(p.ToArray());
      }
      Assert.AreEqual(Int128.Abs(twiceRegionArea), twiceTriangleArea, $"{name}: triangle areas vs region area");

      // (b2) the triangles cover exactly the region: with equal total areas, an empty XOR of
      // their union and the region also rules out an overlap balanced by a gap
      Paths64 triPaths = new(), regionPaths = new();
      foreach (Point64[] t in triangles) triPaths.Add(new Path64(t));
      foreach (PathD path in region)
      {
        Path64 p = new();
        foreach (PointD pt in path) p.Add(ToInt(pt));
        regionPaths.Add(p);
      }
      Paths64 covered = Clipper.Union(triPaths, FillRule.NonZero);
      Assert.AreEqual(0.0, Clipper.Area(Clipper.Xor(covered, regionPaths, FillRule.EvenOdd)), $"{name}: triangles vs region");

      if (!useDelaunay) return;

      // (c) each interior edge (shared by two triangles) is locally Delaunay: the far vertex
      // of one triangle is not inside the other's circumcircle
      Dictionary<(Point64, Point64), Point64> opposite = new();
      foreach (Point64[] t in triangles)
        for (int i = 0; i < 3; i++)
        {
          Point64 u = t[i], v = t[(i + 1) % 3], w = t[(i + 2) % 3];
          if (opposite.TryGetValue((v, u), out Point64 b))
          {
            // (u, v, w) is counter-clockwise, so InCircleSign > 0 means b is strictly inside
            Assert.IsTrue(Delaunay.InCircleSign(u, v, w, b) <= 0,
              $"{name}: edge ({u.X},{u.Y})-({v.X},{v.Y}) is not locally Delaunay");
            opposite.Remove((v, u));
          }
          else opposite[(u, v)] = w;
        }
    }

    private static Int128 TwiceArea(Point64[] p)
    {
      Int128 sum = 0;
      for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
        sum += (Int128) p[j].X * p[i].Y - (Int128) p[i].X * p[j].Y;
      return sum;
    }

    private static PathD Poly(params double[] xy) => Clipper.MakePath(xy);

    private static PathD Rect(double l, double t, double r, double b, bool clockwise)
    {
      PathD p = Poly(l, t, r, t, r, b, l, b);
      if (clockwise) p.Reverse();
      return p;
    }

    private static void CheckBoth(PathsD region, string name)
    {
      CheckTriangulation(region, true, name);
      CheckTriangulation(region, false, name);
    }

    [TestMethod]
    public void TestTriangulateGridStaircase()
    {
      // every vertex on a unit grid, so many quads are exactly cocircular (the no-flip tie)
      PathD p = new();
      const int steps = 25;
      for (int i = 0; i < steps; i++)
      {
        p.Add(new PointD(i, i));
        p.Add(new PointD(i + 1, i));
      }
      p.Add(new PointD(steps, steps));
      p.Add(new PointD(0, steps));
      CheckBoth(new PathsD { p }, "staircase");

      // a rectangle with a vertex at every grid point of its sides
      PathD grid = new();
      for (int x = 0; x < 20; x++) grid.Add(new PointD(x * 0.5, 0));
      for (int y = 0; y < 20; y++) grid.Add(new PointD(10, y * 0.5));
      for (int x = 20; x > 0; x--) grid.Add(new PointD(x * 0.5, 10));
      for (int y = 20; y > 0; y--) grid.Add(new PointD(0, y * 0.5));
      CheckBoth(new PathsD { grid }, "grid rectangle");
    }

    [TestMethod]
    public void TestTriangulateWithHoles()
    {
      PathsD region = new()
      {
        Rect(0, 0, 100, 60, false),
        Rect(10, 10, 20, 20, true),
        Rect(40, 25, 60, 35, true),
        Rect(85, 45, 90, 50, true), // near the outer's corner
        Poly(70, 10, 75, 20, 80, 10) // a triangular hole
      };
      region[4].Reverse();
      if (Clipper.IsPositive(region[4]) == Clipper.IsPositive(region[0])) region[4].Reverse();
      CheckBoth(region, "holes");
    }

    [TestMethod]
    public void TestTriangulateThinSliver()
    {
      CheckBoth(new PathsD { Poly(0, 0, 1000, 0.001, 1000, 0.003, 0, 0.002) }, "sliver");
      CheckBoth(new PathsD { Poly(0, 0, 500, 0.000001, 1000, 0, 500, 0.000003) }, "sliver diamond");
    }

    [TestMethod]
    public void TestTriangulateStarAndComb()
    {
      PathD star = new();
      for (int i = 0; i < 64; i++)
      {
        double a = Math.PI * i / 32, r = (i % 2 == 0) ? 50 : 12;
        star.Add(new PointD(Math.Round(5000 + r * Math.Cos(a), 6), Math.Round(-3000 + r * Math.Sin(a), 6)));
      }
      CheckBoth(new PathsD { star }, "star");

      PathD comb = new() { new PointD(0, 0) };
      for (int i = 0; i < 40; i++)
      {
        comb.Add(new PointD(i * 2 + 1, 0));
        comb.Add(new PointD(i * 2 + 1, -30));
        comb.Add(new PointD(i * 2 + 2, -30));
        comb.Add(new PointD(i * 2 + 2, 0));
      }
      comb.Add(new PointD(81, 0));
      comb.Add(new PointD(81, 5));
      comb.Add(new PointD(0, 5));
      CheckBoth(new PathsD { comb }, "comb");
    }

    [TestMethod]
    public void TestTriangulateInnerLocMinVisibility()
    {
      // minimized from random polygons: the loose edge from an inner local minimum went to a
      // vertex hidden behind an existing loose edge, which gave overlapping triangles
      CheckBoth(new PathsD { Poly(21, 25, 0, 0, 18, 20, 13, 8, 19, 12, 47, 9, 63, 22) }, "case 1979");
      CheckBoth(new PathsD { Poly(8.5, 38, 0, 18.5, 7, 34, 1, 0.5, 8, 30, 19, 27.5, 46, 37) }, "case 185");
      CheckBoth(new PathsD { Poly(17.7, 28, 0, 0.4, 18.3, 26.9, 18.1, 26.4, 16.6, 22.8, 18.2, 24.4, 47, 23.1, 41.3, 26) }, "case 1328");
      CheckBoth(new PathsD { Poly(0, 24, 7, 14, 17, 32, 15, 27, 7, 0, 16, 21, 37, 18, 20, 34) }, "case 578");
    }

    // seeded random unions (or xors) of rectangles, triangles and stars: several outers with
    // holes, as Clipper produces them
    private static PathsD RandomRegion(int seed)
    {
      Random rng = new(seed);
      PathsD shapes = new();
      int k = rng.Next(2, 9);
      double ox = (rng.NextDouble() - 0.5) * (seed % 4 == 0 ? 20000 : 50);
      double oy = (rng.NextDouble() - 0.5) * (seed % 4 == 0 ? 20000 : 50);
      for (int s = 0; s < k; s++)
      {
        double cx = ox + rng.NextDouble() * 60, cy = oy + rng.NextDouble() * 60;
        int kind = rng.Next(3);
        PathD p = new();
        if (kind == 0)
        {
          double w = 2 + rng.NextDouble() * 30, h = 2 + rng.NextDouble() * 30;
          p.Add(new PointD(cx, cy)); p.Add(new PointD(cx + w, cy));
          p.Add(new PointD(cx + w, cy + h)); p.Add(new PointD(cx, cy + h));
        }
        else
        {
          int n = kind == 1 ? 3 : rng.Next(5, 40);
          double[] ang = new double[n];
          for (int i = 0; i < n; i++) ang[i] = (i + rng.NextDouble() * 0.8) * 2 * Math.PI / n;
          for (int i = 0; i < n; i++)
          {
            double r = 3 + rng.NextDouble() * 25;
            p.Add(new PointD(cx + r * Math.Cos(ang[i]), cy + r * Math.Sin(ang[i])));
          }
        }
        for (int i = 0; i < p.Count; i++) p[i] = new PointD(Math.Round(p[i].x, 3), Math.Round(p[i].y, 3));
        shapes.Add(p);
      }
      return Clipper.BooleanOp(rng.Next(3) == 0 ? ClipType.Xor : ClipType.Union, shapes, new PathsD(), FillRule.NonZero, 6);
    }

    [TestMethod]
    public void TestTriangulateRandomRegions()
    {
      // seed 2094 linked an inner local minimum straight through a vertex on the link
      int holes = 0;
      foreach (int seed in new[] { 2094 }.Concat(Enumerable.Range(0, 400)))
      {
        PathsD region = RandomRegion(seed);
        if (region.Count == 0) continue;
        foreach (PathD p in region) if (Clipper.Area(p) < 0) holes++;
        CheckBoth(region, "region seed " + seed);
      }
      Assert.IsTrue(holes > 100, "holes " + holes);
    }

    [TestMethod]
    public void TestTriangulateRandomPolygons()
    {
      // seeded random star-shaped (so simple) polygons, some far from the origin
      for (int seed = 0; seed < 200; seed++)
      {
        Random rng = new(seed);
        int n = rng.Next(3, 60);
        double cx = (rng.NextDouble() - 0.5) * (seed % 3 == 0 ? 10000 : 100);
        double cy = (rng.NextDouble() - 0.5) * (seed % 3 == 0 ? 10000 : 100);
        double[] angles = new double[n];
        for (int i = 0; i < n; i++) angles[i] = rng.NextDouble() * 2 * Math.PI;
        Array.Sort(angles);
        PathD p = new(n);
        for (int i = 0; i < n; i++)
        {
          double r = 1 + rng.NextDouble() * 40;
          p.Add(new PointD(Math.Round(cx + r * Math.Cos(angles[i]), 6), Math.Round(cy + r * Math.Sin(angles[i]), 6)));
        }
        if (Math.Abs(Clipper.Area(p)) < 1e-3) continue;
        // only simple polygons (consecutive angles over pi apart make the path cross itself)
        PathsD u = Clipper.Union(new PathsD { p }, new PathsD(), FillRule.EvenOdd, 6);
        if (u.Count != 1 || u[0].Count != p.Count) continue;
        CheckBoth(new PathsD { p }, "seed " + seed);
      }
    }
  }
}
