using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestOffsetOrientation
  {
    [TestMethod]
    public void TestOffsettingOrientation1()
    {
      Paths64 subject = Ps(P(0, 0, 0, 5, 5, 5, 5, 0));
      Paths64 solution = Clipper.InflatePaths(subject, 1, JoinType.Round, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      // when offsetting, output orientation is positive whatever the input's orientation
      Assert.IsFalse(Clipper.IsPositive(subject[0]));
      Assert.IsTrue(Clipper.IsPositive(solution[0]));
    }

    [TestMethod]
    public void TestOffsettingOrientation2()
    {
      Paths64 subject = Ps(
        P(20, 220, 280, 220, 280, 280, 20, 280),
        P(0, 200, 0, 300, 300, 300, 300, 200));
      ClipperOffset co = new() { ReverseSolution = true };
      co.AddPaths(subject, JoinType.Round, EndType.Polygon);
      Paths64 solution = new();
      co.Execute(5, solution);
      Assert.AreEqual(2, solution.Count);
      // When offsetting, output orientation is positive EXCEPT when ReverseSolution == true
      Assert.IsFalse(Clipper.IsPositive(solution[0]));
    }

    [TestMethod]
    public void TestOffsettingOrientation3() // PolyClipping/Clipper2#13
    {
      // a stray negative sliver lower than the outer path must not reverse the group
      Paths64 subject = Ps(
        P(33621, 18600, 33889, 18931, 33622, 18601, 33118, 18033),
        P(33058, 3177, 33032, 3337, 33017, 3570, 33040, 17609, 33118, 18033, 32723, 17588, 32698, 3608,
          32710, 3376, 32737, 3177));
      Paths64 solution = Clipper.InflatePaths(subject, -8, JoinType.Round, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      // the sliver is a hole, so the result is the outer path's own inward offset
      Assert.AreEqual(Clipper.Area(Clipper.InflatePaths(Ps(subject[1]), -8, JoinType.Round, EndType.Polygon)),
        Clipper.Area(solution));
    }

    [TestMethod]
    public void TestOffsettingOrientation4()
    {
      // a group holding only holes takes its orientation from the outer in another group
      Path64 outer = P(0, 0, 300, 0, 300, 300, 0, 300);
      Path64 hole = P(100, 100, 100, 200, 200, 200, 200, 100);
      for (int mirror = 0; mirror < 2; ++mirror)
      {
        foreach (bool outerFirst in new[] { true, false })
        {
          ClipperOffset co = new();
          co.AddPath(outerFirst ? outer : hole, JoinType.Miter, EndType.Polygon);
          co.AddPath(outerFirst ? hole : outer, JoinType.Miter, EndType.Polygon);
          Paths64 solution = new();
          co.Execute(10, solution);
          Assert.AreEqual(320 * 320 - 80 * 80, Clipper.Area(solution));
        }
        outer.Reverse();
        hole.Reverse();
      }
    }

    internal static Path64 MakeSquare(long left, long top, long size, bool clockwise)
    {
      Path64 result = P(left, top, left + size, top, left + size, top + size, left, top + size);
      if (clockwise) result.Reverse();
      return result;
    }

    private static List<double> OffsetAreas(Paths64 groups, double delta)
    {
      ClipperOffset co = new();
      foreach (Path64 path in groups) co.AddPath(path, JoinType.Round, EndType.Polygon);
      Paths64 solution = new();
      co.Execute(delta, solution);
      return solution.Select(p => Clipper.Area(p)).OrderBy(a => a).ToList();
    }

    private static List<double> OneGroupAreas(Paths64 paths, double delta) =>
      Clipper.InflatePaths(paths, delta, JoinType.Round, EndType.Polygon)
        .Select(p => Clipper.Area(p)).OrderBy(a => a).ToList();

    private static void AreEqual(IList<double> expected, IList<double> actual, string msg = "") =>
      Assert.AreEqual(string.Join(" ", expected), string.Join(" ", actual), msg);

    [TestMethod]
    public void TestOffsettingOrientation5()
    {
      // a group wound against another is its own outer, unless it lies inside that group's outer
      Path64 outer = MakeSquare(0, 0, 1000, false);
      Path64 disjoint = MakeSquare(2000, 0, 100, true);
      Path64 hole = MakeSquare(400, 400, 200, true);
      foreach (bool outerFirst in new[] { true, false })
      {
        Paths64 Groups(Path64 other) => outerFirst ? Ps(outer, other) : Ps(other, outer);
        AreEqual(new double[] { 14308, 1040308 }, OffsetAreas(Groups(disjoint), 10));
        AreEqual(new double[] { 6400, 960400 }, OffsetAreas(Groups(disjoint), -10));
        AreEqual(new double[] { -32400, 1040308 }, OffsetAreas(Groups(hole), 10));
        AreEqual(new double[] { -48308, 960400 }, OffsetAreas(Groups(hole), -10));
      }
    }

    [TestMethod]
    public void TestOffsettingOrientation6()
    {
      // the innermost enclosing path decides, so a group inside another group's hole is an island
      Path64[] chain = { MakeSquare(0, 0, 1000, false), MakeSquare(200, 200, 500, true),
        MakeSquare(300, 300, 100, false) };
      int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
        new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
      foreach (int[] order in orders)
      {
        Paths64 groups = Ps(chain[order[0]], chain[order[1]], chain[order[2]]);
        AreEqual(new double[] { -230400, 14308, 1040308 }, OffsetAreas(groups, 10), string.Join(",", order));
      }
    }

    [TestMethod]
    public void TestOffsettingOrientation7()
    {
      // separate groups behave like one group when their paths' roles are unambiguous
      Path64 outer = MakeSquare(0, 0, 1000, false);
      Path64 ringHole = MakeSquare(500, 500, 400, true);
      Path64 touching = MakeSquare(0, 400, 200, true); // first vertex on the outer's edge
      Path64 inscribed = P(0, 500, 500, 1000, 1000, 500, 500, 0);
      Path64 island = MakeSquare(600, 600, 100, false);
      Path64 hole = MakeSquare(100, 100, 100, true);

      AreEqual(OneGroupAreas(Ps(outer, touching), 10), OffsetAreas(Ps(touching, outer), 10));
      AreEqual(OneGroupAreas(Ps(outer, inscribed), 10), OffsetAreas(Ps(inscribed, outer), 10));

      // a group of an outer and its hole encloses an island in the hole and a hole in the ring
      ClipperOffset co = new();
      co.AddPath(island, JoinType.Round, EndType.Polygon);
      co.AddPath(hole, JoinType.Round, EndType.Polygon);
      co.AddPaths(Ps(outer, ringHole), JoinType.Round, EndType.Polygon);
      Paths64 solution = new();
      co.Execute(10, solution);
      AreEqual(OneGroupAreas(Ps(outer, ringHole, island, hole), 10),
        solution.Select(p => Clipper.Area(p)).OrderBy(a => a).ToList());

      // the hole lies in an outer and in another group's hole of the same area, and the outer decides
      Path64 frame = MakeSquare(-500, -500, 2000, false);
      Path64 frameHole = MakeSquare(0, 0, 1000, true);
      foreach (bool outerFirst in new[] { true, false })
      {
        ClipperOffset tie = new();
        tie.AddPath(hole, JoinType.Round, EndType.Polygon);
        if (outerFirst) tie.AddPath(outer, JoinType.Round, EndType.Polygon);
        tie.AddPaths(Ps(frame, frameHole), JoinType.Round, EndType.Polygon);
        if (!outerFirst) tie.AddPath(outer, JoinType.Round, EndType.Polygon);
        tie.Execute(10, solution);
        AreEqual(OneGroupAreas(Ps(frame, frameHole, outer, hole), 10),
          solution.Select(p => Clipper.Area(p)).OrderBy(a => a).ToList(), "outerFirst " + outerFirst);
      }
    }

    [TestMethod]
    public void TestOffsettingOrientation8()
    {
      // a container with many collinear vertices is searched through an index of its edges
      Path64 corners = P(0, 0, 1000, 0, 1000, 400, 400, 400, 400, 1000, 0, 1000);
      Path64 lShape = new();
      for (int i = 0; i < corners.Count; ++i)
      {
        Point64 a = corners[i];
        Point64 b = corners[(i + 1) % corners.Count];
        long steps = (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y)) / 50;
        for (long k = 0; k < steps; ++k)
          lShape.Add(new Point64(a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps));
      }
      Assert.IsTrue(lShape.Count >= 64);
      Path64 inArm = MakeSquare(100, 100, 100, true);
      Path64 touching = MakeSquare(0, 500, 100, true); // first vertex on an edge's vertex
      Path64 inNotch = MakeSquare(600, 600, 100, true); // inside the bounds only
      Path64 notchOuter = new(inNotch);
      notchOuter.Reverse();
      foreach (double delta in new[] { 10.0, -10.0 })
        AreEqual(OneGroupAreas(Ps(lShape, inArm, touching, notchOuter), delta),
          OffsetAreas(Ps(inNotch, inArm, touching, lShape), delta), "delta " + delta);
    }

    [TestMethod]
    public void TestOffsettingStrayHoles()
    {
      // a hole that no outer encloses contributes nothing, not even the loops of its concave joins
      Path64 outer = MakeSquare(0, 0, 100, false);
      Path64 stray = MakeSquare(500, 500, 50, true);
      Path64 cwOuter = MakeSquare(0, 0, 100, true);
      Path64 ccwStray = MakeSquare(500, 500, 50, false);
      foreach (JoinType joinType in new[] { JoinType.Miter, JoinType.Round })
        foreach (double delta in new[] { 10.0, -10.0 })
        {
          Assert.AreEqual(Exact(Clipper.InflatePaths(Ps(outer), delta, joinType, EndType.Polygon)),
            Exact(Clipper.InflatePaths(Ps(outer, stray), delta, joinType, EndType.Polygon)));
          Assert.AreEqual(Exact(Clipper.InflatePaths(Ps(cwOuter), delta, joinType, EndType.Polygon)),
            Exact(Clipper.InflatePaths(Ps(cwOuter, ccwStray), delta, joinType, EndType.Polygon)));
        }

      // a hole around another group's island is still stray, since none of its vertices is inside an outer
      Path64 farOuter = MakeSquare(0, 0, 1000, false);
      Path64 around = MakeSquare(2000, 0, 300, true);
      Path64 island = MakeSquare(2100, 100, 50, false);
      ClipperOffset co = new();
      co.AddPath(island, JoinType.Round, EndType.Polygon);
      co.AddPaths(Ps(farOuter, around), JoinType.Round, EndType.Polygon);
      Paths64 solution = new();
      co.Execute(10, solution);
      Assert.AreEqual(Exact(Clipper.InflatePaths(Ps(farOuter, island), 10, JoinType.Round, EndType.Polygon)),
        Exact(solution));

      // a hole poking out of its outer still cuts it
      Path64 poking = MakeSquare(80, 40, 40, true);
      Assert.IsTrue(Clipper.Area(Clipper.InflatePaths(Ps(outer, poking), 10, JoinType.Miter, EndType.Polygon)) < 120 * 120);
    }

    private static Paths64 OffsetGroups(Paths64[] groups, double delta)
    {
      ClipperOffset co = new();
      foreach (Paths64 group in groups) co.AddPaths(group, JoinType.Miter, EndType.Polygon);
      Paths64 solution = new();
      co.Execute(delta, solution);
      return solution;
    }

    [TestMethod]
    public void TestOffsettingStrayHoleDoesNotOrient()
    {
      // the stray strip crosses the middle square without enclosing any of its vertices, so it must not
      // decide that the small square inside it is an island rather than the middle square's hole
      Path64 farOuter = MakeSquare(5000, 5000, 10000, false);
      Path64 strip = P(-100, 600, 1100, 600, 1100, 400, -100, 400);
      Path64 middle = MakeSquare(0, 0, 1000, false);
      Path64 hole = MakeSquare(450, 450, 100, true);
      for (int mirror = 0; mirror < 2; ++mirror)
      {
        foreach (double delta in new[] { 0.0, 10.0, -10.0 })
          Assert.AreEqual(Exact(OffsetGroups(new[] { Ps(farOuter), Ps(middle), Ps(hole) }, delta)),
            Exact(OffsetGroups(new[] { Ps(farOuter, strip), Ps(middle), Ps(hole) }, delta)), $"mirror {mirror} delta {delta}");
        foreach (Path64 path in new[] { farOuter, strip, middle, hole }) path.Reverse();
      }
    }

    [TestMethod]
    public void TestOffsettingStrayHoleChain()
    {
      // each strip is stray only once the hole beside the previous strip is known to be a hole, so the
      // whole chain must settle, however long
      foreach (bool mirror in new[] { false, true })
      {
        List<Paths64> withStrips = new(), withoutStrips = new();
        Path64 farOuter = MakeSquare(-300000000, -50000000, 100000000, mirror);
        withStrips.Add(Ps(farOuter));
        withoutStrips.Add(Ps(farOuter));
        int stripGroup = 0;
        long size = 10000000;
        for (long i = 1; i <= 6; ++i, size /= 10)
        {
          long x = i * 300000000;
          Path64 strip = P(x - size * 6 / 10, -size / 10, x + size * 6 / 10, -size / 10,
            x + size * 6 / 10, size / 10, x - size * 6 / 10, size / 10);
          if (!mirror) strip.Reverse();
          Path64 small = MakeSquare(x - size / 20, -size / 20, size / 10, !mirror);
          Path64 middle = MakeSquare(x - size / 2, -size / 2, size, mirror);
          // the strip sits in the group of the previous level's small square
          withStrips[stripGroup].Add(strip);
          stripGroup = withStrips.Count;
          withStrips.Add(Ps(small));
          withStrips.Add(Ps(middle));
          withoutStrips.Add(Ps(small));
          withoutStrips.Add(Ps(middle));
        }
        foreach (double delta in new[] { 0.0, 10.0, -10.0 })
          Assert.AreEqual(Exact(OffsetGroups(withoutStrips.ToArray(), delta)),
            Exact(OffsetGroups(withStrips.ToArray(), delta)), $"mirror {mirror} delta {delta}");
      }
    }

    [TestMethod]
    public void TestOffsettingHoleSizesCompareExactly()
    {
      // Area() rounds both of these, and a hole compared as larger than its outer was dropped as stray
      Path64 farOuter = MakeSquare(-300000000, -300000000, 200000000, false);
      Path64 outer = P(7786030, 124989629, 13919251, 42104456,
        167208900, 68570499, 199121929, 96243643, 185340413, 198615010, 66854563, 174207781);
      Path64 coincident = new(outer);
      coincident.Reverse();
      // a thin outer and a thin hole strictly inside it, whose areas are exactly 36 and 16
      Path64 thinOuter = P(-310414360, -251832566, 13677104, 72258904, 337768562, 396350368, 13677098, 72258898);
      Path64 thinHole = P(13677099, 72258899, 229738075, 288319879, 13677103, 72258903, -202383873, -143802077);
      Path64 smallOuter = MakeSquare(-1000000, -1000000, 100000, false);
      foreach (double delta in new[] { 0.0, 10.0, -10.0 })
      {
        Assert.AreEqual(Exact(OffsetGroups(new[] { Ps(farOuter), Ps(outer, coincident) }, delta)),
          Exact(OffsetGroups(new[] { Ps(farOuter, outer, coincident) }, delta)), "delta " + delta);
        Assert.AreEqual(Exact(OffsetGroups(new[] { Ps(smallOuter), Ps(thinOuter, thinHole) }, delta)),
          Exact(OffsetGroups(new[] { Ps(smallOuter, thinOuter, thinHole) }, delta)), "delta " + delta);
      }
    }

    [TestMethod]
    public void TestOffsettingZeroDeltaNormalizes()
    {
      // a zero delta is insignificant like any other below half a unit, so it normalizes the winding too
      Path64 square = MakeSquare(0, 0, 100, true);
      Paths64 solution = Clipper.InflatePaths(Ps(square), 0, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(Exact(Clipper.InflatePaths(Ps(square), 0.3, JoinType.Miter, EndType.Polygon)), Exact(solution));
      Assert.AreEqual(100 * 100, Clipper.Area(solution));

      PathsD squareD = PsD(PD(0, 0, 0, 1, 1, 1, 1, 0));
      PathsD solutionD = Clipper.InflatePaths(squareD, 0, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(Clipper.PathsDToString(Clipper.InflatePaths(squareD, 0.003, JoinType.Miter, EndType.Polygon)),
        Clipper.PathsDToString(solutionD));
      Assert.AreEqual(1.0, Clipper.Area(solutionD), 1e-12);
    }

    [TestMethod]
    public void TestOffsetClockwiseOuterWithHoleWinding()
    {
      // NodeFlow's hatching input: a clockwise outer with a counter-clockwise hole
      Path64 outer = MakeSquare(0, 0, 1000, true);
      Path64 hole = MakeSquare(400, 400, 200, false);
      Assert.IsFalse(Clipper.IsPositive(outer));
      Assert.IsTrue(Clipper.IsPositive(hole));
      Paths64 solution = Clipper.InflatePaths(Ps(outer, hole), 10, JoinType.Miter, EndType.Polygon);
      Assert.AreEqual(2, solution.Count);
      Assert.AreEqual(1, solution.Count(p => Clipper.Area(p) > 0));
      Assert.AreEqual(1, solution.Count(p => Clipper.Area(p) < 0));
    }
  }
}
