using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestOffsetsZ
  {
    [TestMethod]
    public void TestOffsetsSquareJoinKeepsZ()
    {
      // a square join's two corner vertices are built from one input vertex, so both must keep its z
      Path64 square = new()
      {
        new Point64(0, 0, 1), new Point64(100, 0, 2), new Point64(100, 100, 3), new Point64(0, 100, 4)
      };
      Paths64 solution = Clipper.InflatePaths(Ps(square), 10, JoinType.Square, EndType.Polygon);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(8, solution[0].Count);
      foreach (Point64 pt in solution[0])
      {
        Point64 corner = (pt.X < 50) ? ((pt.Y < 50) ? square[0] : square[3]) :
          ((pt.Y < 50) ? square[1] : square[2]);
        Assert.AreEqual(corner.Z, pt.Z, $"({pt.X},{pt.Y})");
      }
    }
  }
}
