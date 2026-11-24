using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestPolytreeIntersection
  {
    [TestMethod]
    public void TestPolyTreeIntersection()
    {
      Clipper64 clipper = new();
      Paths64 subject = Ps(P(0, 0, 0, 5, 5, 5, 5, 0));
      clipper.AddSubject(subject);
      clipper.AddClip(Ps(P(1, 1, 1, 6, 6, 6, 6, 1)));
      PolyTree64 solution = new();
      Paths64 openPaths = new();
      if (Clipper.IsPositive(subject[0]))
        clipper.Execute(ClipType.Intersection, FillRule.Positive, solution, openPaths);
      else
        clipper.Execute(ClipType.Intersection, FillRule.Negative, solution, openPaths);
      Assert.AreEqual(0, openPaths.Count);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(4, solution[0].Polygon!.Count);
    }
  }
}
