/*******************************************************************************
* Purpose   :  Y-stripe point-in-polygon accelerator (port of the C++ fork's    *
*              StripeIndex, 884ba82)                                           *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;

namespace Clipper2Lib
{
  // Buckets a ring's edges into horizontal Y stripes so a PIP query only scans the
  // stripe spanning pt.Y (~O(sqrt n) vs O(n)). After KiCad's POLY_YSTRIPES_INDEX.
  internal sealed class StripeIndex
  {
    private struct StripeEdge
    {
      public Point64 a;
      public Point64 b;
      public StripeEdge(Point64 a, Point64 b) { this.a = a; this.b = b; }
    }

    private List<StripeEdge> _edges = new List<StripeEdge>();
    private List<int>[] _stripes = Array.Empty<List<int>>();
    private long _yMin;
    private long _stripeHeight = 1;
    private int _stripeCount = 1;

    public int EdgeCount => _edges.Count;
    public bool Empty => _edges.Count == 0;

    private int YToStripe(long y)
    {
      long s = (y - _yMin) / _stripeHeight;
      if (s < 0) return 0;
      if (s >= _stripeCount) return _stripeCount - 1;
      return (int) s;
    }

    private void Build(List<StripeEdge> edges)
    {
      _edges = edges;
      _stripes = Array.Empty<List<int>>();

      if (_edges.Count == 0)
      {
        _yMin = 0;
        _stripeHeight = 1;
        _stripeCount = 1;
        return;
      }

      _yMin = _edges[0].a.Y;
      long yMax = _edges[0].a.Y;
      foreach (StripeEdge e in _edges)
      {
        _yMin = Math.Min(_yMin, Math.Min(e.a.Y, e.b.Y));
        yMax = Math.Max(yMax, Math.Max(e.a.Y, e.b.Y));
      }

      // Brute force calls every point outside a zero-height ring, even one on the ring,
      // so decline it rather than answer IsOn.
      if (yMax == _yMin)
      {
        _edges.Clear();
        return;
      }

      _stripeCount = (int) Math.Sqrt(_edges.Count);
      if (_stripeCount < 1) _stripeCount = 1;
      if (_stripeCount > 65536) _stripeCount = 65536;

      _stripeHeight = (yMax - _yMin + _stripeCount - 1) / _stripeCount;

      // Decline rings whose edges each span many stripes (tall "comb" edges); indexing
      // loses to brute force there. Empty _edges signals the fallback to callers.
      long totalRefs = 0;
      foreach (StripeEdge e in _edges)
        totalRefs += YToStripe(Math.Max(e.a.Y, e.b.Y)) - YToStripe(Math.Min(e.a.Y, e.b.Y)) + 1;

      if (totalRefs > (long) _edges.Count * 8)
      {
        _edges.Clear();
        return;
      }

      _stripes = new List<int>[_stripeCount];
      for (int s = 0; s < _stripeCount; s++) _stripes[s] = new List<int>();

      for (int i = 0; i < _edges.Count; ++i)
      {
        StripeEdge e = _edges[i];
        int sMin = YToStripe(Math.Min(e.a.Y, e.b.Y));
        int sMax = YToStripe(Math.Max(e.a.Y, e.b.Y));
        for (int s = sMin; s <= sMax; ++s)
          _stripes[s].Add(i);
      }
    }

    public void BuildFromPath(Path64 path)
    {
      List<StripeEdge> edges = new List<StripeEdge>();
      if (path.Count >= 3)
      {
        edges.Capacity = path.Count;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
          if (path[j].X != path[i].X || path[j].Y != path[i].Y)
            edges.Add(new StripeEdge(path[j], path[i]));
      }
      Build(edges);
    }

    internal void BuildFromRing(OutPt? op)
    {
      List<StripeEdge> edges = new List<StripeEdge>();
      if (op != null && op.next != op && op.prev != op.next)
      {
        OutPt cur = op;
        do
        {
          OutPt nxt = cur.next!;
          if (cur.pt.X != nxt.pt.X || cur.pt.Y != nxt.pt.Y)
            edges.Add(new StripeEdge(cur.pt, nxt.pt));
          cur = nxt;
        } while (cur != op);
      }
      Build(edges);
    }

    public PointInPolygonResult PointInPolygon(Point64 pt)
    {
      if (_edges.Count == 0) return PointInPolygonResult.IsOutside;

      List<int> stripe = _stripes[YToStripe(pt.Y)];
      bool inside = false;

      foreach (int idx in stripe)
      {
        StripeEdge e = _edges[idx];
        int cross = InternalClipper.CrossProductSign(e.a, e.b, pt);

        // On-segment needs the bbox check; collinear (cross == 0) alone catches points
        // on the edge's infinite line but off the segment.
        if (cross == 0 &&
          pt.X >= Math.Min(e.a.X, e.b.X) && pt.X <= Math.Max(e.a.X, e.b.X) &&
          pt.Y >= Math.Min(e.a.Y, e.b.Y) && pt.Y <= Math.Max(e.a.Y, e.b.Y))
          return PointInPolygonResult.IsOn;

        // Half-open crossing so horizontal edges never affect parity (IsOn only).
        if ((e.a.Y > pt.Y) != (e.b.Y > pt.Y))
        {
          if (e.b.Y > pt.Y ? cross > 0 : cross < 0)
            inside = !inside;
        }
      }

      return inside ? PointInPolygonResult.IsInside : PointInPolygonResult.IsOutside;
    }
  }
}
