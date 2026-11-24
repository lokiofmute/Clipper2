using System.Globalization;
using Clipper2Lib;

// Fuzz harness for Clipper.Triangulate, see README.md.
// usage: TriangulationFuzz <star|region|grid|all|review> <firstSeed> <lastSeed>
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

static PathD Star(int seed)
{
  Random rng = new(seed);
  int n = rng.Next(3, 60);
  double cx = (rng.NextDouble() - 0.5) * (seed % 3 == 0 ? 10000 : 100);
  double cy = (rng.NextDouble() - 0.5) * (seed % 3 == 0 ? 10000 : 100);
  double[] angles = new double[n];
  for (int i = 0; i < n; i++) angles[i] = rng.NextDouble() * 2 * Math.PI;
  Array.Sort(angles);
  PathD p = new(n);
  for (int i = 0; i < n; i++)
  {
    double r = 1 + rng.NextDouble() * 40;
    p.Add(new PointD(Math.Round(cx + r * Math.Cos(angles[i]), 6), Math.Round(cy + r * Math.Sin(angles[i]), 6)));
  }
  return p;
}

static PathsD Region(int seed)
{
  Random rng = new(seed);
  PathsD shapes = new();
  int k = rng.Next(2, 9);
  double ox = (rng.NextDouble() - 0.5) * (seed % 4 == 0 ? 20000 : 50);
  double oy = (rng.NextDouble() - 0.5) * (seed % 4 == 0 ? 20000 : 50);
  for (int s = 0; s < k; s++)
  {
    double cx = ox + rng.NextDouble() * 60, cy = oy + rng.NextDouble() * 60;
    int kind = rng.Next(3);
    PathD p = new();
    if (kind == 0)
    {
      double w = 2 + rng.NextDouble() * 30, h = 2 + rng.NextDouble() * 30;
      p.Add(new PointD(cx, cy)); p.Add(new PointD(cx + w, cy)); p.Add(new PointD(cx + w, cy + h)); p.Add(new PointD(cx, cy + h));
    }
    else
    {
      int n = kind == 1 ? 3 : rng.Next(5, 40);
      double[] ang = new double[n];
      for (int i = 0; i < n; i++) ang[i] = (i + rng.NextDouble() * 0.8) * 2 * Math.PI / n;
      for (int i = 0; i < n; i++)
      {
        double r = 3 + rng.NextDouble() * 25;
        p.Add(new PointD(cx + r * Math.Cos(ang[i]), cy + r * Math.Sin(ang[i])));
      }
    }
    for (int i = 0; i < p.Count; i++) p[i] = new PointD(Math.Round(p[i].x, 3), Math.Round(p[i].y, 3));
    shapes.Add(p);
  }
  return Clipper.BooleanOp(rng.Next(3) == 0 ? ClipType.Xor : ClipType.Union, shapes, new PathsD(), FillRule.NonZero, 6);
}

// CAD-like: unions/differences/xors of grid-aligned rectangles and diamonds
static Paths64 Grid(int seed)
{
  Random rng = new(seed);
  Paths64 subj = new(), clip = new();
  int k = rng.Next(2, 12);
  for (int s = 0; s < k; s++)
  {
    long x = rng.Next(0, 20) * 1000, y = rng.Next(0, 20) * 1000;
    long w = rng.Next(1, 8) * 1000, h = rng.Next(1, 8) * 1000;
    Path64 p = rng.Next(3) == 0
      ? Clipper.MakePath(new long[] { x + w / 2, y, x + w, y + h / 2, x + w / 2, y + h, x, y + h / 2 })
      : Clipper.MakePath(new long[] { x, y, x + w, y, x + w, y + h, x, y + h });
    if (rng.Next(2) == 0) p.Reverse();
    (rng.Next(3) == 0 ? clip : subj).Add(p);
  }
  ClipType ct = (ClipType) rng.Next(1, 5);
  if (ct == ClipType.Intersection) ct = ClipType.Union;
  return Clipper.BooleanOp(ct, subj, clip, FillRule.NonZero);
}

static string? Check(Paths64 region, bool del)
{
  Paths64 tris = new();
  TriangulateResult res = TriangulateResult.fail;
  var task = Task.Run(() => res = Clipper.Triangulate(region, out tris, del));
  if (!task.Wait(5000)) return "HANG";
  if (res != TriangulateResult.success) return "result " + res;
  foreach (Path64 t in tris)
  {
    if (t.Count != 3) return "not a triangle";
    if (InternalClipper.CrossProductSign(t[0], t[1], t[2]) <= 0) return "triangle not positive";
  }
  // the region as Triangulate reads it: an EvenOdd union, collinear vertices kept
  Clipper64 c = new() { PreserveCollinear = true };
  c.AddSubject(region);
  Paths64 rings = new();
  c.Execute(ClipType.Union, FillRule.EvenOdd, rings);
  Int128 sum = 0; foreach (Path64 t in tris) sum += Twice(t);
  Int128 regionArea = 0; foreach (Path64 p in rings) regionArea += Twice(p);
  if (sum != Int128.Abs(regionArea)) return $"coverage sum {sum} region {Int128.Abs(regionArea)}";
  string? chain = ChainMismatch(rings, regionArea < 0, tris);
  if (chain != null) return "coverage " + chain;
  if (del)
  {
    Dictionary<(long, long, long, long), Point64> opp = new();
    foreach (Path64 t in tris)
      for (int i = 0; i < 3; i++)
      {
        Point64 u = t[i], v = t[(i + 1) % 3], w = t[(i + 2) % 3];
        if (opp.TryGetValue((v.X, v.Y, u.X, u.Y), out Point64 b))
        {
          if (InCircle(u, v, w, b) > 0) return $"not Delaunay at ({u.X},{u.Y})-({v.X},{v.Y})";
          opp.Remove((v.X, v.Y, u.X, u.Y));
        }
        else opp[(u.X, u.Y, v.X, v.Y)] = w;
      }
  }
  return null;
}

