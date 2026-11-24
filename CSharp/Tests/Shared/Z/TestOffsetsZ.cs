using System.Collections.Generic;
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

    [TestMethod]
    public void TestOffsetsKeepZBeyondDoublePrecision()
    {
      // Z must never pass through a double: above 2^53 a double can't hold every integer
      long z0 = (1L << 53) + 1;
      Path64 square = new()
      {
        new Point64(0, 0, z0), new Point64(100, 0, z0 + 2), new Point64(100, 100, z0 + 4), new Point64(0, 100, z0 + 6)
      };
      HashSet<long> inputZ = new() { z0, z0 + 2, z0 + 4, z0 + 6 };
      foreach (JoinType jt in new[] { JoinType.Miter, JoinType.Square, JoinType.Bevel, JoinType.Round })
        foreach (double delta in new[] { 10.0, -10.0 })
        {
          Paths64 solution = Clipper.InflatePaths(Ps(square), delta, jt, EndType.Polygon);
          Assert.AreNotEqual(0, solution.Count);
          foreach (Path64 path in solution)
            foreach (Point64 pt in path)
              Assert.IsTrue(inputZ.Contains(pt.Z), $"{jt} {delta}: Z {pt.Z} at ({pt.X},{pt.Y})");
        }

      Path64 scaled = Clipper.ScalePath(square, 2.0);
      for (int i = 0; i < square.Count; i++)
        Assert.AreEqual(square[i].Z, scaled[i].Z, "ScalePath");
    }
  }
}
