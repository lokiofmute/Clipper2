using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestPolytreeUnion
  {
    [TestMethod]
    public void TestPolytreeUnion1()
    {
      // C++ name: TestPolytreeUnion (renamed: a C# method can't share its class's name)
      Paths64 subject = Ps(P(0, 0, 0, 5, 5, 5, 5, 0), P(1, 1, 1, 6, 6, 6, 6, 1));
      Clipper64 clipper = new();
      clipper.AddSubject(subject);
      PolyTree64 solution = new();
      Paths64 openPaths = new();
      if (Clipper.IsPositive(subject[0]))
        clipper.Execute(ClipType.Union, FillRule.Positive, solution, openPaths);
      else
      {
        // because clipping ops normally return Positive solutions
        clipper.ReverseSolution = true;
        clipper.Execute(ClipType.Union, FillRule.Negative, solution, openPaths);
      }
      Assert.AreEqual(0, openPaths.Count);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(8, solution[0].Polygon!.Count);
      Assert.AreEqual(Clipper.IsPositive(subject[0]), Clipper.IsPositive(solution[0].Polygon!));
    }

    [TestMethod]
    public void TestPolytreeUnion2() // #987
    {
      Paths64 subject = Ps(
        P(534, 1024, 534, -800, 1026, -800, 1026, 1024),
        P(1, 1024, 8721, 1024, 8721, 1920, 1, 1920),
        P(30, 1024, 30, -800, 70, -800, 70, 1024),
        P(1, 1024, 1, -1024, 3841, -1024, 3841, 1024),
        P(3900, -1024, 6145, -1024, 6145, 1024, 3900, 1024),
        P(5884, 1024, 5662, 1024, 5662, -1024, 5884, -1024),
        P(534, 1024, 200, 1024, 200, -800, 534, -800),
        P(200, -800, 200, 1024, 70, 1024, 70, -800),
        P(1200, 1920, 1313, 1920, 1313, -800, 1200, -800),
        P(6045, -800, 6045, 1024, 5884, 1024, 5884, -800));
      Clipper64 clipper = new();
      clipper.AddSubject(subject);
      PolyTree64 solution = new();
      Paths64 openPaths = new();
      clipper.Execute(ClipType.Union, FillRule.EvenOdd, solution, openPaths);
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(1, solution[0].Count);
    }

    [TestMethod]
    public void TestPolytreeUnion3()
    {
      Paths64 subject = Ps(P(
        -120927680, 590077597, -120919386, 590077307, -120919432, 590077309, -120919451, 590077309, -120919455, 590077310, -120099297, 590048669,
        -120928004, 590077608, -120902794, 590076728, -120919444, 590077309, -120919450, 590077309, -120919842, 590077323, -120922852, 590077428,
        -120902452, 590076716, -120902455, 590076716, -120912590, 590077070, 11914491, 249689797
      )); 
      Clipper64 clipper = new();
      clipper.AddSubject(subject);
      PolyTree64 solution = new();
      clipper.Execute(ClipType.Union, FillRule.EvenOdd, solution);
    }
  }
}
