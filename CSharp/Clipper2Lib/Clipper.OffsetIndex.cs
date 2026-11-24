/*******************************************************************************
* Purpose   :  Spatial helpers for ClipperOffset's group orientation (port of   *
*              the C++ fork's BoundsTree, PolygonIndex and PolygonLocator,     *
*              64a9ecc)                                                        *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;

namespace Clipper2Lib
{
  // A static R-tree, packed by sorting the boxes into tiles. Tiling by rank rather than by
  // coordinate keeps it balanced however the boxes cluster. Boxes can be marked active, and each
  // node counts its active boxes, so a query for active boxes skips whole subtrees of the others.
  internal sealed class BoundsTree
  {
    private const int NodeSize = 16;

    private struct Node
    {
      public Rect64 bounds;
      public int first;
      public int count;
      public int activeCount;
    }

    // each node covers a run of items, which index the nodes of the level below, or the boxes,
    // and parents maps each of those to the node covering it
    private sealed class Level
    {
      public int[] items = Array.Empty<int>();
      public int[] parents = Array.Empty<int>();
      public readonly List<Node> nodes = new List<Node>();
    }

    private readonly Rect64[] _boxes;
    private readonly bool[] _isActive;
    private readonly List<Level> _levels = new List<Level>();

    public BoundsTree(IList<Rect64> boxes)
    {
      _boxes = new Rect64[boxes.Count];
      boxes.CopyTo(_boxes, 0);
      _isActive = new bool[_boxes.Length];
      Rect64[] rects = (Rect64[]) _boxes.Clone();

      while (rects.Length > 0)
      {
        Level level = new Level
        {
          items = TileOrder(rects),
          parents = new int[rects.Length]
        };
        List<Rect64> nodeRects = new List<Rect64>();

        for (int first = 0; first < level.items.Length; first += NodeSize)
        {
          Node node = new Node
          {
            bounds = Clipper.InvalidRect64,
            first = first,
            count = Math.Min(NodeSize, level.items.Length - first),
            activeCount = 0
          };

          for (int i = first; i < first + node.count; ++i)
          {
            Rect64 r = rects[level.items[i]];
            node.bounds.left = Math.Min(node.bounds.left, r.left);
            node.bounds.top = Math.Min(node.bounds.top, r.top);
            node.bounds.right = Math.Max(node.bounds.right, r.right);
            node.bounds.bottom = Math.Max(node.bounds.bottom, r.bottom);
          }

          for (int i = first; i < first + node.count; ++i)
            level.parents[level.items[i]] = level.nodes.Count;

          level.nodes.Add(node);
          nodeRects.Add(node.bounds);
        }

        _levels.Add(level);
        if (nodeRects.Count == 1) break;
        rects = nodeRects.ToArray();
      }
    }

    public void SetActive(int box, bool isActive)
    {
      if (_isActive[box] == isActive) return;
      _isActive[box] = isActive;
      int item = box;
      foreach (Level level in _levels)
      {
        item = level.parents[item];
        Node node = level.nodes[item];
        node.activeCount += isActive ? 1 : -1;
        level.nodes[item] = node;
      }
    }

    // calls fn with the index of every box that meets, or of every active one, where meets must
    // also hold for every box enclosing one that it holds for
    public void Query(Func<Rect64, bool> meets, Action<int> fn, bool activeOnly = false)
    {
      if (_levels.Count == 0) return;
      Stack<(int level, int node)> stack = new Stack<(int, int)>();
      Level top = _levels[_levels.Count - 1];
      for (int i = 0; i < top.nodes.Count; ++i)
        stack.Push((_levels.Count - 1, i));

      while (stack.Count > 0)
      {
        (int levelIdx, int nodeIdx) = stack.Pop();
        Level level = _levels[levelIdx];
        Node node = level.nodes[nodeIdx];
        if ((activeOnly && node.activeCount == 0) || !meets(node.bounds)) continue;

        for (int i = node.first; i < node.first + node.count; ++i)
        {
          int item = level.items[i];
          if (levelIdx > 0) stack.Push((levelIdx - 1, item));
          else if ((!activeOnly || _isActive[item]) && meets(_boxes[item])) fn(item);
        }
      }
    }

    // sorts by x into vertical slices about as many as there are tiles per slice, then by y
    private static int[] TileOrder(Rect64[] rects)
    {
      int[] order = new int[rects.Length];
      for (int i = 0; i < order.Length; i++) order[i] = i;
      double MidX(int i) => rects[i].left / 2.0 + rects[i].right / 2.0;
      double MidY(int i) => rects[i].top / 2.0 + rects[i].bottom / 2.0;
      Array.Sort(order, (a, b) => MidX(a).CompareTo(MidX(b)));
      int tiles = (rects.Length + NodeSize - 1) / NodeSize;
      int slices = (int) Math.Ceiling(Math.Sqrt(tiles));
      int sliceLen = ((tiles + slices - 1) / slices) * NodeSize;

      for (int first = 0; first < order.Length; first += sliceLen)
      {
        int len = Math.Min(sliceLen, order.Length - first);
        Array.Sort(order, first, len, Comparer<int>.Create((a, b) => MidY(a).CompareTo(MidY(b))));
      }
      return order;
    }
  }

  // Locates points in one polygon with the same result as PointInPolygon, but visits only the
  // edges whose bounds hold the point, then those crossing the point's y to its left. The
  // even-odd parity of a point off the edges doesn't depend on which ray counts the crossings.
  internal sealed class PolygonIndex
  {
    private readonly Path64 _polygon;
    private readonly BoundsTree _edges;

    public PolygonIndex(Path64 polygon)
    {
      _polygon = polygon;
      _edges = new BoundsTree(EdgeBounds(polygon));
    }

    private Point64 Vertex(int i) => _polygon[(i == _polygon.Count) ? 0 : i];

    public PointInPolygonResult Locate(Point64 pt)
    {
      bool isOn = false;
      _edges.Query(
        r => !isOn && r.left <= pt.X && r.right >= pt.X && r.top <= pt.Y && r.bottom >= pt.Y,
        i => { isOn = InternalClipper.CrossProductSign(Vertex(i), Vertex(i + 1), pt) == 0; });
      if (isOn) return PointInPolygonResult.IsOn;

      // with a half-open rule, an edge lying along the ray never crosses it
      bool isInside = false;
      _edges.Query(
        r => r.left <= pt.X && r.top <= pt.Y && r.bottom > pt.Y,
        i =>
        {
          Point64 a = Vertex(i);
          Point64 b = Vertex(i + 1);
          int d = InternalClipper.CrossProductSign(a, b, pt);
          if ((a.Y > pt.Y) != (b.Y > pt.Y) && ((a.Y < b.Y) ? d < 0 : d > 0)) isInside = !isInside;
        });
      return isInside ? PointInPolygonResult.IsInside : PointInPolygonResult.IsOutside;
    }

    private static List<Rect64> EdgeBounds(Path64 polygon)
    {
      List<Rect64> result = new List<Rect64>(polygon.Count);
      for (int i = 0; i < polygon.Count; ++i)
      {
        Point64 a = polygon[i];
        Point64 b = polygon[(i + 1 == polygon.Count) ? 0 : i + 1];
        result.Add(new Rect64(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
      }
      return result;
    }
  }

  // Locates points in a set of polygons. A large polygon gets an edge index on its first use, since
  // many small paths may be tested against it.
  internal sealed class PolygonLocator
  {
    private readonly PolygonIndex?[] _indexes;

    public PolygonLocator(int count)
    {
      _indexes = new PolygonIndex?[count];
    }

    public PointInPolygonResult Locate(Point64 pt, Path64 polygon, int polygonIdx)
    {
      if (polygon.Count < 64) return InternalClipper.PointInPolygon(pt, polygon);
      _indexes[polygonIdx] ??= new PolygonIndex(polygon);
      return _indexes[polygonIdx]!.Locate(pt);
    }
  }
}
