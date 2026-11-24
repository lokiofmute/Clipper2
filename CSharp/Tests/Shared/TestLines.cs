using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{

  [TestClass]
  public class TestLines
  {

    [TestMethod]
    public void TestOpenPaths()
    {
      for (int i = 0; i <= 16; i++)
      {
        Clipper64 c64 = new();
        Paths64 subj = new(), subj_open = new(), clip = new();
        Paths64 solution = new(), solution_open = new();

        Assert.IsTrue(ClipperFileIO.LoadTestNum(TestData.Path("Lines.txt"),
          i, subj, subj_open, clip, out ClipType clipType, out FillRule fillrule,  
          out long area, out int count, out _),
            string.Format("Loading test {0} failed.", i));

        c64.AddSubject(subj);
        c64.AddOpenSubject(subj_open);
        c64.AddClip(clip);
        c64.Execute(clipType, fillrule, solution, solution_open);

        if (area > 0)
        {
          double area2 = Clipper.Area(solution);
          double a = area / area2;
          Assert.IsTrue(a > 0.995 && a < 1.005,
            string.Format("Incorrect area in test {0}", i));
        }

        if (count > 0 && Math.Abs(solution.Count - count) > 0)
        {
          Assert.IsTrue(Math.Abs(solution.Count - count) < 2,
            string.Format("Incorrect count in test {0}", i));
        }

      } //bottom of num loop

    }

    [TestMethod]
    public void TestMultipleLines()
    {
      int testNumber = 1;
      while (true)
      {
        Paths64 subject = new(), subjectOpen = new(), clip = new();
        Paths64 solution = new(), solutionOpen = new();
        if (!TestHelpers.LoadTestNum("Lines.txt", testNumber, subject, subjectOpen, clip,
          out _, out long count, out ClipType ct, out FillRule fr)) break;
        Clipper64 c = new();
        c.AddSubject(subject);
        c.AddOpenSubject(subjectOpen);
        c.AddClip(clip);
        Assert.IsTrue(c.Execute(ct, fr, solution, solutionOpen));
        long count2 = solution.Count + solutionOpen.Count;
        long countDiff = Math.Abs(count2 - count);
        double relativeCountDiff = count != 0 ? countDiff / (double) count : 0;
        if (testNumber == 1)
        {
          Assert.AreEqual(1, solution.Count);
          if (solution.Count > 0)
          {
            Assert.AreEqual(6, solution[0].Count);
            Assert.IsTrue(Clipper.IsPositive(solution[0]));
          }
          Assert.AreEqual(1, solutionOpen.Count);
          if (solutionOpen.Count > 0)
          {
            Assert.AreEqual(2, solutionOpen[0].Count);
            // expect vertex closest to input path's start
            if (solutionOpen[0].Count > 0) Assert.AreEqual(6, solutionOpen[0][0].Y);
          }
        }
        else
        {
          Assert.IsTrue(countDiff <= 8, "count diff in test " + testNumber);
          Assert.IsTrue(relativeCountDiff <= 0.1, "relative count diff in test " + testNumber);
        }
        ++testNumber;
      }
      Assert.IsTrue(testNumber >= 17);
    }
  }
}
