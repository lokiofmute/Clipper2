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
      public Node(Point64 pt) { this.pt = pt; }
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
        // every ring, for the bridge visibility tests
        List<Node> allRings = new List<Node> { ring };
        List<Node> holeStarts = new List<Node>(holes.Count);
        foreach (Path64 hole in holes)
        {
          Node? h = MakeRing(hole, false);
          if (h == null) continue;
          allRings.Add(h);
          holeStarts.Add(Leftmost(h));
        }
        // bridge holes from left to right, so a hole's bridge never meets an unbridged hole
        holeStarts.Sort((a, b) => a.pt.X != b.pt.X ? a.pt.X.CompareTo(b.pt.X) : a.pt.Y.CompareTo(b.pt.Y));
        foreach (Node h in holeStarts)
        {
          Node? bridge = FindBridge(h, ring, allRings);
          if (bridge == null) return false;
          SplitRing(bridge, h);
        }
      }

      // where the ring touches itself, separate pieces that only share a point are clipped
      // on their own
      foreach (Node piece in SplitPinches(ring))
        if (!ClipEars(piece, triangles)) return false;
      return true;
    }

    private static Int128 TwiceArea(Node start, Node end)
    {
      // the loop start .. end, closed back to start
      Int128 sum = 0;
      Node p = start;
      while (true)
      {
        Node q = (p == end) ? start : p.next;
        sum += (Int128) p.pt.X * q.pt.Y - (Int128) q.pt.X * p.pt.Y;
        if (p == end) break;
        p = p.next;
      }
      return sum;
    }

    // Splits a ring wherever it passes the same point twice and both loops there enclose
    // area (pieces touching at a point). Loops without area are dropped. A loop of negative
    // area is a hole attached at that point, so the ring isn't split there.
    private static List<Node> SplitPinches(Node ring)
    {
      List<Node> done = new List<Node>();
      Stack<Node> todo = new Stack<Node>();
      todo.Push(ring);
      while (todo.Count > 0)
      {
        Node start = todo.Pop();
        Dictionary<(long, long), List<Node>> seen = new Dictionary<(long, long), List<Node>>();
        Node p = start;
        do
        {
          if (!seen.TryGetValue((p.pt.X, p.pt.Y), out List<Node>? list))
            seen.Add((p.pt.X, p.pt.Y), list = new List<Node>());
          list.Add(p);
          p = p.next;
        } while (p != start);

        bool split = false;
        foreach (List<Node> list in seen.Values)
        {
          for (int i = 0; i < list.Count && !split; i++)
            for (int j = i + 1; j < list.Count && !split; j++)
            {
              Node a = list[i], b = list[j];
              Int128 area1 = TwiceArea(a, b.prev), area2 = TwiceArea(b, a.prev);
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

    // true when segment a-b crosses an edge of any ring or passes through a vertex
    // (shared end positions don't count)
    private static bool SegmentBlocked(Point64 a, Point64 b, List<Node> rings)
    {
      long minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
      long minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
      foreach (Node start in rings)
      {
        Node p = start;
        do
        {
          Point64 c = p.pt, d = p.next.pt;
          p = p.next;
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
        } while (p != start);
      }
      return false;
    }

    private static bool IsOnSegment(Point64 pt, Point64 a, Point64 b)
    {
      return InternalClipper.CrossProductSign(a, pt, b) == 0 &&
        pt.X >= Math.Min(a.X, b.X) && pt.X <= Math.Max(a.X, b.X) &&
        pt.Y >= Math.Min(a.Y, b.Y) && pt.Y <= Math.Max(a.Y, b.Y);
    }

    // the nearest vertex of the outer ring (holes already bridged in) that hole vertex h
    // can be joined to without crossing anything
    private static Node? FindBridge(Node h, Node ring, List<Node> allRings)
    {
      List<(Int128 d, Node n)> candidates = new List<(Int128, Node)>();
      Node p = ring;
      do
      {
        if (p.pt == h.pt)
        {
          // the hole touches the outer here: a zero length bridge
          if (LocallyInside(p, h.next.pt) || LocallyInside(p, h.prev.pt)) return p;
        }
        else
        {
          Int128 dx = p.pt.X - h.pt.X, dy = p.pt.Y - h.pt.Y;
          candidates.Add((dx * dx + dy * dy, p));
        }
        p = p.next;
      } while (p != ring);

      candidates.Sort((x, y) => x.d.CompareTo(y.d));
      foreach ((Int128 _, Node n) in candidates)
      {
        if (!LocallyInside(n, h.pt) || !LocallyInside(h, n.pt)) continue;
        if (SegmentBlocked(h.pt, n.pt, allRings)) continue;
        return n;
      }
      return null;
    }

    // joins ring node a to node b of another ring by a two-way bridge (copies of a and b)
    private static void SplitRing(Node a, Node b)
    {
      Node a2 = new Node(a.pt), b2 = new Node(b.pt);
      Node an = a.next, bp = b.prev;
      a.next = b; b.prev = a;
      a2.next = an; an.prev = a2;
      b2.next = a2; a2.prev = b2;
      bp.next = b2; b2.prev = bp;
    }

    // true when the triangle (a, b, c) at node ear can be clipped: it is convex, no other
    // vertex lies inside it or on its edges, no edge crosses its diagonal a-c, and no edge
    // from a copy of one of its corners (where a ring touches itself, or at a bridge) enters it
    private static bool IsEar(Node ear)
    {
      Point64 a = ear.prev.pt, b = ear.pt, c = ear.next.pt;
      if (InternalClipper.CrossProductSign(a, b, c) <= 0) return false; // reflex or flat

      long minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
      long minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
      for (Node p = ear.next.next; p != ear.prev; p = p.next)
      {
        Point64 q = p.pt, r = p.next.pt;

        // the edge p -> p.next crossing the diagonal
        if (Math.Max(q.X, r.X) >= minX && Math.Min(q.X, r.X) <= maxX &&
            Math.Max(q.Y, r.Y) >= minY && Math.Min(q.Y, r.Y) <= maxY &&
            q != a && q != c && r != a && r != c &&
            InternalClipper.CrossProductSign(a, c, q) * InternalClipper.CrossProductSign(a, c, r) < 0 &&
            InternalClipper.CrossProductSign(q, r, a) * InternalClipper.CrossProductSign(q, r, c) < 0)
          return false;

        if (q.X < minX || q.X > maxX || q.Y < minY || q.Y > maxY) continue;
        if (q == a || q == b || q == c)
        {
          if (EntersCorner(a, b, c, q, p.prev.pt) || EntersCorner(a, b, c, q, p.next.pt)) return false;
          continue;
        }
        if (InternalClipper.CrossProductSign(a, b, q) >= 0 &&
            InternalClipper.CrossProductSign(b, c, q) >= 0 &&
            InternalClipper.CrossProductSign(c, a, q) >= 0) return false;
      }
      return true;
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

    private static void Remove(Node n)
    {
      n.prev.next = n.next;
      n.next.prev = n.prev;
    }

    // removes vertices that add nothing: repeats, and the middle of three collinear vertices
    // (straight runs and zero-width spikes). Returns a remaining node, or null when fewer than
    // three are left.
    private static Node? FilterRing(Node start)
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
            Remove(p);
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
    private static Node? RemoveDegenerate(Node n)
    {
      while (true)
      {
        if (n.next == n || n.next == n.prev) return null;
        if (n.pt == n.next.pt) { Remove(n.next); continue; }
        if (IsSpike(n)) { Node p = n.prev; Remove(n); n = p; continue; }
        if (IsSpike(n.next)) { Remove(n.next); continue; }
        return n;
      }
    }

    private static bool ClipEars(Node ring, List<Point64[]> triangles)
    {
      Node ear = ring;
      Node stop = ear;
      bool filtered = false;
      while (ear.prev != ear.next)
      {
        if (IsEar(ear))
        {
          triangles.Add(new[] { ear.prev.pt, ear.pt, ear.next.pt });
          Node next = ear.next;
          Remove(ear);
          // the cut can leave repeats or zero-width spikes (where the ring touched itself),
          // which must go before they look like ears
          Node? n = RemoveDegenerate(next);
          if (n != null) n = RemoveDegenerate(n.prev);
          if (n == null) return true; // nothing with area is left
          ear = n.next;
          stop = ear;
          filtered = false;
          continue;
        }

        ear = ear.next;
        if (ear != stop) continue;

        // a whole pass without an ear: drop vertices that add nothing and try again
        if (filtered) return false;
        Node? f = FilterRing(ear);
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
      // vertices by position, triangles as counter-clockwise index triples
      Dictionary<(long, long), int> index = new Dictionary<(long, long), int>();
      List<Point64> pts = new List<Point64>();
      int[][] tris = new int[triangles.Count][];
      for (int t = 0; t < triangles.Count; t++)
      {
        tris[t] = new int[3];
        for (int k = 0; k < 3; k++)
        {
          Point64 pt = triangles[t][k];
          if (!index.TryGetValue((pt.X, pt.Y), out int i))
          {
            i = pts.Count;
            pts.Add(pt);
            index.Add((pt.X, pt.Y), i);
          }
          tris[t][k] = i;
        }
      }

      // each directed edge to its triangle; an edge used twice in one direction (where rings
      // touch) is left alone
      Dictionary<(int, int), int> edges = new Dictionary<(int, int), int>();
      HashSet<(int, int)> fixedEdges = new HashSet<(int, int)>();
      for (int t = 0; t < tris.Length; t++)
        for (int k = 0; k < 3; k++)
        {
          (int, int) e = (tris[t][k], tris[t][(k + 1) % 3]);
          if (!edges.TryAdd(e, t)) fixedEdges.Add(e);
        }

      Stack<(int, int)> pending = new Stack<(int, int)>(edges.Keys);
      long guard = 64L * tris.Length * tris.Length + 1024;
      while (pending.Count > 0 && guard-- > 0)
      {
        (int u, int v) = pending.Pop();
        if (fixedEdges.Contains((u, v)) || fixedEdges.Contains((v, u))) continue;
        if (!edges.TryGetValue((u, v), out int t1) || !edges.TryGetValue((v, u), out int t2)) continue;
        int w = Third(tris[t1], u, v), x = Third(tris[t2], v, u);

        // (u, v, w) is counter-clockwise, so a positive sign means x is inside its circle
        if (InCircleSign(pts[u], pts[v], pts[w], pts[x]) <= 0) continue;
        // the new triangles must be proper (the quad must be convex)
        if (InternalClipper.CrossProductSign(pts[u], pts[x], pts[w]) <= 0 ||
            InternalClipper.CrossProductSign(pts[x], pts[v], pts[w]) <= 0) continue;
        if (edges.ContainsKey((x, w)) || edges.ContainsKey((w, x))) continue;

        foreach (int[] tri in new[] { tris[t1], tris[t2] })
          for (int k = 0; k < 3; k++) edges.Remove((tri[k], tri[(k + 1) % 3]));
        tris[t1] = new[] { u, x, w };
        tris[t2] = new[] { x, v, w };
        foreach (int t in new[] { t1, t2 })
          for (int k = 0; k < 3; k++) edges[(tris[t][k], tris[t][(k + 1) % 3])] = t;

        pending.Push((u, x));
        pending.Push((x, v));
        pending.Push((v, w));
        pending.Push((w, u));
      }

      for (int t = 0; t < tris.Length; t++)
        triangles[t] = new[] { pts[tris[t][0]], pts[tris[t][1]], pts[tris[t][2]] };
    }

    private static int Third(int[] tri, int a, int b)
    {
      for (int k = 0; k < 3; k++)
        if (tri[k] != a && tri[k] != b) return tri[k];
      return tri[0];
    }

    private const long InCircleInt128Limit = 1L << 30;

    // The exact sign of the in-circle determinant of ptD against the circle through ptA, ptB
    // and ptC (positive when ptD is inside for a counter-clockwise ptA, ptB, ptC). Evaluated
    // in double, the determinant is rounding noise for near-cocircular points with large
    // coordinates, and edge flipping then never ended (NodeFlow N972).
    // With every difference below 2^30 the determinant stays below 2^125, so Int128 is exact;
    // larger differences use BigInteger.
    internal static int InCircleSign(Point64 ptA, Point64 ptB, Point64 ptC, Point64 ptD)
    {
      // Clipper's coordinates are within +/-2^61, so these differences can't overflow
      long m00 = ptA.X - ptD.X, m01 = ptA.Y - ptD.Y;
      long m10 = ptB.X - ptD.X, m11 = ptB.Y - ptD.Y;
      long m20 = ptC.X - ptD.X, m21 = ptC.Y - ptD.Y;

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
