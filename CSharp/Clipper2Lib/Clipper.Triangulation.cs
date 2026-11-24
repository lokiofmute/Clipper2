/*******************************************************************************
* Purpose   :  Constrained (Delaunay) triangulation of polygons with holes     *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* This replaces the sweep-line triangulation that upstream Clipper2 2.0.0      *
* shipped (and the C++ removed in 1d16960). That sweep returned success with   *
* overlapping triangles on many simple polygons with collinear vertices, and   *
* with Delaunay on it could flip edges forever (NodeFlow N972). This version:  *
*  1. normalizes the input with a Union into a PolyTree (outers and holes),    *
*  2. ear clips each outer with its holes bridged in, using exact predicates,  *
*  3. checks exactly that the triangles' areas add up to the region's area,    *
*  4. with useDelaunay, flips edges (Lawson) using the exact in-circle test,   *
*     which always terminates.                                                 *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Clipper2Lib
{
  public enum TriangulateResult { success, fail, noPolygons, pathsIntersect }

  internal class Delaunay
  {
    private readonly bool useDelaunay;

    public Delaunay(bool delaunay = true)
    {
      useDelaunay = delaunay;
    }

    // A vertex in an ear clipping ring
    private sealed class Node
    {
      public Point64 pt;
      public Node prev = null!;
      public Node next = null!;
      public bool merged;  // while bridging: part of the outer ring (holes bridged in so far)
      public bool removed; // while ear clipping: no longer in the ring
      public int stamp;    // while ear clipping: the last IsEar query that visited its edge
      public Node(Point64 pt) { this.pt = pt; }
    }

    // A uniform grid over a bounding box, with about one cell per vertex, for finding what
    // lies near a point or a segment
    private sealed class Grid
    {
      private readonly long left, top, size;
      public readonly int cols, rows;

      public Grid(long left, long top, long right, long bottom, int n)
      {
        this.left = left;
        this.top = top;
        double w = (double) right - left + 1, h = (double) bottom - top + 1;
        double s = Math.Max(1.0, Math.Ceiling(Math.Sqrt(w * h / Math.Max(n, 1))));
        // a very thin box would get far more cells than vertices
        while ((Math.Floor(w / s) + 1) * (Math.Floor(h / s) + 1) > 2.0 * n + 64) s *= 2;
        size = (long) s;
        cols = (int) ((right - left) / size) + 1;
        rows = (int) ((bottom - top) / size) + 1;
      }

      public int Cells => cols * rows;
      public long Size => size;
      public int Col(long x) => (int) Math.Clamp((x - left) / size, 0, cols - 1);
      public int Row(long y) => (int) Math.Clamp((y - top) / size, 0, rows - 1);
      public int Cell(Point64 pt) => Row(pt.Y) * cols + Col(pt.X);

      private int RowAt(double y) => (int) Math.Clamp(Math.Floor(y / size), 0, rows - 1);
      private int ColAt(double x) => (int) Math.Clamp(Math.Floor(x / size), 0, cols - 1);

      // every cell the closed triangle (a, b, c) overlaps, and some next to them: per row of
      // cells, the triangle's x range in that row is found in double, with a cell of slack on
      // each side for its rounding
      public void TriangleCells(Point64 a, Point64 b, Point64 c, List<int> cells)
      {
        cells.Clear();
        int r0 = Row(Math.Min(a.Y, Math.Min(b.Y, c.Y))), r1 = Row(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
        int c0 = Col(Math.Min(a.X, Math.Min(b.X, c.X))), c1 = Col(Math.Max(a.X, Math.Max(b.X, c.X)));
        if (r0 == r1 || c1 - c0 <= 1)
        {
          for (int r = r0; r <= r1; r++)
            for (int col = c0; col <= c1; col++) cells.Add(r * cols + col);
          return;
        }
        // offsets from the grid's corner, so they stay small enough for doubles
        double ax = a.X - left, ay = a.Y - top, bx = b.X - left, by = b.Y - top, cx = c.X - left, cy = c.Y - top;
        for (int r = r0; r <= r1; r++)
        {
          double y1 = (double) r * size, y2 = (double) (r + 1) * size;
          double lo = double.MaxValue, hi = double.MinValue;
          ClipToRow(ax, ay, bx, by, y1, y2, ref lo, ref hi);
          ClipToRow(bx, by, cx, cy, y1, y2, ref lo, ref hi);
          ClipToRow(cx, cy, ax, ay, y1, y2, ref lo, ref hi);
          if (lo > hi) continue;
          int ca = Math.Max(c0, ColAt(lo) - 1), cb = Math.Min(c1, ColAt(hi) + 1);
          for (int col = ca; col <= cb; col++) cells.Add(r * cols + col);
        }
      }

      // widens lo..hi by the x range of segment p-q within y1..y2
      private static void ClipToRow(double px, double py, double qx, double qy, double y1, double y2,
        ref double lo, ref double hi)
      {
        if (Math.Max(py, qy) < y1 || Math.Min(py, qy) > y2) return;
        double xa, xb;
        if (py == qy) { xa = px; xb = qx; }
        else
        {
          double ta = Math.Clamp((y1 - py) / (qy - py), 0, 1), tb = Math.Clamp((y2 - py) / (qy - py), 0, 1);
          xa = px + ta * (qx - px);
          xb = px + tb * (qx - px);
        }
        lo = Math.Min(lo, Math.Min(xa, xb));
        hi = Math.Max(hi, Math.Max(xa, xb));
      }

      // every cell segment a-b passes through, and some next to them: the line is followed
      // in double, with a cell of slack on each side for its rounding
      public void SegmentCells(Point64 a, Point64 b, List<int> cells)
      {
        cells.Clear();
        int c0 = Col(Math.Min(a.X, b.X)), c1 = Col(Math.Max(a.X, b.X));
        int r0 = Row(Math.Min(a.Y, b.Y)), r1 = Row(Math.Max(a.Y, b.Y));
        if (c1 - c0 <= 1 || r1 - r0 <= 1)
        {
          for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++) cells.Add(r * cols + c);
          return;
        }
        // offsets from the grid's corner, so they stay small enough for doubles
        double ax = a.X - left, ay = a.Y - top, dx = (double) (b.X - a.X), dy = (double) (b.Y - a.Y);
        if (c1 - c0 >= r1 - r0)
        {
          double lo = Math.Min(ax, ax + dx), hi = Math.Max(ax, ax + dx);
          for (int c = c0; c <= c1; c++)
          {
            double x1 = Math.Max(lo, (double) c * size), x2 = Math.Min(hi, (double) (c + 1) * size);
            double y1 = ay + (x1 - ax) * dy / dx, y2 = ay + (x2 - ax) * dy / dx;
            int ra = Math.Max(r0, RowAt(Math.Min(y1, y2)) - 1), rb = Math.Min(r1, RowAt(Math.Max(y1, y2)) + 1);
            for (int r = ra; r <= rb; r++) cells.Add(r * cols + c);
          }
        }
        else
        {
          double lo = Math.Min(ay, ay + dy), hi = Math.Max(ay, ay + dy);
          for (int r = r0; r <= r1; r++)
          {
            double y1 = Math.Max(lo, (double) r * size), y2 = Math.Min(hi, (double) (r + 1) * size);
            double x1 = ax + (y1 - ay) * dx / dy, x2 = ax + (y2 - ay) * dx / dy;
            int ca = Math.Max(c0, ColAt(Math.Min(x1, x2)) - 1), cb = Math.Min(c1, ColAt(Math.Max(x1, x2)) + 1);
            for (int c = ca; c <= cb; c++) cells.Add(r * cols + c);
          }
        }
      }
    }

    // a linked list of items per grid cell
    private sealed class CellLists<T>
    {
      private readonly int[] head;
      private int[] link;
      private T[] items;
      private int count;

      public CellLists(int cells, int capacity)
      {
        head = new int[cells];
        Array.Fill(head, -1);
        link = new int[Math.Max(capacity, 4)];
        items = new T[link.Length];
      }

      public void Add(int cell, T item)
      {
        if (count == link.Length)
        {
          Array.Resize(ref link, count * 2);
          Array.Resize(ref items, count * 2);
        }
        items[count] = item;
        link[count] = head[cell];
        head[cell] = count++;
      }

      public int First(int cell) => head[cell];
      public int Next(int entry) => link[entry];
      public T this[int entry] => items[entry];

      // takes entry out of its cell's list, where prev is the entry before it (or -1)
      public void Unlink(int cell, int prev, int entry)
      {
        if (prev < 0) head[cell] = link[entry];
        else link[prev] = link[entry];
      }
    }

    internal TriangulateResult Execute(Paths64 paths, out Paths64 sol)
    {
      sol = new Paths64();

      // 1. drop paths without area, then normalize: a Union gives non-intersecting outers
      // and holes, nested in a PolyTree, whatever the orientation of the input paths
      Paths64 input = new Paths64(paths.Count);
      foreach (Path64 path in paths)
      {
        Path64 p = Clipper.StripDuplicates(path, true);
        if (p.Count >= 3 && Clipper.Area(p) != 0) input.Add(p);
      }
      if (input.Count == 0) return TriangulateResult.noPolygons;

      Clipper64 clipper = new Clipper64 { PreserveCollinear = true };
      clipper.AddSubject(input);
      PolyTree64 tree = new PolyTree64();
      if (!clipper.Execute(ClipType.Union, FillRule.EvenOdd, tree)) return TriangulateResult.fail;
      if (tree.Count == 0) return TriangulateResult.noPolygons;

      // paths that cross each other (beyond rounding) aren't a valid input
      if (HasCrossings(input, tree)) return TriangulateResult.pathsIntersect;

      // 2. ear clip each outer with its holes
      List<Point64[]> triangles = new List<Point64[]>();
      Int128 twiceRegionArea = 0;
      Stack<PolyPath64> outers = new Stack<PolyPath64>();
      foreach (PolyPath64 outer in tree) outers.Push(outer);
      while (outers.Count > 0)
      {
        PolyPath64 outer = outers.Pop();
        List<Path64> holes = new List<Path64>(outer.Count);
        foreach (PolyPath64 hole in outer)
        {
          holes.Add(hole.Polygon!);
          foreach (PolyPath64 island in hole) outers.Push(island);
        }
        twiceRegionArea += TwiceArea(outer.Polygon!);
        foreach (Path64 hole in holes) twiceRegionArea -= Int128.Abs(TwiceArea(hole));
        if (!EarClip(outer.Polygon!, holes, triangles)) return TriangulateResult.fail;
      }

      // 3. the triangles must cover the region exactly
      Int128 twiceTriangleArea = 0;
      foreach (Point64[] t in triangles)
      {
        if (InternalClipper.CrossProductSign(t[0], t[1], t[2]) <= 0) return TriangulateResult.fail;
        twiceTriangleArea += TwiceArea(t);
      }
      if (twiceTriangleArea != Int128.Abs(twiceRegionArea)) return TriangulateResult.fail;

      // 4. Delaunay
      if (useDelaunay) Legalize(triangles);

      sol = new Paths64(triangles.Count);
      foreach (Point64[] t in triangles)
        sol.Add(new Path64(3) { t[0], t[1], t[2] });
      return TriangulateResult.success;
    }

    private static Int128 TwiceArea(IReadOnlyList<Point64> path)
    {
      Int128 sum = 0;
      for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        sum += (Int128) path[j].X * path[i].Y - (Int128) path[i].X * path[j].Y;
      return sum;
    }

    // true when the union made a vertex further than rounding (1 unit) from any input
    // vertex, which only a crossing does
    private static bool HasCrossings(Paths64 input, PolyTree64 tree)
    {
      HashSet<(long, long)> inputPts = new HashSet<(long, long)>();
      foreach (Path64 p in input)
        foreach (Point64 pt in p) inputPts.Add((pt.X, pt.Y));

      Paths64 output = Clipper.PolyTreeToPaths64(tree);
      foreach (Path64 p in output)
        foreach (Point64 pt in p)
        {
          if (inputPts.Contains((pt.X, pt.Y))) continue;
          bool near = false;
          foreach ((long x, long y) in inputPts)
            if (Math.Abs(x - pt.X) <= 1 && Math.Abs(y - pt.Y) <= 1) { near = true; break; }
          if (!near) return true;
        }
      return false;
    }

    // ---------------------------------------------------------------------
    // Ear clipping. Every ring keeps the region on its left: outers wind
    // counter-clockwise (positive area) and holes clockwise.
    // ---------------------------------------------------------------------

    private static Node? MakeRing(Path64 path, bool positive)
    {
      bool isPositive = TwiceArea(path) > 0;
      Node? first = null, last = null;
      for (int k = 0; k < path.Count; k++)
      {
        Point64 pt = path[(isPositive == positive) ? k : path.Count - 1 - k];
        Node n = new Node(pt);
        if (first == null) first = n;
        else
        {
          last!.next = n;
          n.prev = last;
        }
        last = n;
      }
      if (first == null) return null;
      last!.next = first;
      first.prev = last;
      return first;
    }

    // Inserts into each edge every vertex (of any of these rings) that lies strictly inside
    // it, so a ring that touches an edge (a hole touching its outer, or a ring touching
    // itself) shares a vertex there, which bridging and ear clipping can handle.
    private static List<Path64> Conform(List<Path64> rings)
    {
      List<Point64> all = new List<Point64>();
      foreach (Path64 r in rings) all.AddRange(r);
      all.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));

      List<Path64> result = new List<Path64>(rings.Count);
      List<Point64> inside = new List<Point64>();
      foreach (Path64 r in rings)
      {
        Path64 res = new Path64(r.Count);
        for (int i = 0; i < r.Count; i++)
        {
          Point64 p = r[i], q = r[(i + 1) % r.Count];
          res.Add(p);
          long minX = Math.Min(p.X, q.X), maxX = Math.Max(p.X, q.X);
          long minY = Math.Min(p.Y, q.Y), maxY = Math.Max(p.Y, q.Y);
          inside.Clear();
          // the vertices are sorted by X, so only those in the edge's X range are visited
          int lo = 0, hi = all.Count;
          while (lo < hi)
          {
            int mid = (lo + hi) / 2;
            if (all[mid].X < minX) lo = mid + 1; else hi = mid;
          }
          for (int k = lo; k < all.Count && all[k].X <= maxX; k++)
          {
            Point64 v = all[k];
            if (v.Y < minY || v.Y > maxY || v == p || v == q) continue;
            if (InternalClipper.CrossProductSign(p, v, q) != 0) continue;
            if (inside.Count == 0 || inside[^1] != v) inside.Add(v);
          }
          if (inside.Count == 0) continue;
          inside.Sort((u, v) =>
          {
            Int128 du = (Int128) (u.X - p.X) * (u.X - p.X) + (Int128) (u.Y - p.Y) * (u.Y - p.Y);
            Int128 dv = (Int128) (v.X - p.X) * (v.X - p.X) + (Int128) (v.Y - p.Y) * (v.Y - p.Y);
            return du.CompareTo(dv);
          });
          for (int k = 0; k < inside.Count; k++)
            if (k == 0 || inside[k] != inside[k - 1]) res.Add(inside[k]);
        }
        result.Add(res);
      }
      return result;
    }

    // Splits a ring at every point it passes more than once, into loops that don't
    private static void Decompose(Path64 ring, List<Path64> loops)
    {
      List<Point64> cur = new List<Point64>(ring.Count);
      Dictionary<(long, long), int> at = new Dictionary<(long, long), int>();
      foreach (Point64 pt in ring)
      {
        if (at.TryGetValue((pt.X, pt.Y), out int i))
        {
          // cur[i] == pt: cur[i .. end] is a loop of its own
          Path64 loop = new Path64(cur.Count - i);
          for (int k = i; k < cur.Count; k++)
          {
            loop.Add(cur[k]);
            if (k > i) at.Remove((cur[k].X, cur[k].Y));
          }
          loops.Add(loop);
          cur.RemoveRange(i + 1, cur.Count - i - 1);
        }
        else
        {
          at.Add((pt.X, pt.Y), cur.Count);
          cur.Add(pt);
        }
      }
      loops.Add(new Path64(cur));
    }

    // true when hole lies inside outer: decided by the first hole vertex that isn't on outer's
    // edge (a hole touching its outer everywhere counts as inside)
    private static bool HoleInside(Path64 hole, Path64 outer)
    {
      foreach (Point64 pt in hole)
      {
        PointInPolygonResult pip = InternalClipper.PointInPolygon(pt, outer);
        if (pip != PointInPolygonResult.IsOn) return pip == PointInPolygonResult.IsInside;
      }
      return true;
    }

    // Splits one outer and its holes into simple outers with their simple holes: rings are
    // made to share a vertex wherever one touches another's edge, then split at every point
    // they pass twice. Loops of positive area are outers, negative ones holes, and each hole
    // goes to the smallest outer around it.
    private static bool EarClip(Path64 outer, List<Path64> holes, List<Point64[]> triangles)
    {
      List<Path64> rings = Conform(new List<Path64>(holes) { outer });
      List<Path64> loops = new List<Path64>();
      foreach (Path64 r in rings) Decompose(r, loops);

      List<(Path64 path, Int128 area, List<Path64> holes)> outers = new List<(Path64, Int128, List<Path64>)>();
      List<Path64> loopHoles = new List<Path64>();
      foreach (Path64 loop in loops)
      {
        if (loop.Count < 3) continue;
        Int128 a = TwiceArea(loop);
        if (a > 0) outers.Add((loop, a, new List<Path64>()));
        else if (a < 0) loopHoles.Add(loop);
      }
      outers.Sort((x, y) => x.area.CompareTo(y.area));
      foreach (Path64 h in loopHoles)
      {
        Rect64 hb = Clipper.GetBounds(h);
        bool placed = false;
        foreach ((Path64 path, Int128 _, List<Path64> its) in outers)
        {
          Rect64 ob = Clipper.GetBounds(path);
          if (hb.left < ob.left || hb.right > ob.right || hb.top < ob.top || hb.bottom > ob.bottom) continue;
          if (!HoleInside(h, path)) continue;
          its.Add(h);
          placed = true;
          break;
        }
        if (!placed) return false;
      }

      foreach ((Path64 path, Int128 _, List<Path64> its) in outers)
        if (!EarClipSimple(path, its, triangles)) return false;
      return true;
    }

    private static bool EarClipSimple(Path64 outer, List<Path64> holes, List<Point64[]> triangles)
    {
      Node? ring = MakeRing(outer, true);
      if (ring == null) return true;

      if (holes.Count > 0)
      {
        List<Node> rings = new List<Node> { ring };
        List<Node> holeStarts = new List<Node>(holes.Count);
        foreach (Path64 hole in holes)
        {
          Node? h = MakeRing(hole, false);
          if (h == null) continue;
          rings.Add(h);
          holeStarts.Add(Leftmost(h));
        }
        BridgeIndex index = new BridgeIndex(rings);
        // bridge holes from left to right, so a hole's bridge never meets an unbridged hole
        holeStarts.Sort((a, b) => a.pt.X != b.pt.X ? a.pt.X.CompareTo(b.pt.X) : a.pt.Y.CompareTo(b.pt.Y));
        foreach (Node h in holeStarts)
        {
          if (index.FindBridge(h) is not (Node bridge, Node hv)) return false;
          index.Merge(hv);
          (Node a2, Node b2) = SplitRing(bridge, hv);
          index.AddBridge(bridge, hv, a2, b2);
        }
      }

      // where the ring touches itself, separate pieces that only share a point are clipped
      // on their own
      foreach (Node piece in SplitPinches(ring))
        if (!ClipEars(piece, triangles)) return false;
      return true;
    }

    // Splits a ring wherever it passes the same point twice and both loops there enclose
    // area (pieces touching at a point). Loops without area are dropped. A loop of negative
    // area is a hole attached at that point, so the ring isn't split there.
    private static List<Node> SplitPinches(Node ring)
    {
      List<Node> done = new List<Node>();
      Stack<Node> todo = new Stack<Node>();
      todo.Push(ring);
      List<Node> nodes = new List<Node>();
      List<Int128> prefix = new List<Int128>();
      List<int> sameNext = new List<int>(), groupFirst = new List<int>(), groupLast = new List<int>();
      Dictionary<(long, long), int> group = new Dictionary<(long, long), int>();
      while (todo.Count > 0)
      {
        Node start = todo.Pop();
        // the nodes in ring order, with the twice-area of the edges before each (prefix[i]
        // runs from nodes[0] to nodes[i]), and the nodes at each position chained in ring
        // order, positions in the order they're first met
        nodes.Clear(); prefix.Clear(); sameNext.Clear(); groupFirst.Clear(); groupLast.Clear();
        group.Clear();
        Int128 sum = 0;
        Node p = start;
        do
        {
          int i = nodes.Count;
          nodes.Add(p);
          prefix.Add(sum);
          sum += (Int128) p.pt.X * p.next.pt.Y - (Int128) p.next.pt.X * p.pt.Y;
          sameNext.Add(-1);
          if (group.TryGetValue((p.pt.X, p.pt.Y), out int g))
          {
            sameNext[groupLast[g]] = i;
            groupLast[g] = i;
          }
          else
          {
            group.Add((p.pt.X, p.pt.Y), groupFirst.Count);
            groupFirst.Add(i);
            groupLast.Add(i);
          }
          p = p.next;
        } while (p != start);

        bool split = false;
        foreach (int first in groupFirst)
        {
          for (int i = first; i >= 0 && !split; i = sameNext[i])
            for (int j = sameNext[i]; j >= 0 && !split; j = sameNext[j])
            {
              Node a = nodes[i], b = nodes[j];
              // a and b share a position, so the loop a .. b.prev closed back to a has the
              // twice-area of the edges from a to b, and the loop b .. a.prev the rest
              Int128 area1 = prefix[j] - prefix[i], area2 = sum - area1;
              if (area1 < 0 || area2 < 0) continue;
              // loop 1: a .. b.prev, loop 2: b .. a.prev
              Node aPrev = a.prev, bPrev = b.prev;
              a.prev = bPrev; bPrev.next = a;
              b.prev = aPrev; aPrev.next = b;
              if (area1 > 0) todo.Push(a);
              if (area2 > 0) todo.Push(b);
              split = true;
            }
          if (split) break;
        }
        if (!split) done.Add(start);
      }
      return done;
    }

    private static Node Leftmost(Node start)
    {
      Node best = start, p = start.next;
      while (p != start)
      {
        if (p.pt.X < best.pt.X || (p.pt.X == best.pt.X && p.pt.Y < best.pt.Y)) best = p;
        p = p.next;
      }
      return best;
    }

    // true when the direction from v to q points into the region at v
    // (the region lies left of the edges prev->v and v->next)
    private static bool LocallyInside(Node v, Point64 q)
    {
      Point64 a = v.prev.pt, b = v.next.pt, p = v.pt;
      int turn = InternalClipper.CrossProductSign(a, p, b);
      bool leftOfOut = InternalClipper.CrossProductSign(p, b, q) > 0;
      bool leftOfIn = InternalClipper.CrossProductSign(a, p, q) > 0;
      return (turn > 0) ? (leftOfOut && leftOfIn) : (leftOfOut || leftOfIn);
    }

    // The outer ring's vertices and every ring's edges in grids, for bridging holes:
    // FindBridge gives the nearest vertex of the outer ring (holes bridged in so far) that a
    // hole vertex can be joined to without crossing anything.
    private sealed class BridgeIndex
    {
      private readonly Grid grid;
      private readonly CellLists<Node> vertices;
      private readonly CellLists<int> edgeCells;
      private readonly List<(Point64 c, Point64 d)> edges;
      private int[] seen;
      private int stamp;
      private readonly List<int> cells = new List<int>();
      private readonly PriorityQueue<Node, (Int128 d, long x, long y)> queue =
        new PriorityQueue<Node, (Int128, long, long)>();

      // rings[0] is the outer ring, the others are holes
      public BridgeIndex(List<Node> rings)
      {
        long left = long.MaxValue, top = long.MaxValue, right = long.MinValue, bottom = long.MinValue;
        int n = 0;
        foreach (Node start in rings)
        {
          Node p = start;
          do
          {
            left = Math.Min(left, p.pt.X); right = Math.Max(right, p.pt.X);
            top = Math.Min(top, p.pt.Y); bottom = Math.Max(bottom, p.pt.Y);
            n++;
            p = p.next;
          } while (p != start);
        }
        grid = new Grid(left, top, right, bottom, n);
        vertices = new CellLists<Node>(grid.Cells, n + 2 * rings.Count);
        edgeCells = new CellLists<int>(grid.Cells, 3 * n);
        edges = new List<(Point64, Point64)>(n + rings.Count);
        foreach (Node start in rings)
        {
          Node p = start;
          do
          {
            p.merged = start == rings[0];
            vertices.Add(grid.Cell(p.pt), p);
            AddEdge(p.pt, p.next.pt);
            p = p.next;
          } while (p != start);
        }
        seen = new int[edges.Count + rings.Count];
      }

      private void AddEdge(Point64 c, Point64 d)
      {
        int id = edges.Count;
        edges.Add((c, d));
        grid.SegmentCells(c, d, cells);
        foreach (int cell in cells) edgeCells.Add(cell, id);
      }

      // hole h becomes part of the outer ring
      public void Merge(Node h)
      {
        Node p = h;
        do
        {
          p.merged = true;
          p = p.next;
        } while (p != h);
      }

      // after SplitRing(a, b): the copies a2 and b2 are new outer ring vertices, and the bridge
      // a-b is a new edge (the ring's other edges keep their positions)
      public void AddBridge(Node a, Node b, Node a2, Node b2)
      {
        a2.merged = b2.merged = true;
        vertices.Add(grid.Cell(a2.pt), a2);
        vertices.Add(grid.Cell(b2.pt), b2);
        if (a.pt != b.pt) AddEdge(a.pt, b.pt);
      }

      // Returns the outer ring vertex to join and the hole vertex to join it to, which is h
      // unless the hole touches the outer ring at another vertex.
      public (Node bridge, Node hole)? FindBridge(Node h)
      {
        // the hole touches the outer ring at a vertex, both of its edges there inside the
        // outer's corner: join it there, by a zero length bridge. The ring then passes that
        // point as the region does; joined anywhere else it would cross itself there.
        Node q = h;
        do
        {
          for (int e = vertices.First(grid.Cell(q.pt)); e >= 0; e = vertices.Next(e))
          {
            Node p = vertices[e];
            if (p.merged && p.pt == q.pt && LocallyInside(p, q.next.pt) && LocallyInside(p, q.prev.pt)) return (p, q);
          }
          q = q.next;
        } while (q != h);

        // the hole touches the outer here: a zero length bridge
        int cell = grid.Cell(h.pt);
        for (int e = vertices.First(cell); e >= 0; e = vertices.Next(e))
        {
          Node p = vertices[e];
          if (p.merged && p.pt == h.pt && (LocallyInside(p, h.next.pt) || LocallyInside(p, h.prev.pt))) return (p, h);
        }

        // the outer ring's vertices from nearest to furthest: after the cells within r of h's
        // cell, every vertex not yet queued is further than r cells away
        queue.Clear();
        int hc = grid.Col(h.pt.X), hr = grid.Row(h.pt.Y);
        int maxR = Math.Max(Math.Max(hc, grid.cols - 1 - hc), Math.Max(hr, grid.rows - 1 - hr));
        for (int r = 0; r <= maxR; r++)
        {
          // the square ring of cells at distance r: its top and bottom rows, then its sides
          int c0 = Math.Max(hc - r, 0), c1 = Math.Min(hc + r, grid.cols - 1);
          if (hr - r >= 0)
            for (int col = c0; col <= c1; col++) QueueCell(h, (hr - r) * grid.cols + col);
          if (r > 0 && hr + r < grid.rows)
            for (int col = c0; col <= c1; col++) QueueCell(h, (hr + r) * grid.cols + col);
          if (r > 0)
            for (int row = Math.Max(hr - r + 1, 0); row <= Math.Min(hr + r - 1, grid.rows - 1); row++)
            {
              if (hc - r >= 0) QueueCell(h, row * grid.cols + hc - r);
              if (hc + r < grid.cols) QueueCell(h, row * grid.cols + hc + r);
            }
          Int128 reach = (Int128) r * grid.Size;
          Int128 bound = r == maxR ? Int128.MaxValue : reach * reach;
          while (queue.TryPeek(out Node? n, out (Int128 d, long, long) key) && key.d <= bound)
          {
            queue.Dequeue();
            if (!LocallyInside(n, h.pt) || !LocallyInside(h, n.pt)) continue;
            if (SegmentBlocked(h.pt, n.pt)) continue;
            return (n, h);
          }
        }
        return null;
      }

      private void QueueCell(Node h, int cell)
      {
        for (int e = vertices.First(cell); e >= 0; e = vertices.Next(e))
        {
          Node p = vertices[e];
          if (!p.merged || p.pt == h.pt) continue;
          Int128 dx = p.pt.X - h.pt.X, dy = p.pt.Y - h.pt.Y;
          queue.Enqueue(p, (dx * dx + dy * dy, p.pt.X, p.pt.Y));
        }
      }

      // true when segment a-b crosses an edge of any ring or passes through a vertex
      // (shared end positions don't count)
      private bool SegmentBlocked(Point64 a, Point64 b)
      {
        long minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        long minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        if (seen.Length < edges.Count) Array.Resize(ref seen, edges.Count * 2);
        if (++stamp == int.MaxValue)
        {
          Array.Clear(seen);
          stamp = 1;
        }
        grid.SegmentCells(a, b, cells);
        foreach (int cell in cells)
          for (int e = edgeCells.First(cell); e >= 0; e = edgeCells.Next(e))
          {
            int id = edgeCells[e];
            if (seen[id] == stamp) continue;
            seen[id] = stamp;
            (Point64 c, Point64 d) = edges[id];
            if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX ||
                Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY) continue;
            if (c != a && c != b && IsOnSegment(c, a, b)) return true;
            if (c == a || c == b || d == a || d == b) continue;
            int s1 = InternalClipper.CrossProductSign(a, b, c);
            int s2 = InternalClipper.CrossProductSign(a, b, d);
            if (s1 * s2 >= 0) continue;
            int s3 = InternalClipper.CrossProductSign(c, d, a);
            int s4 = InternalClipper.CrossProductSign(c, d, b);
            if (s3 * s4 < 0) return true;
          }
        return false;
      }
    }

    private static bool IsOnSegment(Point64 pt, Point64 a, Point64 b)
    {
      return InternalClipper.CrossProductSign(a, pt, b) == 0 &&
        pt.X >= Math.Min(a.X, b.X) && pt.X <= Math.Max(a.X, b.X) &&
        pt.Y >= Math.Min(a.Y, b.Y) && pt.Y <= Math.Max(a.Y, b.Y);
    }

    // joins ring node a to node b of another ring by a two-way bridge, and returns the copies
    // of a and b it makes
    private static (Node a2, Node b2) SplitRing(Node a, Node b)
    {
      Node a2 = new Node(a.pt), b2 = new Node(b.pt);
      Node an = a.next, bp = b.prev;
      a.next = b; b.prev = a;
      a2.next = an; an.prev = a2;
      b2.next = a2; a2.prev = b2;
      bp.next = b2; b2.prev = bp;
      return (a2, b2);
    }

    // true when the triangle (a, b, c) at node ear can be clipped: it is convex, no other
    // vertex lies inside it or on its edges, no edge crosses its diagonal a-c, and no edge
    // from a copy of one of its corners (where a ring touches itself, or at a bridge) enters it
    private static bool IsEar(Node ear, EarIndex? index)
    {
      Point64 a = ear.prev.pt, b = ear.pt, c = ear.next.pt;
      if (InternalClipper.CrossProductSign(a, b, c) <= 0) return false; // reflex or flat
#if TRI_CHECK
      if (index != null)
      {
        bool fast = index.IsEar(ear);
        if (fast != IsEar(ear, null)) throw new InvalidOperationException($"EarIndex disagrees at {b.X},{b.Y}");
        return fast;
      }
#else
      if (index != null) return index.IsEar(ear);
#endif

      long minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
      long minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
      for (Node p = ear.next.next; p != ear.prev; p = p.next)
      {
        if (CrossesDiagonal(a, c, p.pt, p.next.pt, minX, maxX, minY, maxY)) return false;
        if (BlocksEar(a, b, c, p, minX, maxX, minY, maxY)) return false;
      }
      return true;
    }

    // true when edge q-r crosses the diagonal a-c of an ear with bounds min/maxX, min/maxY
    private static bool CrossesDiagonal(Point64 a, Point64 c, Point64 q, Point64 r,
      long minX, long maxX, long minY, long maxY)
    {
      return Math.Max(q.X, r.X) >= minX && Math.Min(q.X, r.X) <= maxX &&
        Math.Max(q.Y, r.Y) >= minY && Math.Min(q.Y, r.Y) <= maxY &&
        q != a && q != c && r != a && r != c &&
        InternalClipper.CrossProductSign(a, c, q) * InternalClipper.CrossProductSign(a, c, r) < 0 &&
        InternalClipper.CrossProductSign(q, r, a) * InternalClipper.CrossProductSign(q, r, c) < 0;
    }

    // true when vertex p (not a corner node of the ear) lies inside the ear (a, b, c) or on its
    // edges, or is a copy of a corner with an edge entering the ear
    private static bool BlocksEar(Point64 a, Point64 b, Point64 c, Node p,
      long minX, long maxX, long minY, long maxY)
    {
      Point64 q = p.pt;
      if (q.X < minX || q.X > maxX || q.Y < minY || q.Y > maxY) return false;
      if (q == a || q == b || q == c)
        return EntersCorner(a, b, c, q, p.prev.pt) || EntersCorner(a, b, c, q, p.next.pt);
      return InternalClipper.CrossProductSign(a, b, q) >= 0 &&
        InternalClipper.CrossProductSign(b, c, q) >= 0 &&
        InternalClipper.CrossProductSign(c, a, q) >= 0;
    }

    // true when the edge from corner k of the counter-clockwise triangle (a, b, c) towards q
    // points strictly into the triangle
    private static bool EntersCorner(Point64 a, Point64 b, Point64 c, Point64 k, Point64 q)
    {
      if (k == a)
        return InternalClipper.CrossProductSign(a, b, q) > 0 && InternalClipper.CrossProductSign(c, a, q) > 0;
      if (k == b)
        return InternalClipper.CrossProductSign(b, c, q) > 0 && InternalClipper.CrossProductSign(a, b, q) > 0;
      return InternalClipper.CrossProductSign(c, a, q) > 0 && InternalClipper.CrossProductSign(b, c, q) > 0;
    }

    // Rings at least this long are ear clipped with an EarIndex; below it, scanning the
    // whole ring is cheaper than building the grid (measured: a 200 node ring clips twice
    // as fast without it, a 2900 node ring twice as fast with it). TRI_CHECK builds use
    // the index for every ring and compare each answer with the whole ring scan.
#if TRI_CHECK
    private const int EarIndexMinNodes = 4;
#else
    private const int EarIndexMinNodes = 512;
#endif

    // The vertices and edges of a ring being ear clipped, in a grid, so IsEar visits only the
    // cells under the ear's bounds instead of the whole ring. Each edge is registered in every
    // cell it passes through, and each Remove registers the edge it makes, so the cells hold
    // every vertex and edge the whole ring scan would find within the bounds, and IsEar gives
    // the same answer. Entries for removed nodes and replaced edges are skipped.
    private sealed class EarIndex
    {
      private readonly Grid grid;
      private readonly CellLists<Node> vertices;
      private readonly CellLists<(Node start, Node end)> edges;
      private readonly List<int> cells = new List<int>();
      private int stamp;

      public EarIndex(Node ring, int n)
      {
        long left = long.MaxValue, top = long.MaxValue, right = long.MinValue, bottom = long.MinValue;
        Node p = ring;
        do
        {
          left = Math.Min(left, p.pt.X); right = Math.Max(right, p.pt.X);
          top = Math.Min(top, p.pt.Y); bottom = Math.Max(bottom, p.pt.Y);
          p = p.next;
        } while (p != ring);
        grid = new Grid(left, top, right, bottom, n);
        vertices = new CellLists<Node>(grid.Cells, n);
        edges = new CellLists<(Node, Node)>(grid.Cells, 4 * n);
        do
        {
          vertices.Add(grid.Cell(p.pt), p);
          AddEdge(p);
          p = p.next;
        } while (p != ring);
      }

      // registers the edge from p to p.next
      public void AddEdge(Node p)
      {
        grid.SegmentCells(p.pt, p.next.pt, cells);
        foreach (int cell in cells) edges.Add(cell, (p, p.next));
      }

      // IsEar for a convex ear
      public bool IsEar(Node ear)
      {
        Point64 a = ear.prev.pt, b = ear.pt, c = ear.next.pt;
        long minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
        long minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
        if (++stamp == int.MaxValue) stamp = 1; // a stale match only repeats a check
        // what can stop the ear lies in the closed triangle (a vertex in it, an edge crossing
        // its diagonal), so only the cells it overlaps are visited
        grid.TriangleCells(a, b, c, cells);
        foreach (int cell in cells)
        {
          // entries of removed vertices and replaced edges are dropped as they're met: they
          // never come back, as a removed node is never linked in again
          for (int e = vertices.First(cell), prev = -1; e >= 0; e = vertices.Next(e))
          {
            Node p = vertices[e];
            if (p.removed) { vertices.Unlink(cell, prev, e); continue; }
            prev = e;
            if (p == ear || p == ear.prev || p == ear.next) continue;
            if (BlocksEar(a, b, c, p, minX, maxX, minY, maxY)) return false;
          }
          for (int e = edges.First(cell), prev = -1; e >= 0; e = edges.Next(e))
          {
            (Node s, Node t) = edges[e];
            if (s.removed || s.next != t) { edges.Unlink(cell, prev, e); continue; }
            prev = e;
            if (s.stamp == stamp) continue;
            s.stamp = stamp;
            if (CrossesDiagonal(a, c, s.pt, t.pt, minX, maxX, minY, maxY)) return false;
          }
        }
        return true;
      }
    }

    private static void Remove(Node n, EarIndex? index)
    {
      n.prev.next = n.next;
      n.next.prev = n.prev;
      n.removed = true;
      index?.AddEdge(n.prev);
    }

    // removes vertices that add nothing: repeats, and the middle of three collinear vertices
    // (straight runs and zero-width spikes). Returns a remaining node, or null when fewer than
    // three are left.
    private static Node? FilterRing(Node start, EarIndex? index)
    {
      Node p = start;
      int guard = 0;
      bool again = true;
      while (again)
      {
        again = false;
        Node stop = p;
        do
        {
          if (p.next == p.prev) return null;
          if (p.pt == p.next.pt || InternalClipper.CrossProductSign(p.prev.pt, p.pt, p.next.pt) == 0)
          {
            Node prev = p.prev;
            Remove(p, index);
            p = prev;
            again = true;
            if (++guard > 1 << 30) return null;
            break;
          }
          p = p.next;
        } while (p != stop);
      }
      return p.next == p.prev ? null : p;
    }

    // a vertex that adds nothing: it repeats its neighbour's position, or the ring turns back
    // on itself there (a zero-width spike)
    private static bool IsSpike(Node n)
    {
      Point64 a = n.prev.pt, b = n.pt, c = n.next.pt;
      if (a == c) return true;
      if (InternalClipper.CrossProductSign(a, b, c) != 0) return false;
      return (Int128) (b.X - a.X) * (c.X - b.X) + (Int128) (b.Y - a.Y) * (c.Y - b.Y) < 0;
    }

    // removes repeats and spikes at n and next to it, as they cascade. Returns a node where
    // n was, or null when fewer than three nodes are left.
    private static Node? RemoveDegenerate(Node n, EarIndex? index)
    {
      while (true)
      {
        if (n.next == n || n.next == n.prev) return null;
        if (n.pt == n.next.pt) { Remove(n.next, index); continue; }
        if (IsSpike(n)) { Node p = n.prev; Remove(n, index); n = p; continue; }
        if (IsSpike(n.next)) { Remove(n.next, index); continue; }
        return n;
      }
    }

    private static bool ClipEars(Node ring, List<Point64[]> triangles)
    {
      int count = 0;
      Node p0 = ring;
      do { count++; p0 = p0.next; } while (p0 != ring && count < EarIndexMinNodes);
      EarIndex? index = null;
      if (count >= EarIndexMinNodes)
      {
        while (p0 != ring) { count++; p0 = p0.next; }
        index = new EarIndex(ring, count);
      }

      Node ear = ring;
      Node stop = ear;
      bool filtered = false;
      while (ear.prev != ear.next)
      {
        if (IsEar(ear, index))
        {
          triangles.Add(new[] { ear.prev.pt, ear.pt, ear.next.pt });
          Node next = ear.next;
          Remove(ear, index);
          // the cut can leave repeats or zero-width spikes (where the ring touched itself),
          // which must go before they look like ears
          Node? n = RemoveDegenerate(next, index);
          if (n != null) n = RemoveDegenerate(n.prev, index);
          if (n == null) return true; // nothing with area is left
          // try the vertex after next first, as earcut does: continuing at the next vertex
          // clips a fan around the previous one, whose slivers span the ring
          ear = n.next.next;
          stop = ear;
          filtered = false;
          continue;
        }

        ear = ear.next;
        if (ear != stop) continue;

        // a whole pass without an ear: drop vertices that add nothing and try again
        if (filtered) return false;
        Node? f = FilterRing(ear, index);
        if (f == null) return true; // nothing with area is left
        ear = f;
        stop = ear;
        filtered = true;
      }
      return true;
    }

    // ---------------------------------------------------------------------
    // Delaunay: Lawson edge flips with the exact in-circle test
    // ---------------------------------------------------------------------

    private static void Legalize(List<Point64[]> triangles)
    {
      // vertices by position, triangles as counter-clockwise index triples: half-edge h of
      // triangle h / 3 runs from tri[h] to tri[Next(h)]
      int count = triangles.Count;
      Dictionary<(long, long), int> index = new Dictionary<(long, long), int>(count + 2);
      List<Point64> pts = new List<Point64>(count + 2);
      int[] tri = new int[3 * count];
      for (int t = 0; t < count; t++)
        for (int k = 0; k < 3; k++)
        {
          Point64 pt = triangles[t][k];
          if (!index.TryGetValue((pt.X, pt.Y), out int i))
          {
            i = pts.Count;
            pts.Add(pt);
            index.Add((pt.X, pt.Y), i);
          }
          tri[3 * t + k] = i;
        }

      // twin[h]: the half-edge running the other way along h's edge, or -1 on the boundary.
      // An edge used twice in one direction (where rings touch) has no twin either, so it is
      // left alone.
      long n = pts.Count;
      Dictionary<long, int> directed = new Dictionary<long, int>(3 * count);
      for (int h = 0; h < 3 * count; h++)
      {
        long key = tri[h] * n + tri[Next(h)];
        directed[key] = directed.TryGetValue(key, out int other) && other != h ? -2 : h;
      }
      int[] twin = new int[3 * count];
      HashSet<long> undirected = new HashSet<long>(3 * count);
      for (int h = 0; h < 3 * count; h++)
      {
        int u = tri[h], v = tri[Next(h)];
        undirected.Add(Math.Min(u, v) * n + Math.Max(u, v));
        twin[h] = directed[u * n + v] == h && directed.TryGetValue(v * n + u, out int r) && r >= 0 ? r : -1;
      }

      Stack<int> pending = new Stack<int>(3 * count);
      for (int h = 3 * count - 1; h >= 0; h--) pending.Push(h);
      long guard = 64L * count * count + 1024;
      while (pending.Count > 0 && guard-- > 0)
      {
        int e = pending.Pop(), f = twin[e];
        if (f < 0) continue;
        int ne = Next(e), pe = Prev(e), nf = Next(f), pf = Prev(f);
        int u = tri[e], v = tri[ne], w = tri[pe], x = tri[pf];

        // (u, v, w) is counter-clockwise, so a positive sign means x is inside its circle
        if (InCircleSign(pts[u], pts[v], pts[w], pts[x]) <= 0) continue;
        // the new triangles must be proper (the quad must be convex)
        if (InternalClipper.CrossProductSign(pts[u], pts[x], pts[w]) <= 0 ||
            InternalClipper.CrossProductSign(pts[x], pts[v], pts[w]) <= 0) continue;
        if (undirected.Contains(Math.Min(x, w) * n + Math.Max(x, w))) continue;

        // (u, v, w) and (v, u, x) become (u, x, w) and (x, v, w)
        undirected.Remove(Math.Min(u, v) * n + Math.Max(u, v));
        undirected.Add(Math.Min(x, w) * n + Math.Max(x, w));
        int a1 = twin[nf], a2 = twin[pf], b1 = twin[ne];
        tri[ne] = x;
        tri[f] = x; tri[nf] = v; tri[pf] = w;
        Link(twin, e, a1);  // u -> x
        Link(twin, ne, pf); // x -> w and w -> x
        Link(twin, f, a2);  // x -> v
        Link(twin, nf, b1); // v -> w

        pending.Push(e);
        pending.Push(f);
        pending.Push(nf);
        pending.Push(pe);
      }

      for (int t = 0; t < count; t++)
        triangles[t] = new[] { pts[tri[3 * t]], pts[tri[3 * t + 1]], pts[tri[3 * t + 2]] };
    }

    private static int Next(int h) => h % 3 == 2 ? h - 2 : h + 1;
    private static int Prev(int h) => h % 3 == 0 ? h + 2 : h - 1;

    // makes half-edges h and g (or the boundary, -1) each other's twin
    private static void Link(int[] twin, int h, int g)
    {
      twin[h] = g;
      if (g >= 0) twin[g] = h;
    }

    private const long InCircleInt128Limit = 1L << 30;
    private const double InCircleErrorBound = 1e-12;

    // The exact sign of the in-circle determinant of ptD against the circle through ptA, ptB
    // and ptC (positive when ptD is inside for a counter-clockwise ptA, ptB, ptC). Evaluated
    // in double, the determinant is rounding noise for near-cocircular points with large
    // coordinates, and edge flipping then never ended (NodeFlow N972). So double only decides
    // when its error bound allows, and otherwise the determinant is computed exactly:
    // with every difference below 2^30 the determinant stays below 2^125, so Int128 is exact;
    // larger differences use BigInteger.
    internal static int InCircleSign(Point64 ptA, Point64 ptB, Point64 ptC, Point64 ptD)
    {
      // Clipper's coordinates are within +/-2^61, so these differences can't overflow
      long m00 = ptA.X - ptD.X, m01 = ptA.Y - ptD.Y;
      long m10 = ptB.X - ptD.X, m11 = ptB.Y - ptD.Y;
      long m20 = ptC.X - ptD.X, m21 = ptC.Y - ptD.Y;

      // first in double: the sign is certain when the determinant is further from zero than
      // its rounding error can reach. Each difference rounds once to double (relative error
      // 2^-53), and the products, lifts and sums add a few roundings more: the error stays
      // below 16 * 2^-53 of the permanent (the determinant with every term made positive).
      // The bound used is far larger, so only near-cocircular points take the exact path.
      double a0d = m00, a1d = m01, b0d = m10, b1d = m11, c0d = m20, c1d = m21;
      double a2d = a0d * a0d + a1d * a1d, b2d = b0d * b0d + b1d * b1d, c2d = c0d * c0d + c1d * c1d;
      double bc = b0d * c1d - c0d * b1d, ca = c0d * a1d - a0d * c1d, ab = a0d * b1d - b0d * a1d;
      double detd = a2d * bc + b2d * ca + c2d * ab;
      double permanent = a2d * (Math.Abs(b0d * c1d) + Math.Abs(c0d * b1d)) +
        b2d * (Math.Abs(c0d * a1d) + Math.Abs(a0d * c1d)) + c2d * (Math.Abs(a0d * b1d) + Math.Abs(b0d * a1d));
      double bound = InCircleErrorBound * permanent;
      if (detd > bound) return 1;
      if (detd < -bound) return -1;

      if (Math.Abs(m00) < InCircleInt128Limit && Math.Abs(m01) < InCircleInt128Limit &&
          Math.Abs(m10) < InCircleInt128Limit && Math.Abs(m11) < InCircleInt128Limit &&
          Math.Abs(m20) < InCircleInt128Limit && Math.Abs(m21) < InCircleInt128Limit)
      {
        Int128 a0 = m00, a1 = m01, b0 = m10, b1 = m11, c0 = m20, c1 = m21;
        Int128 a2 = a0 * a0 + a1 * a1, b2 = b0 * b0 + b1 * b1, c2 = c0 * c0 + c1 * c1;
        Int128 det = a0 * (b1 * c2 - c1 * b2) - b0 * (a1 * c2 - c1 * a2) + c0 * (a1 * b2 - b1 * a2);
        return Int128.Sign(det);
      }
      else
      {
        BigInteger a0 = m00, a1 = m01, b0 = m10, b1 = m11, c0 = m20, c1 = m21;
        BigInteger a2 = a0 * a0 + a1 * a1, b2 = b0 * b0 + b1 * b1, c2 = c0 * c0 + c1 * c1;
        BigInteger det = a0 * (b1 * c2 - c1 * b2) - b0 * (a1 * c2 - c1 * a2) + c0 * (a1 * b2 - b1 * a2);
        return det.Sign;
      }
    }
  }
}
