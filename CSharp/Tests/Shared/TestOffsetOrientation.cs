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
      // when offsetting, output orientation should match input
      Assert.AreEqual(Clipper.IsPositive(subject[0]), Clipper.IsPositive(solution[0]));
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
      // When offsetting, output orientation should match input EXCEPT when ReverseSolution == true
      Assert.AreNotEqual(Clipper.IsPositive(subject[1]), Clipper.IsPositive(solution[0]));
    }
  }
}
