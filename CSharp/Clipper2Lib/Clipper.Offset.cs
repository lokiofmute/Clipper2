/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  11 October 2025                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Path Offset (Inflate/Shrink)                                    *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Clipper2Lib
{
  public enum JoinType
  {
    Miter,
    Square,
    Bevel,
    Round
  }

  public enum EndType
  {
    Polygon,
    Joined,
    Butt,
    Square,
    Round
  }

  public class ClipperOffset
  {

    private class Group
    {
      internal Paths64 inPaths;
      internal JoinType joinType;
      internal EndType endType;
      internal bool pathsReversed; // the group's outers are wound negatively
      // a path's area, exactly, as its sign and twice its magnitude (polygon groups only)
      internal readonly List<ExactArea> areas = new List<ExactArea>();
      internal int outerIdx; // the largest closed path
      internal int outerSign; // sign of the largest closed path's area, 0 if none
      internal bool[]? isStray; // holes no outer encloses, null if there are none

      internal bool IsStray(int pathIdx) => isStray != null && isStray[pathIdx];

      public Group(Paths64 paths, JoinType joinType, EndType endType = EndType.Polygon)
      {
        this.joinType = joinType;
        this.endType = endType;

        bool isJoined = ((endType == EndType.Polygon) || (endType == EndType.Joined));
        inPaths = new Paths64(paths.Count);
        foreach(Path64 path in paths)
          inPaths.Add(Clipper.StripDuplicates(path, isJoined));
        // the offset code assumes every path has at least one vertex
        inPaths.RemoveAll(p => p.Count == 0);

        if (endType != EndType.Polygon) return;

        foreach (Path64 p in inPaths)
          areas.Add(new ExactArea(p));

        // sizes compare exactly, so paths of nearly equal size rank the same however they're
        // wound. The first path wins a tie.
        UInt128 largest = 0;
        for (int i = 0; i < areas.Count; ++i)
        {
          if (areas[i].twiceMagnitude > largest)
          {
            largest = areas[i].twiceMagnitude;
            outerSign = areas[i].sign;
            outerIdx = i;
          }
        }
      }
    }

    internal readonly struct ExactArea
    {
      public readonly int sign;
      public readonly UInt128 twiceMagnitude;

      public ExactArea(Path64 path)
      {
        // the shoelace sum may wrap around on the way, but it ends exact whenever twice the area
        // fits in 127 bits, as it does within Clipper's coordinate range unless the path winds
        // around often
        Int128 sum = 0;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        {
          sum += (Int128) path[j].X * path[i].Y;
          sum -= (Int128) path[i].X * path[j].Y;
        }
        if (sum < 0)
        {
          sign = -1;
          twiceMagnitude = (UInt128) (-sum);
        }
        else
        {
          sign = sum > 0 ? 1 : 0;
          twiceMagnitude = (UInt128) sum;
        }
      }
    }

    // true when path lies inside a container, judged by the first of its vertices that locate
    // doesn't put on the container's edge. A path touching it at every vertex counts as inside.
    private static bool IsInside(Path64 path, Func<Point64, PointInPolygonResult> locate)
    {
      foreach (Point64 pt in path)
      {
        PointInPolygonResult pip = locate(pt);
        if (pip != PointInPolygonResult.IsOn) return pip == PointInPolygonResult.IsInside;
      }
      return true;
    }

    // true when any vertex of path is inside a container, or all of them are on its edge
    private static bool HasVertexInside(Path64 path, Func<Point64, PointInPolygonResult> locate)
    {
      bool allOn = true;
      foreach (Point64 pt in path)
      {
        PointInPolygonResult pip = locate(pt);
        if (pip == PointInPolygonResult.IsInside) return true;
        if (pip == PointInPolygonResult.IsOutside) allOn = false;
      }
      return allOn;
    }

    // true when every vertex lies on one line, so the path encloses no area
    private static bool IsFlat(Path64 path)
    {
      int i = 1;
      while (i < path.Count && path[i] == path[0]) i++;
      if (i == path.Count) return true;
      Point64 pt1 = path[i];
      for (int j = i + 1; j < path.Count; j++)
        if (InternalClipper.CrossProductSign(path[0], pt1, path[j]) != 0) return false;
      return true;
    }

    private const double Tolerance = 1.0E-12;

    // Clipper2 approximates arcs by using series of relatively short straight
    //line segments. And logically, shorter line segments will produce better arc
    // approximations. But very short segments can degrade performance, usually
    // with little or no discernable improvement in curve quality. Very short
    // segments can even detract from curve quality, due to the effects of integer
    // rounding. Since there isn't an optimal number of line segments for any given
    // arc radius (that perfectly balances curve approximation with performance),
    // arc tolerance is user defined. Nevertheless, when the user doesn't define
    // an arc tolerance (ie leaves alone the 0 default value), the calculated
    // default arc tolerance (offset_radius / 500) generally produces good (smooth)
    // arc approximations without producing excessively small segment lengths.
    // See also: https://github.com/PolyClipping/Clipper2/wiki/Trigonometry
    private const double arc_const = 0.002; // <-- 1/500

    private readonly List<Group> _groupList = new List<Group>();
    private Path64 pathOut = new Path64();
    private readonly PathD _normals = new PathD();
    private Paths64 _solution = new Paths64();
    private PolyTree64? _solutionTree;

    private double _groupDelta; //*0.5 for open paths; *-1.0 for negative areas
    private double _delta;
    private double _mitLimSqr;
    private double _stepsPerRad;
    private double _stepSin;
    private double _stepCos;
    private JoinType _joinType;
    public double ArcTolerance { get; set; }
    public bool MergeGroups { get; set; }
    public double MiterLimit { get; set; }
    public bool PreserveCollinear { get; set; }
    public bool ReverseSolution { get; set; }

    public delegate double DeltaCallback64(Path64 path,
      PathD path_norms, int currPt, int prevPt);
    public DeltaCallback64? DeltaCallback { get; set; }

#if USINGZ
    internal void ZCB(Point64 bot1, Point64 top1,
        Point64 bot2, Point64 top2, ref Point64 ip)
    {
      if (bot1.Z != 0 &&
        ((bot1.Z == bot2.Z) || (bot1.Z == top2.Z))) ip.Z = bot1.Z;
      else if (bot2.Z != 0 && bot2.Z == top1.Z) ip.Z = bot2.Z;
      else if (top1.Z != 0 && top1.Z == top2.Z) ip.Z = top1.Z;
      else ZCallback?.Invoke(bot1, top1, bot2, top2, ref ip);
    }
    public ClipperBase.ZCallback64? ZCallback { get; set; }
#endif
    public ClipperOffset(double miterLimit = 2.0,
      double arcTolerance = 0.0, bool
      preserveCollinear = false, bool reverseSolution = false)
    {
      MiterLimit = miterLimit;
      ArcTolerance = arcTolerance;
      MergeGroups = true;
      PreserveCollinear = preserveCollinear;
      ReverseSolution = reverseSolution;
#if USINGZ
      ZCallback = null;
#endif
    }
    public void Clear()
    {
      _groupList.Clear();
    }

    public void AddPath(Path64 path, JoinType joinType, EndType endType)
    {
      Paths64 pp = new Paths64(1) { path };
      AddPaths(pp, joinType, endType);
    }

    public void AddPaths(Paths64 paths, JoinType joinType, EndType endType)
    {
      int cnt = paths.Count;
      if (cnt == 0) return;
      _groupList.Add(new Group(paths, joinType, endType));
    }

    private int CalcSolutionCapacity()
    {
      int result = 0;
      foreach (Group g in _groupList)
        result += (g.endType == EndType.Joined) ? g.inPaths.Count * 2 : g.inPaths.Count;
      return result;
    }

    private readonly struct PathRef
    {
      public readonly UInt128 size;
      public readonly int groupIdx;
      public readonly int pathIdx;
      public PathRef(UInt128 size, int groupIdx, int pathIdx)
      {
        this.size = size;
        this.groupIdx = groupIdx;
        this.pathIdx = pathIdx;
      }
    }

    private void SetGroupOrientations()
    {
      // a group's outers share the orientation of its largest closed path, which a hole touching
      // its outer or a stray sliver left by clipping can't outweigh. Flagging groups as 'reversed'
      // (negating delta etc.) is much more efficient than reversing every path.
      List<PathRef> paths = new List<PathRef>();
      List<Rect64> bounds = new List<Rect64>();
      List<int> groupEnds = new List<int>(); // where each group's paths end in paths
      int[] outerRefs = new int[_groupList.Count]; // each group's largest path in paths
      bool hasCcw = false, hasCw = false, hasHoles = false;

      for (int i = 0; i < _groupList.Count; ++i)
      {
        Group g = _groupList[i];
        g.pathsReversed = g.outerSign < 0;
        g.isStray = null;
        hasCcw = hasCcw || (g.outerSign > 0);
        hasCw = hasCw || (g.outerSign < 0);

        for (int j = 0; j < g.areas.Count; ++j)
        {
          if (g.areas[j].sign == 0) continue;
          hasHoles = hasHoles || (g.areas[j].sign != g.outerSign);
          if (j == g.outerIdx) outerRefs[i] = paths.Count;
          paths.Add(new PathRef(g.areas[j].twiceMagnitude, i, j));
          bounds.Add(Clipper.GetBounds(g.inPaths[j]));
        }
        groupEnds.Add(paths.Count);
      }

      // only a group wound against another can hold that group's holes
      bool isMixed = hasCcw && hasCw;
      if (!isMixed && !hasHoles) return;

      // A group wound against another holds that group's holes when its largest path lies inside one
      // of that group's outers, and the innermost enclosing path decides, so a group inside another
      // group's hole stays an island. A hole that no outer at least as large encloses is stray and
      // contributes nothing, since offsetting it alone would leave the loops its concave joins add.
      // Each decision depends only on paths at least as large, so settling from the largest path down,
      // with a group's own largest path first among equals, settles everything in one pass whatever
      // the AddPaths order. Without mixed orientations no group turns into holes, so any order will do.
      int[] order = new int[paths.Count];
      for (int i = 0; i < order.Length; i++) order[i] = i;
      if (isMixed)
      {
        // stable, like the C++ std::stable_sort
        Array.Sort(order, (a, b) =>
        {
          if (paths[a].size != paths[b].size) return paths[a].size > paths[b].size ? -1 : 1;
          bool aOuter = paths[a].pathIdx == _groupList[paths[a].groupIdx].outerIdx;
          bool bOuter = paths[b].pathIdx == _groupList[paths[b].groupIdx].outerIdx;
          if (aOuter != bOuter) return aOuter ? -1 : 1;
          return a.CompareTo(b);
        });
      }

      Path64 PathOf(int i) => _groupList[paths[i].groupIdx].inPaths[paths[i].pathIdx];
      int SignOf(int i) => _groupList[paths[i].groupIdx].areas[paths[i].pathIdx].sign;
      bool IsOuter(int i) => (SignOf(i) < 0) == _groupList[paths[i].groupIdx].pathsReversed;

      // the outers are active, so a hole looking for one skips the holes around it. Holes inside
      // their own group's outer don't need the tree at all.
      BoundsTree? tree = null;
      BoundsTree GetTree()
      {
        if (tree != null) return tree;
        tree = new BoundsTree(bounds);
        for (int i = 0; i < paths.Count; ++i)
          tree.SetActive(i, IsOuter(i));
        return tree;
      }

      PolygonLocator locator = new PolygonLocator(paths.Count);
      Func<Point64, PointInPolygonResult> LocateIn(int i) => pt => locator.Locate(pt, PathOf(i), i);
      List<int> enclosers = new List<int>();

      foreach (int k in order)
      {
        int groupIdx = paths[k].groupIdx;
        Group g = _groupList[groupIdx];
        Path64 path = PathOf(k);
        Rect64 pathBounds = bounds[k];

        if (paths[k].pathIdx == g.outerIdx)
        {
          if (!isMixed) continue;
          enclosers.Clear();
          GetTree().Query(r => r.Contains(pathBounds), i =>
          {
            if (paths[i].groupIdx != groupIdx && paths[i].size > paths[k].size &&
              !_groupList[paths[i].groupIdx].IsStray(paths[i].pathIdx))
              enclosers.Add(i);
          });

          // a heap, since only the smallest few enclosers are usually needed
          PriorityQueue<int, UInt128> heap = new PriorityQueue<int, UInt128>();
          foreach (int i in enclosers) heap.Enqueue(i, paths[i].size);
          int innermost = paths.Count;
          bool isHole = false;
          while (heap.Count > 0)
          {
            int i = heap.Dequeue();
            if (innermost < paths.Count && paths[i].size > paths[innermost].size) break;
            if (!IsInside(path, LocateIn(i))) continue;
            innermost = i;
            // the enclosing path must be one of its group's outers, wound against this group
            if (IsOuter(i) && SignOf(i) != g.outerSign) isHole = true;
          }
          if (!isHole) continue;

          g.pathsReversed = !g.pathsReversed;
          for (int i = groupIdx > 0 ? groupEnds[groupIdx - 1] : 0; i < groupEnds[groupIdx]; ++i)
            GetTree().SetActive(i, IsOuter(i));
          continue;
        }

        if (IsOuter(k)) continue;

        // a hole with a vertex inside an outer counts as enclosed, so one poking out still cuts it
        int ownOuter = outerRefs[groupIdx];
        bool isEnclosed = IsOuter(ownOuter) && HasVertexInside(path, LocateIn(ownOuter));
        if (!isEnclosed)
        {
          GetTree().Query(r => !isEnclosed && r.Intersects(pathBounds), i =>
          {
            if (paths[i].size >= paths[k].size) isEnclosed = HasVertexInside(path, LocateIn(i));
          }, true);
        }
        if (isEnclosed) continue;

        g.isStray ??= new bool[g.inPaths.Count];
        g.isStray[paths[k].pathIdx] = true;
      }
    }

    private void InitClipper(Clipper64 c)
    {
      c.PreserveCollinear = PreserveCollinear;
#if USINGZ
      c.ZCallback = ZCB;
#endif
    }

    private void KeepGrownRegions()
    {
      // growth is always wound positively, so a positive union removes the eroded regions
      Clipper64 c = new Clipper64();
      InitClipper(c);
      c.AddSubject(new Paths64 { _solution[^1] });
      _solution.RemoveAt(_solution.Count - 1);
      Paths64 grown = new Paths64();
      c.Execute(ClipType.Union, FillRule.Positive, grown);
      _solution.AddRange(grown);
    }

    private void ExecuteInternal(double delta)
    {
      if (_groupList.Count == 0) return;
      _solution.EnsureCapacity(CalcSolutionCapacity());
      SetGroupOrientations();

      if (Math.Abs(delta) < 0.5) // ie: offset is insignificant
      {
        foreach (Group group in _groupList)
        {
          // an open path is only as wide as its delta, so an insignificant delta leaves nothing
          if (group.endType != EndType.Polygon) continue;
          for (int i = 0; i < group.inPaths.Count; ++i)
          {
            if (group.IsStray(i)) continue;
            Path64 p = new Path64(group.inPaths[i]);
            if (group.pathsReversed) p.Reverse();
            _solution.Add(p);
          }
        }
      }
      else
      {
        _mitLimSqr = (MiterLimit <= 1 ?
          2.0 : 2.0 / Clipper.Sqr(MiterLimit));
        _delta = delta;
        foreach (Group group in _groupList)
          DoGroupOffset(group);
      }

      if (_solution.Count == 0) return;

      // clean up self-intersections ...
      Clipper64 c = new Clipper64();
      InitClipper(c);
      c.ReverseSolution = ReverseSolution;
      c.AddSubject(_solution);
      // outers are wound positively, whatever the orientation of the input
      if (_solutionTree != null)
        c.Execute(ClipType.Union, FillRule.Positive, _solutionTree);
      else
        c.Execute(ClipType.Union, FillRule.Positive, _solution);
    }

    public void Execute(double delta, Paths64 solution)
    {
      solution.Clear();
      _solution = solution;
      _solutionTree = null;
      ExecuteInternal(delta);
    }

    public void Execute(double delta, PolyTree64 solutionTree)
    {
      solutionTree.Clear();
      _solutionTree = solutionTree;
      _solution = new Paths64();
      try
      {
        ExecuteInternal(delta);
      }
      finally
      {
        _solutionTree = null;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PointD GetUnitNormal(Point64 pt1, Point64 pt2)
    {
      double dx = (pt2.X - pt1.X);
      double dy = (pt2.Y - pt1.Y);
      if ((dx == 0) && (dy == 0)) return new PointD();

      double f = 1.0 / Math.Sqrt(dx * dx + dy * dy);
      dx *= f;
      dy *= f;

      return new PointD(dy, -dx);
    }

    public void Execute(DeltaCallback64 deltaCallback, Paths64 solution)
    {
      DeltaCallback = deltaCallback;
      Execute(1.0, solution);
    }    
    
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointD TranslatePoint(PointD pt, double dx, double dy)
    {
#if USINGZ
      return new PointD(pt.x + dx, pt.y + dy, pt.z);
#else
      return new PointD(pt.x + dx, pt.y + dy);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointD ReflectPoint(PointD pt, PointD pivot)
    {
#if USINGZ
      return new PointD(pivot.x + (pivot.x - pt.x), pivot.y + (pivot.y - pt.y), pt.z);
#else
      return new PointD(pivot.x + (pivot.x - pt.x), pivot.y + (pivot.y - pt.y));
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AlmostZero(double value, double epsilon = 0.001)
    {
      return Math.Abs(value) < epsilon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Hypotenuse(double x, double y)
    {
      return Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointD NormalizeVector(PointD vec)
    {
	    double h = Hypotenuse(vec.x, vec.y);
	    if (AlmostZero(h)) return new PointD(0,0);
        double inverseHypot = 1 / h;
	    return new PointD(vec.x* inverseHypot, vec.y* inverseHypot);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointD GetAvgUnitVector(PointD vec1, PointD vec2)
    {
	    return NormalizeVector(new PointD(vec1.x + vec2.x, vec1.y + vec2.y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Point64 GetPerpendic(Point64 pt, PointD norm)
    {
#if USINGZ
      return InternalClipper.MakePoint(pt.X + norm.x * _groupDelta,
        pt.Y + norm.y * _groupDelta, pt.Z);
#else
      return new Point64(pt.X + norm.x * _groupDelta,
        pt.Y + norm.y * _groupDelta);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PointD GetPerpendicD(Point64 pt, PointD norm)
    {
#if USINGZ
      return new PointD(pt.X + norm.x * _groupDelta,
        pt.Y + norm.y * _groupDelta, pt.Z);
#else
      return new PointD(pt.X + norm.x * _groupDelta,
        pt.Y + norm.y * _groupDelta);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoBevel(Path64 path, int j, int k)
    {
      Point64 pt1, pt2;
      if (j == k)
      {
        double absDelta = Math.Abs(_groupDelta);
#if USINGZ
        pt1 = InternalClipper.MakePoint(
          path[j].X - absDelta * _normals[j].x, 
          path[j].Y - absDelta * _normals[j].y, path[j].Z);
        pt2 = InternalClipper.MakePoint(
          path[j].X + absDelta * _normals[j].x, 
          path[j].Y + absDelta * _normals[j].y, path[j].Z);
#else
        pt1 = new Point64(
          path[j].X - absDelta * _normals[j].x,
          path[j].Y - absDelta * _normals[j].y);
        pt2 = new Point64(
          path[j].X + absDelta * _normals[j].x,
          path[j].Y + absDelta * _normals[j].y);
#endif
      }
      else
      {
#if USINGZ
        pt1 = InternalClipper.MakePoint(
          path[j].X + _groupDelta * _normals[k].x,
          path[j].Y + _groupDelta * _normals[k].y, path[j].Z);
        pt2 = InternalClipper.MakePoint(
          path[j].X + _groupDelta * _normals[j].x,
          path[j].Y + _groupDelta * _normals[j].y, path[j].Z);
#else
        pt1 = new Point64(
          path[j].X + _groupDelta * _normals[k].x,
          path[j].Y + _groupDelta * _normals[k].y);
        pt2 = new Point64(
          path[j].X + _groupDelta * _normals[j].x,
          path[j].Y + _groupDelta * _normals[j].y);
#endif
      }
      pathOut.Add(pt1);
      pathOut.Add(pt2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoSquare(Path64 path, int j, int k)
    {
      PointD vec;
      if (j == k)
      {
        vec = new PointD(_normals[j].y, -_normals[j].x);
      }
      else
      {
        vec = GetAvgUnitVector(
          new PointD(-_normals[k].y, _normals[k].x),
          new PointD(_normals[j].y, -_normals[j].x));
      }

      double absDelta = Math.Abs(_groupDelta);
      // now offset the original vertex delta units along unit vector
      PointD ptQ = new PointD(path[j]);
      ptQ = TranslatePoint(ptQ, absDelta * vec.x, absDelta * vec.y);

      // get perpendicular vertices
      PointD pt1 = TranslatePoint(ptQ, _groupDelta * vec.y, _groupDelta * -vec.x);
      PointD pt2 = TranslatePoint(ptQ, _groupDelta * -vec.y, _groupDelta * vec.x);
      // get 2 vertices along one edge offset
      PointD pt3 = GetPerpendicD(path[k], _normals[k]);

      if (j == k)
      {
        PointD pt4 = new PointD(
          pt3.x + vec.x * _groupDelta,
          pt3.y + vec.y * _groupDelta);
        InternalClipper.GetLineIntersectPt(pt1, pt2, pt3, pt4, out PointD pt);
#if USINGZ
        pt.z = ptQ.z;
#endif    
        //get the second intersect point through reflecion
        pathOut.Add(new Point64(ReflectPoint(pt, ptQ)));
        pathOut.Add(new Point64(pt));
      }
      else
      {
        PointD pt4 = GetPerpendicD(path[j], _normals[k]);
        InternalClipper.GetLineIntersectPt(pt1, pt2, pt3, pt4, out PointD pt);
#if USINGZ
        pt.z = ptQ.z;
#endif
        pathOut.Add(new Point64(pt));
        //get the second intersect point through reflecion
        pathOut.Add(new Point64(ReflectPoint(pt, ptQ)));
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoMiter(Path64 path, int j, int k, double cosA)
    {
      double q = _groupDelta / (cosA + 1);
#if USINGZ
      pathOut.Add(InternalClipper.MakePoint(
          path[j].X + (_normals[k].x + _normals[j].x) * q,
          path[j].Y + (_normals[k].y + _normals[j].y) * q,
          path[j].Z));
#else
      pathOut.Add(new Point64(
          path[j].X + (_normals[k].x + _normals[j].x) * q,
          path[j].Y + (_normals[k].y + _normals[j].y) * q));
#endif
    }

    private void SetArcSteps(double delta)
    {
      // calculate the number of steps required to approximate a circle
      // (see https://github.com/PolyClipping/Clipper2/wiki/Trigonometry)
      // arcTol - when ArcTolerance is undefined (0) then curve imprecision
      // will be relative to the size of the offset (delta). Obviously very
      // large offsets will almost always require much less precision.
      double absDelta = Math.Abs(delta);
      double arcTol = (ArcTolerance > Tolerance) ?
        Math.Min(absDelta, ArcTolerance) : absDelta * arc_const;
      double stepsPer360 = Math.Min(Math.PI / Math.Acos(1 - arcTol / absDelta), absDelta * Math.PI);
      _stepSin = Math.Sin(2 * Math.PI / stepsPer360);
      _stepCos = Math.Cos(2 * Math.PI / stepsPer360);
      if (delta < 0.0) _stepSin = -_stepSin;
      _stepsPerRad = stepsPer360 / (2 * Math.PI);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoRound(Path64 path, int j, int k, double angle)
    {
      // when DeltaCallback is assigned, _groupDelta won't be constant,
      // so the arc steps need recalculating for *every* vertex.
      if (DeltaCallback != null) SetArcSteps(_groupDelta);

      Point64 pt = path[j];
      PointD offsetVec = new PointD(_normals[k].x * _groupDelta, _normals[k].y * _groupDelta);
      if (j == k) offsetVec.Negate();
#if USINGZ
      pathOut.Add(InternalClipper.MakePoint(pt.X + offsetVec.x, pt.Y + offsetVec.y, pt.Z));
#else
      pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y));
#endif
      int steps = (int) Math.Ceiling(_stepsPerRad * Math.Abs(angle)); // #448, #456
      for (int i = 1; i < steps; i++) // ie 1 less than steps
      {
        offsetVec = new PointD(offsetVec.x * _stepCos - _stepSin * offsetVec.y,
            offsetVec.x * _stepSin + offsetVec.y * _stepCos);
#if USINGZ
        pathOut.Add(InternalClipper.MakePoint(pt.X + offsetVec.x, pt.Y + offsetVec.y, pt.Z));
#else
        pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y));
#endif
      }
      pathOut.Add(GetPerpendic(pt, _normals[j]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BuildNormals(Path64 path)
    {
      int cnt = path.Count;
      _normals.Clear();
      if (cnt == 0) return;
      _normals.EnsureCapacity(cnt);
      for (int i = 0; i < cnt - 1; i++)
        _normals.Add(GetUnitNormal(path[i], path[i + 1]));
      _normals.Add(GetUnitNormal(path[cnt - 1], path[0]));
    }

    private void OffsetPoint(Group group, Path64 path, int j, int k)
    {
      // Let A = change in angle where edges join
      // A == 0: ie no change in angle (flat join)
      // A == PI: edges 'spike'
      // sin(A) < 0: right turning
      // cos(A) < 0: change in angle is more than 90 degree
      if (path[j] == path[k]) return;

      double sinA = InternalClipper.CrossProduct(_normals[j], _normals[k]);
      double cosA = InternalClipper.DotProduct(_normals[j], _normals[k]);
      if (sinA > 1.0) sinA = 1.0;
      else if (sinA < -1.0) sinA = -1.0;

      if (DeltaCallback != null)
      {
        _groupDelta = DeltaCallback(path, _normals, j, k);
        if (group.pathsReversed) _groupDelta = -_groupDelta;
      }
      if (Math.Abs(_groupDelta) <= Tolerance)
      {
        pathOut.Add(path[j]);
        return;
      }

      if (cosA > -0.999 && (sinA * _groupDelta < 0)) // test for concavity first (#593)
      {
        // is concave
        // by far the simplest way to construct concave joins, especially those joining very
        // short segments, is to insert 3 points that produce negative regions. These regions
        // will be removed later by the finishing union operation. This is also the best way
        // to ensure that path reversals (ie over-shrunk paths) are removed.
        pathOut.Add(GetPerpendic(path[j], _normals[k]));
        pathOut.Add(path[j]); // (#405, #873, #916)
        pathOut.Add(GetPerpendic(path[j], _normals[j]));
      }
      else if ((cosA > 0.999) && (_joinType != JoinType.Round))
      {
        // almost straight - less than 2.5 degree (#424, #482, #526 & #724)
        DoMiter(path, j, k, cosA);
      }
      else if (_joinType == JoinType.Miter)
      {
        // miter unless the angle is sufficiently acute to exceed ML
        if (cosA > _mitLimSqr - 1) DoMiter(path, j, k, cosA);
        else DoSquare(path, j, k);
      }
      else if (_joinType == JoinType.Round)
        DoRound(path, j, k, Math.Atan2(sinA, cosA));
      else if (_joinType == JoinType.Bevel)
        DoBevel(path, j, k);
      else
        DoSquare(path, j, k);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OffsetPolygon(Group group, Path64 path)
    {
      pathOut = new Path64();
      int cnt = path.Count;
      for (int j = 0, k = cnt - 1; j < cnt; k = j, j++)
        OffsetPoint(group, path, j, k);
      // a reversed group's outers come out negative, but the solution's outers are always positive
      if (group.pathsReversed) pathOut.Reverse();
      _solution.Add(pathOut);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OffsetOpenJoined(Group group, Path64 path)
    {
      OffsetPolygon(group, path);
      path = Clipper.ReversePath(path);
      // nb: the reversed path's normals are exactly the negated, rotated normals
      // that the C++ code derives from the forward normals
      BuildNormals(path);
      OffsetPolygon(group, path);
    }

    private void OffsetOpenPath(Group group, Path64 path, EndType endType)
    {
      pathOut = new Path64();
      int highI = path.Count - 1;

      // do the line start cap
      if (DeltaCallback != null)
        _groupDelta = DeltaCallback(path, _normals, 0, 0);

      if (Math.Abs(_groupDelta) <= Tolerance)
        pathOut.Add(path[0]);
      else
        switch (endType)
        {
          case EndType.Butt:
            DoBevel(path, 0, 0);
            break;
          case EndType.Round:
            DoRound(path, 0, 0, Math.PI);
            break;
          default:
            DoSquare(path, 0, 0);
            break;
        }

      // offset the left side going forward
      for (int j = 1, k = 0; j < highI; k = j, j++)
        OffsetPoint(group, path, j, k);

      // reverse normals ...
      for (int i = highI; i > 0; i--)
        _normals[i] = new PointD(-_normals[i - 1].x, -_normals[i - 1].y);
      _normals[0] = _normals[highI];

      // do the line end cap
      if (DeltaCallback != null)
        _groupDelta = DeltaCallback(path, _normals, highI, highI);
      if (Math.Abs(_groupDelta) <= Tolerance)
        pathOut.Add(path[highI]);
      else
        switch (endType)
        {
          case EndType.Butt:
            DoBevel(path, highI, highI);
            break;
          case EndType.Round:
            DoRound(path, highI, highI, Math.PI);
            break;
          default:
            DoSquare(path, highI, highI);
            break;
        }

      // offset the left side going back
      for (int j = highI - 1, k = highI; j > 0; k = j, j--)
        OffsetPoint(group, path, j, k);

      _solution.Add(pathOut);
    }

    private void DoGroupOffset(Group group)
    {
      if (group.endType == EndType.Polygon)
      {
        // a straight path (2 points) can now also be 'polygon' offset
        // where the ends will be treated as (180 deg.) joins
        double delta = (group.outerSign != 0) ? _delta : Math.Abs(_delta);
        _groupDelta = (group.pathsReversed) ? -delta : delta;
      }
      else
        _groupDelta = Math.Abs(_delta);

      double absDelta = Math.Abs(_groupDelta);
      _joinType = group.joinType;

      if (group.joinType == JoinType.Round || group.endType == EndType.Round)
        SetArcSteps(_groupDelta);

      for (int pathIdx = 0; pathIdx < group.inPaths.Count; pathIdx++)
      {
        if (group.IsStray(pathIdx)) continue;
        Path64 p = group.inPaths[pathIdx];
        int cnt = p.Count;
        pathOut = new Path64();
        BuildNormals(p);

        if (cnt == 1) // single point
        {
          double pointDelta = (group.endType == EndType.Polygon) ? _delta : Math.Abs(_delta);
          if (DeltaCallback != null)
          {
            pointDelta = DeltaCallback(p, _normals, 0, 0);
            _groupDelta = group.pathsReversed ? -pointDelta : pointDelta;
            absDelta = Math.Abs(_groupDelta);
          }

          // a point can only grow, so the caller's delta decides and not the group's orientation
          if (pointDelta < 1) continue;
          Point64 pt = p[0];
          // single vertex so build a circle or square ...
          if (group.joinType == JoinType.Round)
          {
            // DoRound may have left the arc steps of another path's delta
            if (DeltaCallback != null) SetArcSteps(_groupDelta);
            double radius = absDelta;
            int steps = _stepsPerRad > 0 ? (int) Math.Ceiling(_stepsPerRad * 2 * Math.PI) : 0; //#617
            pathOut = Clipper.Ellipse(pt, radius, radius, steps);
#if USINGZ
            pathOut = InternalClipper.SetZ(pathOut, pt.Z);
#endif
          }
          else
          {
            int d = (int) Math.Ceiling(absDelta);
            Rect64 r = new Rect64(pt.X - d, pt.Y - d, pt.X + d, pt.Y + d);
            pathOut = r.AsPath();
#if USINGZ
            pathOut = InternalClipper.SetZ(pathOut, pt.Z);
#endif
          }
          _solution.Add(pathOut);
          continue;
        } // end of offsetting a single point

        EndType endType = group.endType;
        if (cnt == 2 && endType == EndType.Joined)
          endType = (group.joinType == JoinType.Round) ? EndType.Round : EndType.Square;

        if (endType == EndType.Polygon)
        {
          // a path without area can only grow, so a negative delta erodes it to nothing
          bool flat = IsFlat(p);
          if (flat && DeltaCallback == null && _delta < 0) continue;
          OffsetPolygon(group, p);
          // a callback can grow some vertices and erode others, so keep only what grew
          if (flat && DeltaCallback != null) KeepGrownRegions();
        }
        else if (endType == EndType.Joined) OffsetOpenJoined(group, p);
        else OffsetOpenPath(group, p, endType);
      }
    }
  }

} // namespace