using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestRandomPaths
  {
    // inclusive bounds, like std::uniform_int_distribution
    private static int GenerateRandomInt(Random rng, int minValue, int maxValue) =>
      minValue == maxValue ? minValue : rng.Next(minValue, maxValue + 1);

    private static Paths64 GenerateRandomPaths(Random rng, int minPathCount, int maxComplexity)
    {
      int pathCount = GenerateRandomInt(rng, minPathCount, maxComplexity);
      Paths64 result = new(pathCount);
      for (int path = 0; path < pathCount; ++path)
      {
        int pathLength = GenerateRandomInt(rng, 0, Math.Max(0, maxComplexity));
        Path64 resultPath = new(pathLength);
        for (int point = 0; point < pathLength; ++point)
        {
          if (resultPath.Count == 0)
            resultPath.Add(new Point64(
              rng.Next(-maxComplexity, maxComplexity * 2 + 1),
              rng.Next(-maxComplexity, maxComplexity * 2 + 1)));
          else
          {
            Point64 prev = resultPath[^1];
            resultPath.Add(new Point64(prev.X + rng.Next(-5, 6), prev.Y + rng.Next(-5, 6)));
          }
        }
        result.Add(resultPath);
      }
      return result;
    }

    [TestMethod]
    public void TestRandomPaths1()
    {
      // C++ name: TestRandomPaths (a C# method cannot share its class name). The C++
      // generator (std::default_random_engine) is not reproducible in .NET, so the
      // sequence differs; the asserted property is the same.
      Random rng = new(42);
      for (int i = 0; i < 750; ++i)
      {
        int maxComplexity = Math.Max(1, i / 10);
        Paths64 subject = GenerateRandomPaths(rng, 1, maxComplexity);
        Paths64 subjectOpen = GenerateRandomPaths(rng, 0, maxComplexity);
        Paths64 clip = GenerateRandomPaths(rng, 0, maxComplexity);
        ClipType ct = (ClipType) GenerateRandomInt(rng, 0, 4);
        FillRule fr = (FillRule) GenerateRandomInt(rng, 0, 3);
        Paths64 solution = new(), solutionOpen = new();
        Clipper64 c = new();
        c.AddSubject(subject);
        c.AddOpenSubject(subjectOpen);
        c.AddClip(clip);
        c.Execute(ct, fr, solution, solutionOpen);
        long areaPaths = (long) Clipper.Area(solution);
        PolyTree64 solutionPolytree = new();
        Paths64 solutionPolytreeOpen = new();
        Clipper64 clipperPolytree = new();
        clipperPolytree.AddSubject(subject);
        clipperPolytree.AddOpenSubject(subjectOpen);
        clipperPolytree.AddClip(clip);
        clipperPolytree.Execute(ct, fr, solutionPolytree, solutionPolytreeOpen);
        Paths64 solutionPolytreePaths = Clipper.PolyTreeToPaths64(solutionPolytree);
        long areaPolytree = (long) Clipper.Area(solutionPolytreePaths);
        Assert.AreEqual(areaPaths, areaPolytree, "iteration " + i);
      }
    }
  }
}
