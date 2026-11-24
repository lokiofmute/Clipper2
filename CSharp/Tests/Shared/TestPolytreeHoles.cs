using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestPolytreeHoles
  {
    [TestMethod]
    public void TestPolytreeHoles1()
    {
      Paths64 subject = new(), subjectOpen = new(), clip = new();
      Assert.IsTrue(LoadTestNum("PolytreeHoleOwner.txt", 1, subject, subjectOpen, clip,
        out _, out _, out ClipType ct, out FillRule fr));
      Clipper64 c = new();
      c.AddSubject(subject);
      c.AddOpenSubject(subjectOpen);
      c.AddClip(clip);
      PolyTree64 solution = new();
      Paths64 solutionOpen = new();
      c.Execute(ct, fr, solution, solutionOpen);
      Assert.IsTrue(CheckPolytreeFullyContainsChildren(solution));
    }

    [TestMethod]
    public void TestPolytreeHoles2()
    {
      Paths64 subject = new(), subjectOpen = new(), clip = new();
      Assert.IsTrue(LoadTestNum("PolytreeHoleOwner2.txt", 1, subject, subjectOpen, clip,
        out _, out _, out ClipType ct, out _));
      Point64[] pointsOfInterestOutside = {
        new(21887, 10420), new(21726, 10825), new(21662, 10845), new(21617, 10890) };
      // confirm that each 'pointsOfInterestOutside' is outside every subject
      foreach (Point64 poi in pointsOfInterestOutside)
      {
        int outsideSubjectCount = 0;
        foreach (Path64 path in subject)
          if (Clipper.PointInPolygon(poi, path) != PointInPolygonResult.IsOutside)
            ++outsideSubjectCount;
        Assert.AreEqual(0, outsideSubjectCount);
      }
      Point64[] pointsOfInterestInside = {
        new(21887, 10430), new(21843, 10520), new(21810, 10686), new(21900, 10461) };
      // confirm that each 'pointsOfInterestInside' is inside exactly one subject
      foreach (Point64 poi in pointsOfInterestInside)
      {
        int insideSubjectCount = 0;
        foreach (Path64 path in subject)
          if (Clipper.PointInPolygon(poi, path) != PointInPolygonResult.IsOutside)
            ++insideSubjectCount;
        Assert.AreEqual(1, insideSubjectCount);
      }
      PolyTree64 solutionTree = new();
      Paths64 solutionOpen = new();
      Clipper64 c = new();
      c.AddSubject(subject);
      c.AddOpenSubject(subjectOpen);
      c.AddClip(clip);
      c.Execute(ct, FillRule.Negative, solutionTree, solutionOpen);
      Paths64 solutionPaths = Clipper.PolyTreeToPaths64(solutionTree);
      Assert.AreNotEqual(0, solutionPaths.Count);
      double subjectArea = -Clipper.Area(subject); // negate (see fillrule)
      double solutionTreeArea = GetPolytreeArea(solutionTree);
      double solutionPathsArea = Clipper.Area(solutionPaths);
      Assert.IsTrue(solutionPathsArea < subjectArea);
      Assert.IsTrue(solutionPathsArea > subjectArea * 0.92);
      Assert.AreEqual(solutionPathsArea, solutionTreeArea, 0.0001);
      Assert.IsTrue(CheckPolytreeFullyContainsChildren(solutionTree));
      foreach (Point64 poi in pointsOfInterestOutside)
        Assert.IsFalse(PolytreeContainsPoint(solutionTree, poi));
      foreach (Point64 poi in pointsOfInterestInside)
        Assert.IsTrue(PolytreeContainsPoint(solutionTree, poi));
    }

    [TestMethod]
    public void TestPolytreeHoles3()
    {
      Paths64 subject = new(), clip = new();
      PolyTree64 solution = new();
      Clipper64 c = new();
  subject.Add(P( 1072,501, 1072,501, 1072,539, 1072,539, 1072,539, 870,539,
    870,539, 870,539, 870,520, 894,520, 898,524, 911,524, 915,520, 915,520, 936,520,
    940,524, 953,524, 957,520, 957,520, 978,520, 983,524, 995,524, 1000,520, 1021,520,
    1025,524, 1038,524, 1042,520, 1038,516, 1025,516, 1021,520, 1000,520, 995,516,
    983,516, 978,520, 957,520, 953,516, 940,516, 936,520, 915,520, 911,516, 898,516,
    894,520, 870,520, 870,516, 870,501, 870,501, 870,501, 1072,501 ));
  clip.Add(P( 870,501, 971,501, 971,539, 870,539 ));
      c.AddSubject(subject);
      c.AddClip(clip);
      c.Execute(ClipType.Intersection, FillRule.NonZero, solution);
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 2);
    }

    [TestMethod]
    public void TestPolytreeHoles4() // #618
    {
      Paths64 subject = new();
      PolyTree64 solution = new();
      Clipper64 c = new();
  subject.Add(P( 50,500, 50,300, 100,300, 100,350, 150,350,
    150,250, 200,250, 200,450, 350,450, 350,200, 400,200, 400,225, 450,225,
    450,175, 400,175, 400,200, 350,200, 350,175, 200,175, 200,250, 150,250,
    150,200, 100,200, 100,300, 50,300, 50,125, 500,125, 500,500 ));
  subject.Add(P( 250,425, 250,375,  300,375, 300,425 ));
      c.AddSubject(subject);
      c.Execute(ClipType.Union, FillRule.NonZero, solution);
      // Polytree root
      //   +- Polygon with 3 holes.
      //      +- Hole with 1 nested polygon.
      //         +-Polygon
      //      +- Hole
      //      +- Hole
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 3);
    }

    [TestMethod]
    public void TestPolytreeHoles5()
    {
      Paths64 subject = new(), clip = new();
  subject.Add(P( 0,30, 400,30, 400,100, 0,100 ));
  clip.Add(P( 20,30, 30,30, 30,150, 20,150 ));
  clip.Add(P( 200,0, 300,0, 300,30, 280,30, 280,20, 220,20, 220,30, 200,30 ));
  clip.Add(P( 200,50, 300,50, 300,80, 200,80 ));
      Clipper64 c = new();
      c.AddSubject(subject);
      c.AddClip(clip);
      PolyTree64 tree = new();
      c.Execute(ClipType.Xor, FillRule.NonZero, tree);
      // Polytree with 3 polygons; polygon (2) contains 2 holes.
      Assert.IsTrue(tree.Count == 3 && tree[2].Count == 2);
    }

    [TestMethod]
    public void TestPolytreeHoles6() // #618
    {
      Paths64 subject = new(), clip = new();
  subject.Add(P( 150,50, 200,50, 200,100, 150,100 ));
  subject.Add(P( 125,100, 150,100, 150,150, 125,150 ));
  subject.Add(P( 225,50, 300,50, 300,80, 225,80 ));
  subject.Add(P( 225,100, 300,100, 300,150, 275,150, 275,175, 260,175,
    260,250, 235,250, 235,300, 275,300, 275,275, 300,275, 300,350, 225,350 ));
  subject.Add(P( 300,150, 350,150, 350,175, 300,175 ));
  clip.Add(P( 0,0, 400,0, 400,50, 0,50 ));
  clip.Add(P( 0,100, 400,100, 400,150, 0,150 ));
  clip.Add(P( 260,175, 325,175, 325,275, 260,275 ));
      Clipper64 c = new();
      c.AddSubject(subject);
      c.AddClip(clip);
      PolyTree64 tree = new();
      c.Execute(ClipType.Xor, FillRule.NonZero, tree);
      // Polytree with 3 polygons; polygon (2) contains 1 hole.
      Assert.IsTrue(tree.Count == 3 && tree[2].Count == 1);
    }

    [TestMethod]
    public void TestPolytreeHoles7() // #618
    {
      Paths64 subject = new();
  subject.Add(P( 0, 0, 100000, 0, 100000, 100000, 200000, 100000,
    200000, 0, 300000, 0, 300000, 200000, 0, 200000 ));
  subject.Add(P( 0, 0, 0, -100000, 250000, -100000, 250000, 0 ));
      PolyTree64 polytree = new();
      Clipper64 c = new();
      c.AddSubject(subject);
      c.Execute(ClipType.Union, FillRule.NonZero, polytree);
      Assert.IsTrue(polytree.Count == 1 && polytree[0].Count == 1);
    }

    [TestMethod]
    public void TestPolytreeHoles8() // #942
    {
      Paths64 subject = new();
      PolyTree64 solution = new();
      Clipper64 c = new();
  subject.Add(P( 1588700,-8717600, 1616200,-8474800,
    1588700,-8474800 ));
  subject.Add(P( 13583800,-15601600, 13582800,-15508500,
    13555300,-15508500, 13555500,-15182200, 13010900,-15185400 ));
  subject.Add(P( 956700,-3092300, 1152600,3147400, 25600,3151700 ));
  subject.Add(P( 22575900,-16604000, 31286800,-12171900,
    31110200,4882800, 30996200,4826300, 30414400,5447400, 30260000,5391500,
    29662200,5805400, 28844500,5337900, 28435000,5789300, 27721400,5026400,
    22876300,5034300, 21977700,4414900, 21148000,4654700, 20917600,4653400,
    19334300,12411000, -2591700,12177200, 53200,3151100, -2564300,12149800,
    7819400,4692400, 10116000,5228600, 6975500,3120100, 7379700,3124700,
    11037900,596200, 12257000,2587800, 12257000,596200, 15227300,2352700,
    18444400,1112100, 19961100,5549400, 20173200,5078600, 20330000,5079300,
    20970200,4544300, 20989600,4563700, 19465500,1112100, 21611600,4182100,
    22925100,1112200, 22952700,1637200, 23059000,1112200, 24908100,4181200,
    27070100,3800600, 27238000,3800700, 28582200,520300, 29367800,1050100,
    29291400,179400, 29133700,360700, 29056700,312600, 29121900,332500,
    29269900,162300, 28941400,213100, 27491300,-3041500, 27588700,-2997800,
    22104900,-16142800, 13010900,-15603000, 13555500,-15182200,
    13555300,-15508500, 13582800,-15508500, 13583100,-15154700,
    1588700,-8822800, 1588700,-8379900, 1588700,-8474800, 1616200,-8474800,
    1003900,-630100, 1253300,-12284500, 12983400,-16239900 ));
  subject.Add(P( 198200,12149800, 1010600,12149800, 1011500,11859600 ));
  subject.Add(P( 21996700,-7432000, 22096700,-7432000, 22096700,-7332000 ));
      c.AddSubject(subject);
      c.Execute(ClipType.Union, FillRule.NonZero, solution);
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 2 && solution[0][1].Count == 1);
    }

    [TestMethod]
    public void TestPolytreeHoles9() // #957
    {
      Paths64 subject = new() {
    P( 77910,46865, 78720,46865, 78720,48000, 77910,48000, 77910,46865),
    P( 82780,53015, 93600,53015, 93600,54335, 82780,54335, 82780,53015),
    P( 82780,48975, 84080,48975, 84080,53015, 82780,53015, 82780,48975),
    P( 77910,48000, 84080,48000, 84080,48975, 77910,48975, 77910,48000),
    P( 89880,40615, 90700,40615, 90700,46865, 89880,46865, 89880,40615),
    P( 92700,54335, 93600,54335, 93600,61420, 92700,61420, 92700,54335),
    P( 78950,47425, 84080,47425, 84080,47770, 78950,47770, 78950,47425),
    P( 82780,61420, 93600,61420, 93600,62435, 82780,62435, 82780,61420),
    P( 101680,63085, 100675,63085, 100675,47770, 100680,47770, 100680,40615, 101680,40615, 101680,63085),
    P( 76195,39880, 89880,39880, 89880,41045, 76195,41045, 76195,39880),
    P( 85490,56145, 90520,56145, 90520,59235, 85490,59235, 85490,56145),
    P( 89880,39880, 101680,39880, 101680,40615, 89880,40615, 89880,39880),
    P( 89880,46865, 100680,46865, 100680,47770, 89880,47770, 89880,46865),
    P( 82780,54335, 83280,54335, 83280,61420, 82780,61420, 82780,54335),
    P( 76195,41045, 76855,41045, 76855,62665, 76195,62665, 76195,41045),
    P( 76195,62665, 100675,62665, 100675,63085, 76195,63085, 76195,62665),
    P( 82780,41045, 84080,41045, 84080,47425, 82780,47425, 82780,41045) };
      PolyTree64 solution = new();
      Clipper64 c = new();
      c.AddSubject(subject);
      c.Execute(ClipType.Union, FillRule.NonZero, solution);
      //  Polytree with 1 polygon.
      //    + -Polygon(0) contains 2 holes.
      //    + -Hole(0) contains 1 nested polygon.
      //    + -Polygon(0) contains 1 hole.
      //    + -Hole(0) contains 1 nested polygon.
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 2 && solution[0][0].Count == 1);
    }

    [TestMethod]
    public void TestPolytreeHoles10() // #973
    {
      Paths64 subject = new() {
      P( 0,0,  79530,0,  79530,940,  0,940,  0,0 ),
      P( 0,33360,  79530,33360,  79530,34300,  0,34300,  0,33360 ),
      P( 78470,940,  79530,940,  79530,33360,  78470,33360,  78470,940 ),
      P( 0,940,  940,940,  940,33360,  0,33360,  0,940 ),
      P( 29290,940,  30350,940,  30350,33360,  29290,33360,  29290,940 )
      };
      Clipper64 c = new();
      c.AddSubject(subject);
      PolyTree64 solution = new();
      c.Execute(ClipType.Union, FillRule.NonZero, solution);
      // Polytree with 1 polygon; polygon(0) contains 2 holes.
      Assert.IsTrue(solution.Count == 1 && solution[0].Count == 2);
    }
  }
}
