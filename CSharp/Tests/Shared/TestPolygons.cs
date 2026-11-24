using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{

  [TestClass]
  public class TestPolygons
  {

    private static bool IsInList(int num, int[] list)
    {
      foreach (int i in list) if (i == num) return true;
      return false;
    }

    [TestMethod]
    public void TestClosedPaths()
    {
      int testNum = 0;
      while (true)
      {
        testNum++;
        Clipper64 c64 = new();
        Paths64 subj = new(), subj_open = new (), clip = new();
        Paths64 solution = new(), solution_open = new();

        if (!ClipperFileIO.LoadTestNum(TestData.Path("Polygons.txt"),
          testNum, subj, subj_open, clip, out ClipType clipType, out FillRule fillrule, 
          out long storedArea, out int storedCount, out _))
        {          
          Assert.IsTrue(testNum > 180, string.Format("Loading test polygon {0} failed.", testNum));
          break;
        }

        c64.AddSubject(subj);
        c64.AddOpenSubject(subj_open);
        c64.AddClip(clip);
        c64.Execute(clipType, fillrule, solution, solution_open);
        int measuredCount = solution.Count;
        long measuredArea = (long)Clipper.Area(solution);
        int countDiff = storedCount > 0 ? Math.Abs(storedCount - measuredCount) : 0;
        long areaDiff = storedArea > 0 ? Math.Abs(storedArea - measuredArea) : 0;
        double areaDiffRatio = storedArea <= 0 ? 0 : (double) areaDiff / storedArea;

        // check polygon counts
        if (storedCount > 0)
        {
          if (IsInList(testNum, new int[] { 140, 150, 165, 166, 172, 173, 176, 177, 179 }))
          {
            Assert.IsTrue(countDiff <= 9);
          }
          else if (testNum >= 120)
          {
            Assert.IsTrue(countDiff <= 6);
          }
          else if (IsInList(testNum, new int[] { 27, 121, 126 }))
            Assert.IsTrue(countDiff <= 2);
          else if (IsInList(testNum, new int[] { 23, 37, 43, 45, 87, 102, 111, 118, 119 }))
            Assert.IsTrue(countDiff <= 1);
          else
            Assert.IsTrue(countDiff == 0);
        }

        // check polygon areas
        if (storedArea > 0)
        {
          if (IsInList(testNum, new int[] { 19, 22, 23, 24 }))
            Assert.IsTrue(areaDiffRatio <= 0.5);
          else if (testNum == 193)
            Assert.IsTrue(areaDiffRatio <= 0.25);
          else if (testNum == 63)
            Assert.IsTrue(areaDiffRatio <= 0.1);
          else if (testNum == 16)
            Assert.IsTrue(areaDiffRatio <= 0.075);
          else if (IsInList(testNum, new int[] { 15, 26 }))
            Assert.IsTrue(areaDiffRatio <= 0.05);
          else if (IsInList(testNum, new int[] { 52, 53, 54, 59, 60, 64, 117, 118, 119, 184 }))
            Assert.IsTrue(areaDiffRatio <= 0.02);
          else
            Assert.IsTrue(areaDiffRatio <= 0.01);
        }

      } //bottom of num loop

    }

    [TestMethod]
    public void TestMultiplePolygons()
    {
      int testNumber = 1;
      while (testNumber <= 1000)
      {
        Paths64 subject = new(), subjectOpen = new(), clip = new();
        Paths64 solution = new(), solutionOpen = new();
        if (!TestHelpers.LoadTestNum("Polygons.txt", testNumber, subject, subjectOpen, clip,
          out long storedArea, out long storedCount, out ClipType ct, out FillRule fr)) break;
        Clipper64 c = new();
        c.AddSubject(subject);
        c.AddOpenSubject(subjectOpen);
        c.AddClip(clip);
        c.Execute(ct, fr, solution, solutionOpen);
        long measuredArea = (long) Clipper.Area(solution);
        long measuredCount = solution.Count + solutionOpen.Count;
        // check the polytree variant too
        PolyTree64 solutionPolytree = new();
        Paths64 solutionPolytreeOpen = new();
        Clipper64 clipperPolytree = new();
        clipperPolytree.AddSubject(subject);
        clipperPolytree.AddOpenSubject(subjectOpen);
        clipperPolytree.AddClip(clip);
        clipperPolytree.Execute(ct, fr, solutionPolytree, solutionPolytreeOpen);
        long measuredAreaPolytree = (long) solutionPolytree.Area();
        Paths64 solutionPolytreePaths = Clipper.PolyTreeToPaths64(solutionPolytree);
        long measuredCountPolytree = solutionPolytreePaths.Count;
        string msg = " in test " + testNumber;
        // check polygon counts
        if (storedCount <= 0) { } // skip count
        else if (TestHelpers.IsInList(testNumber, 120, 121, 130, 138,
          140, 148, 163, 165, 166, 167, 168, 172, 173, 175, 178, 180))
          Assert.AreEqual(storedCount, measuredCount, 5, msg);
        else if (TestHelpers.IsInList(testNumber, 126))
          Assert.AreEqual(storedCount, measuredCount, 3, msg);
        else if (TestHelpers.IsInList(testNumber, 16, 27, 181))
          Assert.AreEqual(storedCount, measuredCount, 2, msg);
        else if (testNumber >= 120 && testNumber <= 184)
          Assert.AreEqual(storedCount, measuredCount, 2, msg);
        else if (TestHelpers.IsInList(testNumber, 23, 45, 87, 102, 111, 113, 191))
          Assert.AreEqual(storedCount, measuredCount, 1, msg);
        else
          Assert.AreEqual(storedCount, measuredCount, msg);
        // check polygon areas
        if (storedArea <= 0) { } // skip area
        else if (TestHelpers.IsInList(testNumber, 19, 22, 23, 24))
          Assert.AreEqual(storedArea, measuredArea, 0.5 * measuredArea, msg);
        else if (testNumber == 193)
          Assert.AreEqual(storedArea, measuredArea, 0.2 * measuredArea, msg);
        else if (testNumber == 63)
          Assert.AreEqual(storedArea, measuredArea, 0.1 * measuredArea, msg);
        else if (testNumber == 16)
          Assert.AreEqual(storedArea, measuredArea, 0.075 * measuredArea, msg);
        else if (testNumber == 26)
          Assert.AreEqual(storedArea, measuredArea, 0.05 * measuredArea, msg);
        else if (TestHelpers.IsInList(testNumber, 15, 52, 53, 54, 59, 60, 64, 66, 117, 119, 184))
          Assert.AreEqual(storedArea, measuredArea, 0.02 * measuredArea, msg);
        else
          Assert.AreEqual(storedArea, measuredArea, 0.01 * measuredArea, msg);
        Assert.AreEqual(measuredCount, measuredCountPolytree, msg);
        Assert.AreEqual(measuredArea, measuredAreaPolytree, msg);
        ++testNumber;
      }
      Assert.IsTrue(testNumber > 180, "Polygons.txt has too few tests");
    }

    [TestMethod]
    public void TestHorzSpikes() // #720
    {
      Paths64 paths = TestHelpers.Ps(
        TestHelpers.P(1600, 0, 1600, 100, 2050, 100, 2050, 300, 450, 300, 450, 0),
        TestHelpers.P(1800, 200, 1800, 100, 1600, 100, 2000, 100, 2000, 200));
      Clipper64 c = new();
      c.AddSubject(paths);
      c.Execute(ClipType.Union, FillRule.NonZero, paths);
      Assert.IsTrue(paths.Count >= 1);
    }

    [TestMethod]
    public void TestCollinearOnMacOs() // #777
    {
      Paths64 subject = TestHelpers.Ps(
        TestHelpers.P(0, -453054451, 0, -433253797, -455550000, 0),
        TestHelpers.P(0, -433253797, 0, 0, -455550000, 0));
      Clipper64 clipper = new() { PreserveCollinear = false };
      clipper.AddSubject(subject);
      Paths64 solution = new();
      clipper.Execute(ClipType.Union, FillRule.NonZero, solution);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(3, solution[0].Count);
      Assert.AreEqual(Clipper.IsPositive(subject[0]), Clipper.IsPositive(solution[0]));
    }
      [TestMethod]
    public void TestIntersectPointIsNotFused()
    {
      // both exact intersections have a coordinate of -100.5, and the tie rounds to even unless
      // the multiply and add in GetLineIntersectPt are fused into one rounding. .NET never
      // contracts a * b + c into an FMA on its own, so this guards the rounding itself.
      Assert.IsTrue(InternalClipper.GetLineIntersectPt(new Point64(-207, -383), new Point64(350, 194),
        new Point64(347, 193), new Point64(-206, -384), out Point64 ip));
      Assert.AreEqual(66L, ip.X);
      Assert.AreEqual(-100L, ip.Y);
      Assert.IsTrue(InternalClipper.GetLineIntersectPt(new Point64(-383, -207), new Point64(194, 350),
        new Point64(193, 347), new Point64(-384, -206), out ip));
      Assert.AreEqual(-100L, ip.X);
      Assert.AreEqual(66L, ip.Y);
    }
      [TestMethod]
    public void TestXorDivergingSharedEdges() // PolyClipping/Clipper2#13
    {
      // the paths share edges that diverge at 33889,18931 by less than half a unit for several scanbeams
      Paths64 subject = new(), clip = new();
      subject.Add(TestHelpers.P(
        97513, 3213, 97542, 3449, 97548, 169199, 97559, 169465, 97626, 170263, 97663, 170547,
        97747, 170984, 97915, 171671, 97999, 171956, 98156, 172387, 98420, 173010, 98536, 173254,
        98766, 173674, 99129, 174259, 99290, 174494, 99585, 174873, 100292, 175675, 133417, 208800,
        133586, 209021, 133442, 209203, 129002, 213655, 128782, 213824, 128600, 213682, 7772, 92854,
        7604, 92634, 7752, 92446, 13507, 86686, 13738, 86509, 14008, 86472, 26760, 86465,
        27036, 86453, 27811, 86385, 28080, 86349, 28535, 86262, 29186, 86101, 29446, 86025,
        29917, 85856, 30530, 85596, 30783, 85475, 31229, 85228, 31842, 84841, 32182, 84600,
        32810, 84072, 33032, 83862, 33342, 83530, 33958, 82797, 34128, 82575, 34408, 82159,
        34675, 81711, 34805, 81471, 35000, 81054, 35259, 80418, 35352, 80157, 35483, 79709,
        35634, 79071, 35688, 78786, 35750, 78289, 35820, 77190, 35816, 24593, 35807, 24353,
        35739, 23457, 35684, 23032, 35633, 22770, 35468, 22076, 35370, 21745, 35176, 21200,
        35042, 20881, 34747, 20272, 34566, 19941, 34037, 19135, 33889, 18931, 33621, 18600,
        32723, 17588, 32698, 3608, 32710, 3376, 32737, 3177, 97246, 3177
      ));
      clip.Add(TestHelpers.P(
        97512, 3212, 97542, 3449, 97548, 169199, 97559, 169465, 97626, 170263, 97663, 170547,
        97747, 170984, 97915, 171671, 97999, 171956, 98156, 172387, 98420, 173010, 98536, 173254,
        98766, 173674, 99129, 174259, 99290, 174494, 99585, 174873, 100292, 175675, 133417, 208800,
        133586, 209021, 133442, 209203, 129002, 213655, 128782, 213824, 128600, 213682, 7772, 92854,
        7604, 92634, 7752, 92446, 13507, 86686, 13738, 86509, 14008, 86472, 26760, 86465,
        27036, 86453, 27811, 86385, 28080, 86349, 28535, 86262, 29186, 86101, 29446, 86025,
        29917, 85856, 30530, 85596, 30783, 85475, 31229, 85228, 31842, 84841, 32182, 84600,
        32810, 84072, 33032, 83862, 33342, 83530, 33958, 82797, 34128, 82575, 34408, 82159,
        34675, 81711, 34805, 81471, 35000, 81054, 35259, 80418, 35352, 80157, 35483, 79709,
        35634, 79071, 35688, 78786, 35750, 78289, 35820, 77190, 35816, 24593, 35807, 24353,
        35739, 23457, 35684, 23032, 35633, 22770, 35468, 22076, 35370, 21745, 35176, 21200,
        35042, 20881, 34747, 20272, 34566, 19941, 34037, 19135, 33889, 18931, 33622, 18601,
        33118, 18033, 33040, 17609, 33017, 3570, 33032, 3337, 33058, 3177, 97246, 3177
      ));
      Paths64 solution = Clipper.Xor(subject, clip, FillRule.EvenOdd);
      Paths64 swapped = Clipper.Xor(clip, subject, FillRule.EvenOdd);
      Assert.AreNotEqual(0, solution.Count);
      Assert.AreNotEqual(0, swapped.Count);
      foreach (Path64 path in solution) Assert.IsTrue(Clipper.IsPositive(path));
      foreach (Path64 path in swapped) Assert.IsTrue(Clipper.IsPositive(path));
      Assert.AreEqual(Clipper.Area(solution), Clipper.Area(swapped));
    }

    [TestMethod]
    public void TestDifferenceRoundsIntersections() // PolyClipping/Clipper2#13
    {
      // the exact difference has an area of 3.27; truncating its intersection points
      // reversed the only contour to an area of -3.5
      Paths64 subject = TestHelpers.Ps(TestHelpers.P(0, 10, 5, 2, 7, 1));
      Paths64 clip = TestHelpers.Ps(TestHelpers.P(3, 6, 6, 1, 11, 11));
      Paths64 solution = Clipper.Difference(subject, clip, FillRule.NonZero);
      Assert.AreEqual(1, solution.Count);
      Assert.IsTrue(Clipper.IsPositive(solution[0]));
      Assert.AreEqual(3.27, Clipper.Area(solution), 1.0);
    }

    [TestMethod]
    public void TestDifferenceClampsIntersectionToSegment()
    {
      // the lines of two edges cross just beyond the subject vertex at 6,6
      Paths64 subject = TestHelpers.Ps(TestHelpers.P(6, 6, 8, 2, 2, 12, 3, 7, 12, 4));
      Paths64 clip = TestHelpers.Ps(TestHelpers.P(10, 3, 7, 12, 3, 1, 2, 11, 11, 2, 1, 8));
      Paths64 solution = Clipper.Difference(subject, clip, FillRule.EvenOdd);
      foreach (Path64 path in solution) Assert.IsTrue(Clipper.IsPositive(path));
    }

    [TestMethod]
    public void TestIntersectInt32SpanBeyondIntMax()
    {
      // C++ uses Point<int32_t>, whose spans overflow int; C# only has Point64, whose ends are
      // always subtracted in 64 bits, so this checks the same crossing with Point64.
      Assert.IsTrue(InternalClipper.GetLineIntersectPt(new Point64(-2000000000, -1000000000),
        new Point64(2000000000, 1000000000), new Point64(-1000000000, 2000000000),
        new Point64(1000000000, -2000000000), out Point64 ip));
      Assert.AreEqual(0L, ip.X);
      Assert.AreEqual(0L, ip.Y);
    }

    [TestMethod]
    public void TestUnionKeepsIntersectionInScanbeam()
    {
      // the clip edge leaving -28785,-61 starts almost on the subject's flat edge, so their intersection
      // is computed just below the scanbeam, and clamping it used to leave it 6 units before that vertex
      Paths64 subject = TestHelpers.Ps(TestHelpers.P(-69425, 73, -33569, -35, -24000, -87));
      Paths64 clip = TestHelpers.Ps(TestHelpers.P(-3862, 183, -33569, -35, -28785, -61, -26393, -75, -24000, -87));
      Paths64 solution = Clipper.Union(subject, clip, FillRule.EvenOdd);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(4, solution[0].Count);
      Assert.AreEqual(2230933, Clipper.Area(solution));
    }
  }
}