// Exact coverage. As 1-chains, the triangles' directed edges minus the region's must cancel
// once every edge is split at every vertex lying on it. Then at every point the triangles'
// winding numbers add up to the region's (1 inside, 0 outside), and as every triangle is
// positive, they cover the region exactly once and nothing else. (A Union and Xor of the
// triangles can't decide this: the engine drops slivers under a unit wide, e.g. grid 92063.)
static string? ChainMismatch(Paths64 rings, bool ringsNegative, Paths64 tris)
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

static int InCircle(Point64 a, Point64 b, Point64 c, Point64 d)
{
  System.Numerics.BigInteger a0 = a.X - d.X, a1 = a.Y - d.Y, b0 = b.X - d.X, b1 = b.Y - d.Y, c0 = c.X - d.X, c1 = c.Y - d.Y;
  var a2 = a0 * a0 + a1 * a1; var b2 = b0 * b0 + b1 * b1; var c2 = c0 * c0 + c1 * c1;
  return (a0 * (b1 * c2 - c1 * b2) - b0 * (a1 * c2 - c1 * a2) + c0 * (a1 * b2 - b1 * a2)).Sign;
}

static Int128 Twice(Path64 p)
{
  Int128 s = 0;
  for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (Int128) p[j].X * p[i].Y - (Int128) p[i].X * p[j].Y;
  return s;
}

static Paths64 ToInt(PathsD pp) => Clipper.ScalePaths64(pp, 1e6);

string fam = args[0];
int first = int.Parse(args[1]), last = int.Parse(args[2]);
int bad = 0, runs = 0, fails = 0, skipped = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();

void Run(string name, Paths64 region)
{
  foreach (bool del in new[] { false, true })
  {
    runs++;
    string? err = Check(region, del);
    if (err != null)
    {
      bad++;
      if (err.StartsWith("result fail")) fails++;
      if (bad <= 15) Console.WriteLine($"BAD {name} del {del}: {err}");
    }
  }
}

if (fam == "selftest")
{
  // the coverage check must catch a triangle moved by one unit (area and winding unchanged)
  int caught = 0, tried = 0;
  for (int seed = first; seed <= last; seed++)
  {
    Paths64 g = Grid(seed);
    if (g.Count == 0 || Clipper.Triangulate(g, out Paths64 tris, false) != TriangulateResult.success) continue;
    Clipper64 c = new() { PreserveCollinear = true };
    c.AddSubject(g);
    Paths64 rings = new();
    c.Execute(ClipType.Union, FillRule.EvenOdd, rings);
    Int128 ra = 0; foreach (Path64 p in rings) ra += Twice(p);
    int k = seed % tris.Count;
    tris[k] = Clipper.TranslatePath(tris[k], 1, 0);
    tried++;
    if (ChainMismatch(rings, ra < 0, tris) != null) caught++;
    else Console.WriteLine($"MISSED grid {seed}");
  }
  Console.WriteLine($"selftest: {caught} of {tried} moved triangles caught");
  return;
}
if (fam == "review")
{
  Run("review1", new Paths64 { Clipper.MakePath(new long[] { 14000,4000, 12000,7000, 11286,5929, 10000,7000, 8000,5333, 8000,7000, 6000,7000, 6000,5000,
    5000,4000, 6000,3000, 7000,4000, 7600,4000, 10000,2000, 10857,2714, 12000,1000 }) });
  Run("review15", new Paths64 { Clipper.MakePath(new long[] { 11000,10000, 10000,11000, 11000,12000, 11000,13000, 8000,13000, 8000,12000, 10000,12000, 10000,6000, 11000,6000 }) });
}
for (int seed = first; seed <= last; seed++)
{
  if (fam == "star" || fam == "all")
  {
    PathD p = Star(seed);
    PathsD u = Clipper.Union(new PathsD { p }, new PathsD(), FillRule.EvenOdd, 6);
    if (Math.Abs(Clipper.Area(p)) >= 1e-3 && u.Count == 1 && u[0].Count == p.Count) Run($"star {seed}", ToInt(new PathsD { p }));
    else skipped++;
  }
  if (fam == "region" || fam == "all")
  {
    PathsD r = Region(seed);
    if (r.Count > 0) Run($"region {seed}", ToInt(r));
  }
  if (fam == "grid" || fam == "all")
  {
    Paths64 g = Grid(seed);
    if (g.Count > 0) Run($"grid {seed}", g);
  }
}
Console.WriteLine($"{fam}: runs {runs} bad {bad} (fail results {fails}) skipped {skipped} in {sw.Elapsed.TotalSeconds:F1}s");
