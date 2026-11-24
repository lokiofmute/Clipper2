using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  // C++ test helpers (ClipFileLoad.h, clipper.h test utilities) for the ported tests.
  internal static class TestHelpers
  {
    public static Path64 P(params long[] xy) => Clipper.MakePath(xy);
    public static PathD PD(params double[] xy) => Clipper.MakePath(xy);

    public static Paths64 Ps(params Path64[] paths) => new Paths64(paths);
    public static PathsD PsD(params PathD[] paths) => new PathsD(paths);

    // mirrors C++ LoadTestNum(ifs, num, subj, subj_open, clip, area, count, ct, fr)
    public static bool LoadTestNum(string fileName, int num, Paths64 subj, Paths64 subjOpen,
      Paths64 clip, out long area, out long count, out ClipType ct, out FillRule fr)
    {
      bool ok = ClipperFileIO.LoadTestNum(TestData.Path(fileName), num, subj, subjOpen, clip,
        out ct, out fr, out area, out int cnt, out _);
      count = cnt;
      return ok;
    }

    public static bool IsInList(int num, params int[] list) => Array.IndexOf(list, num) >= 0;

    // exact text of paths, for equality like C++ operator== (Z included in the USINGZ build)
    public static string Exact(Paths64 paths)
    {
      System.Text.StringBuilder sb = new();
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

    public static double Distance(PointD a, PointD b) =>
      Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    public static double Distance(Point64 a, Point64 b) => Math.Sqrt(Clipper.DistanceSqr(a, b));

    public static double Length(Path64 path, bool isClosed = false)
    {
      double result = 0;
      if (path.Count < 2) return result;
      for (int i = 1; i < path.Count; i++) result += Distance(path[i - 1], path[i]);
      if (isClosed) result += Distance(path[^1], path[0]);
      return result;
    }

    // C++ details::PolyPath64ContainsChildren / CheckPolytreeFullyContainsChildren
    private static bool PolyPathContainsChildren(PolyPath64 pp)
    {
      foreach (PolyPath64 child in pp)
      {
        // a child may share vertices with its parent, but never lies outside it
        int outsideCnt = 0;
        foreach (Point64 pt in child.Polygon!)
        {
          PointInPolygonResult result = Clipper.PointInPolygon(pt, pp.Polygon!);
          if (result == PointInPolygonResult.IsInside) --outsideCnt;
          else if (result == PointInPolygonResult.IsOutside) ++outsideCnt;
          if (outsideCnt > 1) return false;
          if (outsideCnt < -1) break;
        }
        if (child.Count > 0 && !PolyPathContainsChildren(child)) return false;
      }
      return true;
    }

    public static bool CheckPolytreeFullyContainsChildren(PolyTree64 polytree)
    {
      foreach (PolyPath64 child in polytree)
        if (child.Count > 0 && !PolyPathContainsChildren(child)) return false;
      return true;
    }

    private static void PolyPathContainsPoint(PolyPath64 pp, Point64 pt, ref int counter)
    {
      if (pp.Polygon != null && pp.Polygon.Count > 0 &&
        Clipper.PointInPolygon(pt, pp.Polygon) != PointInPolygonResult.IsOutside)
      {
        if (pp.IsHole) --counter; else ++counter;
      }
      foreach (PolyPath64 child in pp) PolyPathContainsPoint(child, pt, ref counter);
    }

    public static bool PolytreeContainsPoint(PolyPath64 pp, Point64 pt)
    {
      int counter = 0;
      foreach (PolyPath64 child in pp) PolyPathContainsPoint(child, pt, ref counter);
      Assert.IsTrue(counter >= 0, "point inside more holes than outers"); // pt can't be inside more holes than outers
      return counter != 0;
    }

    private static void GetPolyPathArea(PolyPath64 pp, ref double area)
    {
      area += Clipper.Area(pp.Polygon!);
      foreach (PolyPath64 child in pp) GetPolyPathArea(child, ref area);
    }

    public static double GetPolytreeArea(PolyPath64 pp)
    {
      double result = 0;
      foreach (PolyPath64 child in pp) GetPolyPathArea(child, ref result);
      return result;
    }
  }
}
