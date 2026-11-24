using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  internal static class StripeIndexTestUtils
  {
    public static Path64 MakeSquare(long size) =>
      new() { new Point64(0, 0), new Point64(size, 0), new Point64(size, size), new Point64(0, size) };

    // Many-edged ring so the index spans several stripes.
    public static Path64 MakeCircle(long r, int n)
    {
      Path64 p = new(n);
      for (int i = 0; i < n; ++i)
      {
        double a = 2.0 * Math.PI * i / n;
        p.Add(new Point64((long) Math.Round(r * Math.Cos(a), MidpointRounding.AwayFromZero),
          (long) Math.Round(r * Math.Sin(a), MidpointRounding.AwayFromZero)));
      }
      return p;
    }

    // Closed OutPt ring for exercising StripeIndex.BuildFromRing.
    public static OutPt? MakeRing(Path64 path)
    {
      OutRec outrec = new();
      OutPt? head = null, prev = null;
      foreach (Point64 pt in path)
      {
        OutPt op = new(pt, outrec);
        if (prev != null)
        {
          prev.next = op;
          op.prev = prev;
        }
        else head = op;
        prev = op;
      }
      if (head != null)
      {
        prev!.next = head;
        head.prev = prev;
      }
      return head;
    }

    // Independent ground truth for "pt on boundary" so fuzz can assert exact equality
    // off-boundary rather than skipping every IsOn (which would mask a spurious IsOn).
    public static bool IsOnAnySegment(Path64 poly, Point64 pt)
    {
      for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
      {
        Point64 a = poly[j], b = poly[i];
        if (InternalClipper.CrossProductSign(a, b, pt) != 0) continue;
        if (pt.X >= Math.Min(a.X, b.X) && pt.X <= Math.Max(a.X, b.X) &&
          pt.Y >= Math.Min(a.Y, b.Y) && pt.Y <= Math.Max(a.Y, b.Y))
          return true;
      }
      return false;
    }

    // Deterministic LCG so the fuzz is reproducible (same sequence as the C++ test).
    public sealed class Lcg
    {
      private ulong _state;
      public Lcg(ulong seed) { _state = seed; }
      public long Next(long lo, long hi)
      {
        _state = _state * 6364136223846793005UL + 1442695040888963407UL;
        ulong span = (ulong) (hi - lo + 1);
        return lo + (long) ((_state >> 33) % span);
      }
    }

    // FNV-1a hash of coordinates so the signature distinguishes same-size, different geometry.
    public static ulong HashPolygon(Path64 poly)
    {
      ulong h = 1469598103934665603UL;
      foreach (Point64 pt in poly)
      {
        h = (h ^ (ulong) pt.X) * 1099511628211UL;
        h = (h ^ (ulong) pt.Y) * 1099511628211UL;
      }
      return h;
    }

    // Depth-first PolyTree signature (level, hole flag, vertex count, coord hash per node).
    public static void SerializeTree(PolyPath64 node, StringBuilder sb)
    {
      for (int i = 0; i < node.Count; ++i)
      {
        PolyPath64 c = node[i];
        sb.Append(c.Level).Append(c.IsHole ? "H" : "S").Append(c.Polygon!.Count).Append(':')
          .Append(HashPolygon(c.Polygon)).Append(';');
        SerializeTree(c, sb);
      }
    }

    public static string TreeSignature(Paths64 subj, Paths64 clip, ClipType ct, FillRule fr, int threshold)
    {
      Clipper64 clipper = new() { PipIndexThreshold = threshold };
      clipper.AddSubject(subj);
      clipper.AddClip(clip);
      PolyTree64 tree = new();
      Paths64 open = new();
      clipper.Execute(ct, fr, tree, open);
      StringBuilder sb = new();
      SerializeTree(tree, sb);
      return sb.ToString();
    }
  }

  [TestClass]
  public class StripeIndexTests
  {
    private static StripeIndex Build(Path64 path)
    {
      StripeIndex index = new();
      index.BuildFromPath(path);
      return index;
    }

    [TestMethod]
    public void InsidePoint()
    {
      Assert.AreEqual(PointInPolygonResult.IsInside, Build(StripeIndexTestUtils.MakeSquare(100)).PointInPolygon(new Point64(50, 50)));
    }

    [TestMethod]
    public void OutsidePoint()
    {
      StripeIndex index = Build(StripeIndexTestUtils.MakeSquare(100));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(150, 50)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(-10, 50)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(50, 200)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(50, -5)));
    }

    [TestMethod]
    public void OnEdge()
    {
      StripeIndex index = Build(StripeIndexTestUtils.MakeSquare(100));
      Assert.AreEqual(PointInPolygonResult.IsOn, index.PointInPolygon(new Point64(50, 0)));   // bottom (horizontal) edge
      Assert.AreEqual(PointInPolygonResult.IsOn, index.PointInPolygon(new Point64(100, 50))); // right (vertical) edge
    }

    [TestMethod]
    public void OnVertex()
    {
      StripeIndex index = Build(StripeIndexTestUtils.MakeSquare(100));
      Assert.AreEqual(PointInPolygonResult.IsOn, index.PointInPolygon(new Point64(0, 0)));
      Assert.AreEqual(PointInPolygonResult.IsOn, index.PointInPolygon(new Point64(100, 100)));
    }

    [TestMethod]
    public void CollinearExtensionIsNotOn()
    {
      // Points on an edge's infinite line but off the segment must not read IsOn,
      // even when they share that edge's stripe.
      StripeIndex index = Build(StripeIndexTestUtils.MakeSquare(100));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(200, 0)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(-50, 0)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(100, 200)));
    }

    private static void CheckPoint(StripeIndex index, Path64 poly, Point64 pt)
    {
      PointInPolygonResult actual = index.PointInPolygon(pt);
      if (StripeIndexTestUtils.IsOnAnySegment(poly, pt))
        Assert.AreEqual(PointInPolygonResult.IsOn, actual, $"boundary at ({pt.X},{pt.Y})");
      else
        Assert.AreEqual(Clipper.PointInPolygon(pt, poly), actual, $"mismatch at ({pt.X},{pt.Y})");
    }

    [TestMethod]
    public void MatchesBruteForceOnConcavePolygon()
    {
      // A concave "C" shape exercises the parity logic on a non-convex ring.
      Path64 poly = TestHelpers.P(0, 0, 100, 0, 100, 30, 30, 30, 30, 70, 100, 70, 100, 100, 0, 100);
      StripeIndex index = Build(poly);
      for (long y = -5; y <= 105; y += 3)
        for (long x = -5; x <= 105; x += 3)
          CheckPoint(index, poly, new Point64(x, y));
    }

    [TestMethod]
    public void DegeneratePathsAreOutside()
    {
      StripeIndex empty = Build(new Path64());
      Assert.IsTrue(empty.Empty);
      Assert.AreEqual(PointInPolygonResult.IsOutside, empty.PointInPolygon(new Point64(0, 0)));
      StripeIndex segment = Build(TestHelpers.P(0, 0, 10, 0));
      Assert.AreEqual(PointInPolygonResult.IsOutside, segment.PointInPolygon(new Point64(5, 0)));
    }

    [TestMethod]
    public void MultiStripeMatchesBruteForce()
    {
      Path64 poly = StripeIndexTestUtils.MakeCircle(10000, 256);
      Assert.IsTrue(poly.Count > 64); // ensures sqrt(edges) yields several stripes
      StripeIndex index = Build(poly);
      StripeIndexTestUtils.Lcg rng = new(0x9E3779B97F4A7C15UL);
      int checkedCount = 0;
      for (int i = 0; i < 20000; ++i)
      {
        Point64 pt = new(rng.Next(-10200, 10200), rng.Next(-10200, 10200));
        CheckPoint(index, poly, pt);
        if (!StripeIndexTestUtils.IsOnAnySegment(poly, pt)) ++checkedCount;
      }
      Assert.IsTrue(checkedCount > 1000); // sanity: the loop actually exercised non-boundary points
    }

    [TestMethod]
    public void AllHorizontalRingHasNoInterior()
    {
      // A zero-height ring is declined, so points on it are outside as PointInOpPolygon says.
      StripeIndex index = Build(TestHelpers.P(0, 10, 50, 10, 100, 10));
      Assert.IsTrue(index.Empty);
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(50, 10)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(0, 10)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(50, 0)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(50, 20)));
      Assert.AreEqual(PointInPolygonResult.IsOutside, index.PointInPolygon(new Point64(200, 10)));
    }

    [TestMethod]
    public void BoundaryRowPointsMatchBruteForce()
    {
      // Points on the y_min, y_max and vertex rows stress half-open parity and clamping.
      Path64 poly = TestHelpers.P(0, 0, 100, 0, 60, 50, 100, 100, 0, 100, 40, 50);
      StripeIndex index = Build(poly);
      foreach (long y in new long[] { 0, 50, 100 })
        for (long x = -10; x <= 110; ++x)
          CheckPoint(index, poly, new Point64(x, y));
    }

    [TestMethod]
    public void BuildFromRingMatchesBuildFromPath()
    {
      Path64 poly = StripeIndexTestUtils.MakeCircle(5000, 97); // odd count, ensures distinct edges
      StripeIndex fromPath = Build(poly);
      StripeIndex fromRing = new();
      fromRing.BuildFromRing(StripeIndexTestUtils.MakeRing(poly));
      Assert.AreEqual(fromPath.EdgeCount, fromRing.EdgeCount);
      StripeIndexTestUtils.Lcg rng = new(0xD1B54A32D192ED03UL);
      for (int i = 0; i < 5000; ++i)
      {
        Point64 pt = new(rng.Next(-5200, 5200), rng.Next(-5200, 5200));
        Assert.AreEqual(fromPath.PointInPolygon(pt), fromRing.PointInPolygon(pt), $"ring vs path mismatch at ({pt.X},{pt.Y})");
      }
    }

    [TestMethod]
    public void AdversarialCombRingDeclinesIndexing()
    {
      // Tall zigzag where every edge spans the full Y range, so each lands in every
      // stripe. The index declines it (leaves itself empty) rather than lose to brute force.
      const long height = 100000;
      Path64 comb = new();
      for (int i = 0; i < 200; ++i)
        comb.Add(new Point64(i, (i & 1) != 0 ? height : 0));
      Assert.IsTrue(Build(comb).Empty);
    }

    [TestMethod]
    public void NestedPolyTreeIdenticalWithAndWithoutIndex()
    {
      // Concentric circles nest as alternating solids and holes, exercising the
      // owner determination the index accelerates.
      Paths64 subject = new()
      {
        StripeIndexTestUtils.MakeCircle(10000, 220), StripeIndexTestUtils.MakeCircle(7500, 200),
        StripeIndexTestUtils.MakeCircle(5000, 180), StripeIndexTestUtils.MakeCircle(2500, 160)
      };
      string brute = StripeIndexTestUtils.TreeSignature(subject, new Paths64(), ClipType.Union, FillRule.EvenOdd, int.MaxValue);
      string indexed = StripeIndexTestUtils.TreeSignature(subject, new Paths64(), ClipType.Union, FillRule.EvenOdd, 1);
      Assert.AreNotEqual(0, brute.Length);
      Assert.IsTrue(brute.Contains('H')); // sanity: the tree really nests
      Assert.AreEqual(brute, indexed);
    }
  }

