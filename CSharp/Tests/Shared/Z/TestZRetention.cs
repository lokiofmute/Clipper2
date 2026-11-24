using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  // Z must pass through every Boolean op unchanged, including Z values that use the
  // high 32 bits (NodeFlow encodes (loop + 1) << 32 | vertex).
  [TestClass]
  public class TestZRetention
  {
    private const long C = 5_000_000;

    private static Path64 Square(long left, long top, long right, long bottom, long zHigh)
    {
      Path64 path = new()
      {
        new Point64(left, top), new Point64(right, top),
        new Point64(right, bottom), new Point64(left, bottom)
      };
      for (int i = 0; i < path.Count; i++)
      {
        Point64 pt = path[i];
        pt.Z = (zHigh << 32) | (long) i;
        path[i] = pt;
      }
      return path;
    }

    private static Paths64 Subject() => Ps(Square(-C, -C, C / 2, C / 2, 2));
    private static Paths64 Clip() => Ps(Square(-C / 2, -C / 2, C, C, 3));

    private static Dictionary<(long, long), long> VertexZ()
    {
      Dictionary<(long, long), long> result = new();
      foreach (Path64 path in Subject())
        foreach (Point64 pt in path) result[(pt.X, pt.Y)] = pt.Z;
      foreach (Path64 path in Clip())
        foreach (Point64 pt in path) result.TryAdd((pt.X, pt.Y), pt.Z);
      return result;
    }

    private static readonly ClipType[] ClipTypes =
      { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor };
    private static readonly FillRule[] FillRules = { FillRule.NonZero, FillRule.EvenOdd };

    [TestMethod]
    public void TestZHighBitsSurviveBooleans()
    {
      Dictionary<(long, long), long> vertexZ = VertexZ();
      foreach (ClipType ct in ClipTypes)
        foreach (FillRule fr in FillRules)
        {
          Clipper64 c = new();
          c.AddSubject(Subject());
          c.AddClip(Clip());
          Paths64 solution = new();
          Assert.IsTrue(c.Execute(ct, fr, solution));
          Assert.AreNotEqual(0, solution.Count, ct + " " + fr);
          int vertexHits = 0;
          foreach (Path64 path in solution)
            foreach (Point64 pt in path)
              if (vertexZ.TryGetValue((pt.X, pt.Y), out long z))
              {
                Assert.AreEqual(z, pt.Z, $"{ct} {fr} at ({pt.X},{pt.Y})");
                ++vertexHits;
              }
          Assert.AreNotEqual(0, vertexHits, ct + " " + fr);
        }
    }

    [TestMethod]
    public void TestZWithoutCallback()
    {
      Dictionary<(long, long), long> vertexZ = VertexZ();
      foreach (ClipType ct in ClipTypes)
        foreach (FillRule fr in FillRules)
        {
          Clipper64 c = new();
          c.AddSubject(Subject());
          c.AddClip(Clip());
          Paths64 solution = new();
          Assert.IsTrue(c.Execute(ct, fr, solution));
          int crossings = 0;
          foreach (Path64 path in solution)
            foreach (Point64 pt in path)
              if (vertexZ.TryGetValue((pt.X, pt.Y), out long z))
                Assert.AreEqual(z, pt.Z, $"{ct} {fr} vertex ({pt.X},{pt.Y})");
              else
              {
                Assert.AreEqual(0L, pt.Z, $"{ct} {fr} crossing ({pt.X},{pt.Y})");
                ++crossings;
              }
          if (ct != ClipType.Union) Assert.AreNotEqual(0, crossings, ct + " " + fr);
        }
    }

    [TestMethod]
    public void TestExecuteAfterZCallbackThrows()
    {
      // a clipper must be left clean when an exception escapes the Z callback.
      // Unlike C++, C# Execute catches it (upstream 2.0.0 behaviour) and returns false.
      Paths64 subject = Ps(P(0, 0, 100, 0, 100, 100, 0, 100), P(50, 50, 150, 50, 150, 150, 50, 150));
      Paths64 clip = Ps(P(20, -10, 80, -10, 80, 200, 20, 200));

      Clipper64 fresh = new();
      fresh.AddSubject(subject);
      fresh.AddClip(clip);
      Paths64 expected = new();
      Assert.IsTrue(fresh.Execute(ClipType.Xor, FillRule.NonZero, expected));

      Clipper64 clipper = new();
      clipper.AddSubject(subject);
      clipper.AddClip(clip);
      int calls = 0;
      clipper.ZCallback = (Point64 b1, Point64 t1, Point64 b2, Point64 t2, ref Point64 ip) =>
      {
        if (++calls == 3) throw new InvalidOperationException("Z callback");
      };
      Paths64 solution = new();
      Assert.IsFalse(clipper.Execute(ClipType.Xor, FillRule.NonZero, solution));
      Assert.AreEqual(3, calls);

      clipper.ZCallback = null;
      Assert.IsTrue(clipper.Execute(ClipType.Xor, FillRule.NonZero, solution));
      Assert.AreEqual(Clipper.Paths64ToString(expected), Clipper.Paths64ToString(solution));
    }
  }
}
