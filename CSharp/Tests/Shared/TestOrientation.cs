using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestOrientation
  {
    [TestMethod]
    public void TestNegativeOrientation()
    {
      Paths64 subjects = Ps(P(0, 0, 0, 100, 100, 100, 100, 0), P(10, 10, 10, 110, 110, 110, 110, 10));
      Assert.IsFalse(Clipper.IsPositive(subjects[0]));
      Assert.IsFalse(Clipper.IsPositive(subjects[1]));
      Paths64 clips = Ps(P(50, 50, 50, 150, 150, 150, 150, 50));
      Assert.IsFalse(Clipper.IsPositive(clips[0]));
      Paths64 solution = Clipper.Union(subjects, clips, FillRule.Negative);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(12, solution[0].Count);
    }
  }
}
