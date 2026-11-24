using System.Numerics;
using Clipper2Lib;

namespace TriangulationBench;

// Exact checks of every case's result, as the fuzz harness does them, so the large inputs
// are checked too: success, positive triangles, exact coverage (the triangles' edges minus
// the region's cancel as chains, once split at every vertex on them), and with Delaunay,
// every shared edge locally Delaunay (BigInteger in-circle test).
public static class Verify
{
  public static int Run(string? filter)
  {
    int bad = 0;
    foreach (Case c in Inputs.All())
    {
      if (filter != null && !c.Name.Contains(filter)) continue;
      foreach (bool del in new[] { false, true })
      {
        string? err = Check(c, del);
        if (err != null) bad++;
        Console.WriteLine($"{c.Name,-40} del {del,-5} {err ?? "ok"}");
      }
    }
    Console.WriteLine(bad == 0 ? "all ok" : $"{bad} bad");
    return bad == 0 ? 0 : 1;
  }

  private static string? Check(Case c, bool del)
  {
    double scale = Math.Pow(10, c.DecPlaces);
    Paths64 region = Clipper.ScalePaths64(c.Paths, scale);
    // through the Paths64 overload: the PathsD one scales the same way, and its triangles
    // would come back through doubles
    if (Clipper.Triangulate(region, out Paths64 tris, del) != TriangulateResult.success) return "not success";
    foreach (Path64 t in tris)
      if (t.Count != 3 || InternalClipper.CrossProductSign(t[0], t[1], t[2]) <= 0) return "bad triangle";

    Clipper64 clipper = new() { PreserveCollinear = true };
    clipper.AddSubject(region);
    Paths64 rings = new();
    clipper.Execute(ClipType.Union, FillRule.EvenOdd, rings);
    Int128 sum = 0, regionArea = 0;
    foreach (Path64 t in tris) sum += Twice(t);
    foreach (Path64 p in rings) regionArea += Twice(p);
    if (sum != Int128.Abs(regionArea)) return $"area {sum} vs {Int128.Abs(regionArea)}";
    string? chain = ChainMismatch(rings, regionArea < 0, tris);
    if (chain != null) return "coverage " + chain;

    if (!del) return null;
    Dictionary<(long, long, long, long), Point64> opp = new();
    foreach (Path64 t in tris)
      for (int i = 0; i < 3; i++)
      {
        Point64 u = t[i], v = t[(i + 1) % 3], w = t[(i + 2) % 3];
        if (opp.Remove((v.X, v.Y, u.X, u.Y), out Point64 b))
        {
          if (InCircle(u, v, w, b) > 0) return $"not Delaunay at ({u.X},{u.Y})-({v.X},{v.Y})";
        }
        else opp[(u.X, u.Y, v.X, v.Y)] = w;
      }
    return null;
  }

  private static string? ChainMismatch(Paths64 rings, bool ringsNegative, Paths64 tris)
  {
    List<(Point64 a, Point64 b, int w)> segs = new();
    foreach (Path64 t in tris)
      for (int i = 0; i < 3; i++) segs.Add((t[i], t[(i + 1) % 3], 1));
    foreach (Path64 r in rings)
      for (int i = 0; i < r.Count; i++) segs.Add((r[i], r[(i + 1) % r.Count], ringsNegative ? 1 : -1));
    List<Point64> verts = segs.Select(s => s.a).Concat(segs.Select(s => s.b))
      .DistinctBy(p => (p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
    Dictionary<(long, long, long, long), int> net = new();
    List<Point64> on = new();
    foreach ((Point64 a, Point64 b, int w) in segs)
    {
      if (a == b) continue;
      long minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
      int lo = 0, hi = verts.Count;
      while (lo < hi) { int mid = (lo + hi) / 2; if (verts[mid].X < minX) lo = mid + 1; else hi = mid; }
      on.Clear();
      on.Add(a);
      for (int k = lo; k < verts.Count && verts[k].X <= maxX; k++)
      {
        Point64 v = verts[k];
        if (v.Y < minY || v.Y > maxY || v == a || v == b) continue;
        if (InternalClipper.CrossProductSign(a, v, b) == 0) on.Add(v);
      }
      on.Add(b);
      on.Sort((u, v) => ((Int128) (u.X - a.X) * (u.X - a.X) + (Int128) (u.Y - a.Y) * (u.Y - a.Y))
        .CompareTo((Int128) (v.X - a.X) * (v.X - a.X) + (Int128) (v.Y - a.Y) * (v.Y - a.Y)));
      for (int k = 0; k + 1 < on.Count; k++)
      {
        Point64 u = on[k], v = on[k + 1];
        bool fwd = u.X < v.X || (u.X == v.X && u.Y < v.Y);
        var key = fwd ? (u.X, u.Y, v.X, v.Y) : (v.X, v.Y, u.X, u.Y);
        net[key] = net.GetValueOrDefault(key) + (fwd ? w : -w);
      }
    }
    foreach (var kv in net)
      if (kv.Value != 0) return $"edge ({kv.Key.Item1},{kv.Key.Item2})-({kv.Key.Item3},{kv.Key.Item4}) left {kv.Value}";
    return null;
  }

  private static int InCircle(Point64 a, Point64 b, Point64 c, Point64 d)
  {
    BigInteger a0 = a.X - d.X, a1 = a.Y - d.Y, b0 = b.X - d.X, b1 = b.Y - d.Y, c0 = c.X - d.X, c1 = c.Y - d.Y;
    BigInteger a2 = a0 * a0 + a1 * a1, b2 = b0 * b0 + b1 * b1, c2 = c0 * c0 + c1 * c1;
    return (a0 * (b1 * c2 - c1 * b2) - b0 * (a1 * c2 - c1 * a2) + c0 * (a1 * b2 - b1 * a2)).Sign;
  }

  private static Int128 Twice(Path64 p)
  {
    Int128 s = 0;
    for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (Int128) p[j].X * p[i].Y - (Int128) p[i].X * p[j].Y;
    return s;
  }
}
