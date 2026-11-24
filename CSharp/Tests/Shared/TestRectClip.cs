using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestRectClipTests
  {
    [TestMethod]
    public void TestRectClip()
    {
      Paths64 sub = new(), clp = new(), sol;
      Rect64 rect = new(100, 100, 700, 500);
      clp.Add(rect.AsPath());
      sub.Add(P(100, 100, 700, 100, 700, 500, 100, 500));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(Clipper.Area(sub), Clipper.Area(sol));
      sub.Clear();
      sub.Add(P(110, 110, 700, 100, 700, 500, 100, 500));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(Clipper.Area(sub), Clipper.Area(sol));
      sub.Clear();
      sub.Add(P(90, 90, 700, 100, 700, 500, 100, 500));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(Clipper.Area(clp), Clipper.Area(sol));
      sub.Clear();
      sub.Add(P(110, 110, 690, 110, 690, 490, 110, 490));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(Clipper.Area(sub), Clipper.Area(sol));
      sub.Clear();
      clp.Clear();
      rect = new Rect64(390, 290, 410, 310);
      clp.Add(rect.AsPath());
      sub.Add(P(410, 290, 500, 290, 500, 310, 410, 310));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(0, sol.Count);
      sub.Clear();
      sub.Add(P(430, 290, 470, 330, 390, 330));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(0, sol.Count);
      sub.Clear();
      sub.Add(P(450, 290, 480, 330, 450, 330));
      sol = Clipper.RectClip(rect, sub);
      Assert.AreEqual(0, sol.Count);
      sub.Clear();
      sub.Add(P(208, 66, 366, 112, 402, 303, 234, 332, 233, 262, 243, 140, 215, 126, 40, 172));
      rect = new Rect64(237, 164, 322, 248);
      sol = Clipper.RectClip(rect, sub);
      Rect64 solBounds = Clipper.GetBounds(sol);
      Assert.AreEqual(rect.Width, solBounds.Width);
      Assert.AreEqual(rect.Height, solBounds.Height);
    }

    [TestMethod]
    public void TestRectClip2() // #597
    {
      Rect64 rect = new(54690, 0, 65628, 6000);
      Paths64 subject = Ps(P(700000, 6000, 0, 6000, 0, 5925, 700000, 5925));
      Paths64 solution = Clipper.RectClip(rect, subject);
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 4);
    }

    [TestMethod]
    public void TestRectClip3() // #637
    {
      Rect64 r = new(-1800000000L, -137573171L, -1741475021L, 3355443L);
      Paths64 subject = Ps(P(-1800000000L, 10005000L,
        -1800000000L, -5000L, -1789994999L, -5000L, -1789994999L, 10005000L));
      Paths64 solution = Clipper.RectClip(r, subject);
      Assert.AreEqual(1, solution.Count);
    }

    [TestMethod]
    public void TestRectClipOrientation() // #864
    {
      Rect64 rect = new(1222, 1323, 3247, 3348);
      Path64 subject = P(375, 1680, 1915, 4716, 5943, 586, 3987, 152);
      RectClip64 clip = new(rect);
      Paths64 solution = clip.Execute(Ps(subject));
      Assert.AreEqual(1, solution.Count);
      Assert.AreEqual(Clipper.IsPositive(subject), Clipper.IsPositive(solution[0]));
    }
  }
}