#if !USINGZ
  // Real-world stress cases exercise nesting where one parent owns >1000 children.
  // They run only in the non-Z build (nesting is USINGZ-independent) to keep the suite fast.
  [TestClass]
  public class StripeIndexRealWorld
  {
    private static void GatherStats(PolyPath64 node, ref int nodes, ref int maxChildren, ref int holes)
    {
      if (node.Count > maxChildren) maxChildren = node.Count;
      for (int i = 0; i < node.Count; ++i)
      {
        PolyPath64 c = node[i];
        ++nodes;
        if (c.IsHole) ++holes;
        GatherStats(c, ref nodes, ref maxChildren, ref holes);
      }
    }

    [TestMethod]
    public void SubtractTreeIdenticalWithAndWithoutIndex()
    {
      Paths64 subj = new(), subjOpen = new(), clip = new();
      Assert.IsTrue(TestHelpers.LoadTestNum("clipper_subtract.txt", 1, subj, subjOpen, clip,
        out _, out _, out ClipType ct, out FillRule fr));
      Clipper64 clipper = new() { PipIndexThreshold = int.MaxValue };
      clipper.AddSubject(subj);
      clipper.AddClip(clip);
      PolyTree64 bruteTree = new();
      Paths64 open = new();
      clipper.Execute(ct, fr, bruteTree, open);

      int nodes = 0, maxChildren = 0, holes = 0;
      GatherStats(bruteTree, ref nodes, ref maxChildren, ref holes);
      Assert.IsTrue(maxChildren > 1000, "max children " + maxChildren); // confirms heavy single-parent nesting
      Assert.IsTrue(holes > 0);

      StringBuilder sb = new();
      StripeIndexTestUtils.SerializeTree(bruteTree, sb);
      string brute = sb.ToString();
      string indexed = StripeIndexTestUtils.TreeSignature(subj, clip, ct, fr, 1);
      Assert.AreNotEqual(0, brute.Length);
      Assert.AreEqual(brute, indexed);
    }

    [TestMethod]
    public void InflateUnionTreeIdenticalWithAndWithoutIndex()
    {
      Paths64 subj = new(), subjOpen = new(), clip = new();
      Assert.IsTrue(TestHelpers.LoadTestNum("clipper_inflate.txt", 1, subj, subjOpen, clip,
        out _, out _, out _, out _));
      // The file is an OFFSET case; union its subjects to exercise nesting.
      Clipper64 clipper = new() { PipIndexThreshold = int.MaxValue };
      clipper.AddSubject(subj);
      PolyTree64 bruteTree = new();
      Paths64 open = new();
      clipper.Execute(ClipType.Union, FillRule.NonZero, bruteTree, open);

      int nodes = 0, maxChildren = 0, holes = 0;
      GatherStats(bruteTree, ref nodes, ref maxChildren, ref holes);
      Assert.IsTrue(maxChildren > 500, "max children " + maxChildren); // confirms substantial single-parent nesting
      Assert.IsTrue(holes > 0);

      StringBuilder sb = new();
      StripeIndexTestUtils.SerializeTree(bruteTree, sb);
      string brute = sb.ToString();
      // Validate the production default threshold; subtract case covers threshold 1.
      string indexed = StripeIndexTestUtils.TreeSignature(subj, new Paths64(), ClipType.Union, FillRule.NonZero, 64);
      Assert.AreNotEqual(0, brute.Length);
      Assert.AreEqual(brute, indexed);
    }
  }
#endif
}
