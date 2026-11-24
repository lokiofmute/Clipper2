using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  // Triangulation is this fork's own C# module (the C++ removed it in 1d16960).
  [TestClass]
  public class TestTriangulation
  {
    private static PathD Circle(double cx, double cy, double radius, int n)
    {
      PathD path = new(n);
      for (int i = 0; i < n; i++)
      {
        double a = 2 * Math.PI * i / n;
        path.Add(new PointD(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
      }
      return path;
    }

    private static void CheckTriangulation(PathD circle, bool useDelaunay)
    {
      PathsD paths = new() { circle };
      TriangulateResult result = Clipper.Triangulate(paths, 6, out PathsD tris, useDelaunay);
      Assert.AreEqual(TriangulateResult.success, result);
      // a convex polygon has n - 2 triangles, less any vertex that rounds onto its neighbours' line
      int n = circle.Count;
      Assert.IsTrue(tris.Count <= n - 2 && tris.Count >= n - 2 - n / 50,
        $"{tris.Count} triangles for {n} vertices");
      double polygonArea = Math.Abs(Clipper.Area(circle));
      double triangleArea = 0;
      foreach (PathD tri in tris)
      {
        Assert.AreEqual(3, tri.Count);
        triangleArea += Math.Abs(Clipper.Area(tri));
      }
      Assert.AreEqual(polygonArea, triangleArea, 1e-6 * polygonArea);
    }

    // NodeFlow's N972 input: a sketch preview's circle (centre 11.411, 28.589, radius 21.411)
    // sampled to 68 points, exactly as NodeFlow passes it
    private static readonly double[] N972Ring =
    {
        32.8219997882843, 28.58900010585785, 32.73066423738885, 30.56455794055928, 32.45743682741875, 32.523261012377105,
        32.00464863829859, 34.448398357321835, 31.37616269920931, 36.323545382350716, 30.57734103058533, 38.132703994204405,
        29.614998897246764, 39.86043908949794, 28.497346662962972, 41.4920102415805, 27.23391974252379, 43.013497460658314,
        25.835497248942616, 44.41191995423949, 24.314010029864804, 45.67534687467867, 22.68243887778224, 46.79299910896246,
        20.954703782488707, 47.75534124230103, 19.145545170635017, 48.55416291092501, 17.270398145606144, 49.18264885001429,
        15.345260800661414, 49.63543703913445, 13.38655772884358, 49.90866444910455, 11.410999894142153, 50,
        9.435442059440724, 49.908664449104556, 7.476738987622891, 49.63543703913445, 5.551601642678159, 49.18264885001429,
        3.676454617649285, 48.55416291092501, 1.8672960057955947, 47.75534124230103, 0.139560910502059, 46.79299910896246,
        -1.4920102415805, 45.675346874678674, -3.0134974606583143, 44.41191995423949, -4.411919954239485, 43.013497460658314,
        -5.67534687467867, 41.4920102415805, -6.792999108962459, 39.860439089497945, -7.755341242301025, 38.132703994204405,
        -8.554162910925005, 36.32354538235072, -9.182648850014285, 34.44839835732184, -9.635437039134452, 32.523261012377105,
        -9.908664449104553, 30.564557940559283, -10, 28.589000105857853, -9.908664449104556, 26.61344227115643,
        -9.635437039134452, 24.654739199338596, -9.182648850014285, 22.72960185439387, -8.554162910925005, 20.85445482936498,
        -7.755341242301032, 19.045296217511304, -6.792999108962459, 17.31756112221776, -5.675346874678674, 15.685989970135202,
        -4.411919954239487, 14.164502751057384, -3.013497460658318, 12.766080257476217, -1.4920102415805037, 11.502653337037032,
        0.1395609105020572, 10.38500110275324, 1.8672960057955983, 9.422658969414666, 3.6764546176492843, 8.62383730079069,
        5.551601642678155, 7.995351361701413, 7.4767389876228805, 7.54256317258125, 9.435442059440724, 7.269335762611142,
        11.410999894142147, 7.178000211715698, 13.38655772884357, 7.269335762611142, 15.345260800661414, 7.5425631725812465,
        17.27039814560614, 7.995351361701413, 19.14554517063501, 8.62383730079069, 20.954703782488696, 9.422658969414666,
        22.68243887778224, 10.38500110275324, 24.314010029864797, 11.502653337037025, 25.83549724894262, 12.766080257476217,
        27.23391974252378, 14.164502751057373, 28.49734666296297, 15.685989970135195, 29.614998897246757, 17.317561122217754,
        30.577341030585334, 19.045296217511297, 31.37616269920931, 20.854454829364983, 32.00464863829858, 22.729601854393852,
        32.457436827418746, 24.65473919933858, 32.73066423738886, 26.61344227115642
    };

    [TestMethod]
    [Timeout(10000)]
    [DataRow(true)]
    [DataRow(false)]
    public void TestTriangulateN972Ring(bool useDelaunay)
    {
      // with useDelaunay this never returned: the in-circle test, evaluated in double, is
      // rounding noise for these near-cocircular points, so ForceLegal flipped an edge back
      // and forth while its pending stack grew without bound
      PathD ring = Clipper.MakePath(N972Ring);
      Assert.AreEqual(68, ring.Count);
      PathsD paths = new() { ring };
      Assert.AreEqual(TriangulateResult.success, Clipper.Triangulate(paths, 6, out PathsD tris, useDelaunay));
      Assert.AreEqual(66, tris.Count);
      double triangleArea = 0;
      foreach (PathD tri in tris) triangleArea += Math.Abs(Clipper.Area(tri));
      double ringArea = Math.Abs(Clipper.Area(ring));
      Assert.AreEqual(ringArea, triangleArea, 1e-6 * ringArea);
    }

    [TestMethod]
    [Timeout(10000)]
    [DataRow(true)]
    [DataRow(false)]
    public void TestTriangulateDenseCircle(bool useDelaunay)
    {
      // the same circle sampled every 0.02306 mm (5834 points)
      int n = (int) Math.Round(2 * Math.PI * 21.411 / 0.02306);
      CheckTriangulation(Circle(11.411, 28.589, 21.411, n), useDelaunay);
    }

    [TestMethod]
    [Timeout(20000)]
    public void TestTriangulateLargeCoordsUsesExactFallback()
    {
      // a 1000 mm radius at 6 decimal places gives in-circle differences up to 2e9 > 2^30,
      // which the exact predicate evaluates with BigInteger rather than Int128
      CheckTriangulation(Circle(5000, 5000, 1000, 4000), true);
    }

    private static int InCircleSignReference(Point64 a, Point64 b, Point64 c, Point64 d)
    {
      BigInteger m00 = a.X - (BigInteger) d.X, m01 = a.Y - (BigInteger) d.Y;
      BigInteger m10 = b.X - (BigInteger) d.X, m11 = b.Y - (BigInteger) d.Y;
      BigInteger m20 = c.X - (BigInteger) d.X, m21 = c.Y - (BigInteger) d.Y;
      BigInteger m02 = m00 * m00 + m01 * m01, m12 = m10 * m10 + m11 * m11, m22 = m20 * m20 + m21 * m21;
      BigInteger det = m00 * (m11 * m22 - m21 * m12) - m10 * (m01 * m22 - m21 * m02) + m20 * (m01 * m12 - m11 * m02);
      return det.Sign;
    }

    [TestMethod]
    public void TestInCircleSignFilterAgreesWithExact()
    {
      // InCircleSign decides in double when its error bound allows: it must never give a sign
      // the exact determinant doesn't. Random points at every scale (Int128 and BigInteger
      // ranges), and cocircular points (Pythagorean triples on one radius, far from the
      // origin), exactly and nudged by one unit, where the double determinant is rounding noise.
      Random rng = new(20261124);
      long[] scales = { 10, 1000, 1_000_000, 1L << 30, 1L << 40, 1L << 52, 1L << 59 };
      foreach (long scale in scales)
        for (int k = 0; k < 3000; k++)
        {
          Point64 a = RandomPoint(rng, scale), b = RandomPoint(rng, scale);
          Point64 c = RandomPoint(rng, scale), d = RandomPoint(rng, scale);
          Assert.AreEqual(InCircleSignReference(a, b, c, d), Delaunay.InCircleSign(a, b, c, d),
            $"random scale {scale}");
        }

      // every (x, y) with x^2 + y^2 = R^2 from these triples, R = 5 * 13 * 17 * 25 * 29
      long r = 5L * 13 * 17 * 25 * 29;
      List<(long, long)> circle = new();
      foreach ((long p, long q, long h) in new[] { (3L, 4L, 5L), (5L, 12L, 13L), (8L, 15L, 17L), (7L, 24L, 25L), (20L, 21L, 29L) })
        foreach (long sx in new[] { -1L, 1L })
          foreach (long sy in new[] { -1L, 1L })
          {
            circle.Add((sx * p * (r / h), sy * q * (r / h)));
            circle.Add((sx * q * (r / h), sy * p * (r / h)));
          }
      foreach (long scale in new long[] { 1, 1000, 100_000, 10_000_000 })
        for (int k = 0; k < 3000; k++)
        {
          Point64 offset = RandomPoint(rng, 1L << 50);
          Point64 OnCircle(int i) => new(offset.X + circle[i].Item1 * scale, offset.Y + circle[i].Item2 * scale);
          int i0 = rng.Next(circle.Count), i1 = rng.Next(circle.Count), i2 = rng.Next(circle.Count), i3 = rng.Next(circle.Count);
          Point64 a = OnCircle(i0), b = OnCircle(i1), c = OnCircle(i2), d = OnCircle(i3);
          Assert.AreEqual(InCircleSignReference(a, b, c, d), Delaunay.InCircleSign(a, b, c, d), $"cocircular scale {scale}");
          Point64 nudged = new(d.X + rng.Next(-1, 2), d.Y + rng.Next(-1, 2));
          Assert.AreEqual(InCircleSignReference(a, b, c, nudged), Delaunay.InCircleSign(a, b, c, nudged), $"nudged scale {scale}");
        }
    }

    private static Point64 RandomPoint(Random rng, long scale) =>
      new((long) ((rng.NextDouble() * 2 - 1) * scale), (long) ((rng.NextDouble() * 2 - 1) * scale));

    [TestMethod]
    public void TestInCircleSignIsExact()
    {
      // cocircular points (on x^2 + y^2 = 5^2 * k^2, shifted far from the origin) must give 0,
      // and points just inside or outside must give the exact sign, in both the Int128 range
      // and beyond it
      foreach (long scale in new long[] { 1, 1000, 1_000_000, 100_000_000, 1_000_000_000 })
        foreach (long offset in new long[] { 0, 5_000_000_000, -4_000_000_000_000_000 })
        {
          Point64 a = new(offset + 5 * scale, offset), b = new(offset, offset + 5 * scale);
          Point64 c = new(offset - 3 * scale, offset - 4 * scale);
          Point64 on = new(offset + 4 * scale, offset - 3 * scale);
          Point64 inside = new(on.X - 1, on.Y + 1), outside = new(on.X + 1, on.Y - 1);
          foreach (Point64 d in new[] { on, inside, outside })
            Assert.AreEqual(InCircleSignReference(a, b, c, d), Delaunay.InCircleSign(a, b, c, d),
              $"scale {scale} offset {offset} d ({d.X},{d.Y})");
          Assert.AreEqual(0, Delaunay.InCircleSign(a, b, c, on));
        }
    }
  }
}
