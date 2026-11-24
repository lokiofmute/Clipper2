using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestReuse
  {
    // exact text of the paths, Z included in the USINGZ build
    private static string Exact(Paths64 paths)
    {
      StringBuilder sb = new();
      foreach (Path64 p in paths)
      {
        foreach (Point64 pt in p)
        {
          sb.Append(pt.X).Append(',').Append(pt.Y);
#if USINGZ
          sb.Append(',').Append(pt.Z);
#endif
          sb.Append(' ');
        }
        sb.Append('|');
      }
      return sb.ToString();
    }

    private static Clipper64 NewClipper(Paths64 subject, Paths64 subjectOpen, Paths64 clip)
    {
      Clipper64 c = new();
#if USINGZ
      // a callback that depends on its arguments, so a stale edge would show up in Z
      c.ZCallback = (Point64 b1, Point64 t1, Point64 b2, Point64 t2, ref Point64 ip) =>
        { ip.Z = (1L << 40) | ((b1.X ^ t2.Y) & 0xFFFFF); };
#endif
      c.AddSubject(subject);
      c.AddOpenSubject(subjectOpen);
      c.AddClip(clip);
      return c;
    }

#if USINGZ
    private static void TagZ(Paths64 pp, long kind)
    {
      for (int i = 0; i < pp.Count; ++i)
        for (int j = 0; j < pp[i].Count; ++j)
        {
          Point64 pt = pp[i][j];
          pt.Z = (kind << 40) | ((long) (i + 1) << 20) | (long) j;
          pp[i][j] = pt;
        }
    }
#endif

    [TestMethod]
    public void TestReusedClipperMatchesFreshClipper()
    {
      // a clipper's working objects are released after each Execute, so
      // executing again on the same clipper must give the same results
      for (int testNumber = 1; testNumber <= 100; ++testNumber)
      {
        Paths64 subject = new(), subjectOpen = new(), clip = new();
        if (!TestHelpers.LoadTestNum("Polygons.txt", testNumber, subject, subjectOpen, clip,
          out _, out _, out _, out FillRule fr)) break;
#if USINGZ
        TagZ(subject, 1); TagZ(subjectOpen, 2); TagZ(clip, 3);
#endif
        Clipper64 reused = NewClipper(subject, subjectOpen, clip);

        for (int i = 1; i <= 4; ++i)
        {
          ClipType clipType = (ClipType) i;
          string msg = " in test " + testNumber + " " + clipType;
          Clipper64 fresh = NewClipper(subject, subjectOpen, clip);
          Clipper64 freshTree = NewClipper(subject, subjectOpen, clip);

          Paths64 closed = new(), open = new(), freshClosed = new(), freshOpen = new();
          reused.Execute(clipType, fr, closed, open);
          fresh.Execute(clipType, fr, freshClosed, freshOpen);
          Assert.AreEqual(Exact(freshClosed), Exact(closed), msg);
          Assert.AreEqual(Exact(freshOpen), Exact(open), msg);

          PolyTree64 tree = new(), freshTreeResult = new();
          reused.Execute(clipType, fr, tree, open);
          freshTree.Execute(clipType, fr, freshTreeResult, freshOpen);
          Assert.AreEqual(Exact(Clipper.PolyTreeToPaths64(freshTreeResult)), Exact(Clipper.PolyTreeToPaths64(tree)), msg);
          Assert.AreEqual(Exact(freshOpen), Exact(open), msg);
        }

        // and after Clear(), with the paths added again (the callback stays set)
        reused.Clear();
        reused.AddSubject(subject);
        reused.AddOpenSubject(subjectOpen);
        reused.AddClip(clip);
        Clipper64 fresh2 = NewClipper(subject, subjectOpen, clip);
        Paths64 again = new(), freshAgain = new();
        reused.Execute(ClipType.Xor, fr, again);
        fresh2.Execute(ClipType.Xor, fr, freshAgain);
        Assert.AreEqual(Exact(freshAgain), Exact(again), " after Clear in test " + testNumber);
      }
    }
  }
}
