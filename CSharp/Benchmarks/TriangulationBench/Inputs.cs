using System.Globalization;
using Clipper2Lib;

namespace TriangulationBench;

// One benchmark input: paths in mm, triangulated through the PathsD overload as NodeFlow does
public sealed record Case(string Name, PathsD Paths, int DecPlaces)
{
  public int Points => Paths.Sum(p => p.Count);
  public override string ToString() => Name;
}

public static class Inputs
{
  public static IReadOnlyList<Case> All()
  {
    List<Case> cases = new()
    {
      new Case("n972-ring", new PathsD { Clipper.MakePath(N972Ring) }, 6),
      new Case("dense-circle", new PathsD { Circle(11.411, 28.589, 21.411, 5834) }, 6),
      new Case("plate-100-holes", Plate(10, 10), 6),
      new Case("plate-500-holes", Plate(25, 20), 6),
      new Case("star-10k", new PathsD { Star(10000) }, 6),
      new Case("comb-10k", new PathsD { Comb(2500) }, 6),
      new Case("grid-cad", GridCad(400), 6),
      // 2 m across: in-circle differences pass 2^30 and use the BigInteger fallback
      new Case("big-circle-bigint", new PathsD { Circle(5000, 5000, 1000, 4000) }, 6),
    };
    cases.AddRange(FromFiles());
    return cases;
  }

  public static PathD Circle(double cx, double cy, double radius, int n)
  {
    PathD path = new(n);
    for (int i = 0; i < n; i++)
    {
      double a = 2 * Math.PI * i / n;
      path.Add(new PointD(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
    }
    return path;
  }

  // a 20 mm pitch plate with cols x rows round holes (r 6 mm, 64 points)
  public static PathsD Plate(int cols, int rows)
  {
    double w = cols * 20 + 10, h = rows * 20 + 10;
    PathsD paths = new() { Clipper.MakePath(new double[] { 0, 0, w, 0, w, h, 0, h }) };
    for (int i = 0; i < cols; i++)
      for (int j = 0; j < rows; j++)
        paths.Add(Circle(15 + i * 20, 15 + j * 20, 6, 64));
    return paths;
  }

  // a concave star: every other vertex is pulled in, so half the vertices are reflex
  public static PathD Star(int n)
  {
    Random rng = new(1);
    PathD path = new(n);
    for (int i = 0; i < n; i++)
    {
      double a = 2 * Math.PI * i / n;
      double r = (i % 2 == 0 ? 100 : 80) + rng.NextDouble() * 5;
      path.Add(new PointD(Math.Round(r * Math.Cos(a), 4), Math.Round(r * Math.Sin(a), 4)));
    }
    return path;
  }

  // a comb with 'teeth' teeth 0.1 mm wide and 20 mm long, at a 0.2 mm pitch
  public static PathD Comb(int teeth)
  {
    PathD path = new(teeth * 4 + 2);
    double width = teeth * 0.2;
    path.Add(new PointD(width, -5));
    path.Add(new PointD(width, 0));
    for (int i = teeth - 1; i >= 0; i--)
    {
      double x = i * 0.2;
      path.Add(new PointD(x + 0.1, 0));
      path.Add(new PointD(x + 0.1, 20));
      path.Add(new PointD(x, 20));
      path.Add(new PointD(x, 0));
    }
    path[^1] = new PointD(0, -5);
    return path;
  }

  // CAD-like: a union of grid-aligned rectangles less some others, full of collinear runs,
  // T-touches and pinches (the fuzz harness's grid family, scaled up)
  public static PathsD GridCad(int k)
  {
    Random rng = new(7);
    PathsD subj = new(), clip = new();
    for (int s = 0; s < k; s++)
    {
      double x = rng.Next(0, 100), y = rng.Next(0, 100);
      double w = rng.Next(1, 12), h = rng.Next(1, 12);
      PathD p = Clipper.MakePath(new[] { x, y, x + w, y, x + w, y + h, x, y + h });
      (rng.Next(4) == 0 ? clip : subj).Add(p);
    }
    return Clipper.BooleanOp(ClipType.Difference, subj, clip, FillRule.NonZero, 6);
  }

  // inputs/*.txt: a header "# name decPlaces=6 ...", then one path per line as "x,y x,y ...",
  // paths separated by blank lines (or simply one per line)
  public static IEnumerable<Case> FromFiles()
  {
    string dir = Path.Combine(AppContext.BaseDirectory, "inputs");
    if (!Directory.Exists(dir)) yield break;
    foreach (string file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f))
    {
      string name = Path.GetFileNameWithoutExtension(file);
      int dec = 6;
      PathsD paths = new();
      foreach (string raw in File.ReadLines(file))
      {
        string line = raw.Trim();
        if (line.Length == 0) continue;
        if (line.StartsWith('#'))
        {
          foreach (string tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (tok.StartsWith("decPlaces=")) dec = int.Parse(tok["decPlaces=".Length..], CultureInfo.InvariantCulture);
          continue;
        }
        PathD path = new();
        foreach (string pt in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
          string[] xy = pt.Split(',');
          path.Add(new PointD(double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture)));
        }
        if (path.Count > 0) paths.Add(path);
      }
      if (paths.Count > 0) yield return new Case(name, paths, dec);
    }
  }

  // NodeFlow's N972 input (copied from Tests/Shared/TestTriangulation.cs)
  public static readonly double[] N972Ring =
  {
    32.8219997882843, 28.58900010585785, 32.73066423738885, 30.56455794055928, 32.45743682741875, 32.523261012377105,
    32.00464863829859, 34.448398357321835, 31.37616269920931, 36.323545382350716, 30.57734103058533, 38.132703994204405,
    29.614998897246764, 39.86043908949794, 28.497346662962972, 41.4920102415805, 27.23391974252379, 43.013497460658314,
    25.835497248942616, 44.41191995423949, 24.314010029864804, 45.67534687467867, 22.68243887778224, 46.79299910896246,
    20.954703782488707, 47.75534124230103, 19.145545170635017, 48.55416291092501, 17.270398145606144, 49.18264885001429,
    15.345260800661414, 49.63543703913445, 13.38655772884358, 49.90866444910455, 11.410999894142153, 50,
    9.435442059440724, 49.908664449104556, 7.476738987622891, 49.63543703913445, 5.551601642678159, 49.18264885001429,
    3.676454617649285, 48.55416291092501, 1.8672960057955947, 47.75534124230103, 0.139560910502059, 46.79299910896246,
    -1.4920102415805, 45.675346874678674, -3.0134974606583143, 44.41191995423949, -4.411919954239485, 43.013497460658314,
    -5.67534687467867, 41.4920102415805, -6.792999108962459, 39.860439089497945, -7.755341242301025, 38.132703994204405,
    -8.554162910925005, 36.32354538235072, -9.182648850014285, 34.44839835732184, -9.635437039134452, 32.523261012377105,
    -9.908664449104553, 30.564557940559283, -10, 28.589000105857853, -9.908664449104556, 26.61344227115643,
    -9.635437039134452, 24.654739199338596, -9.182648850014285, 22.72960185439387, -8.554162910925005, 20.85445482936498,
    -7.755341242301032, 19.045296217511304, -6.792999108962459, 17.31756112221776, -5.675346874678674, 15.685989970135202,
    -4.411919954239487, 14.164502751057384, -3.013497460658318, 12.766080257476217, -1.4920102415805037, 11.502653337037032,
    0.1395609105020572, 10.38500110275324, 1.8672960057955983, 9.422658969414666, 3.6764546176492843, 8.62383730079069,
    5.551601642678155, 7.995351361701413, 7.4767389876228805, 7.54256317258125, 9.435442059440724, 7.269335762611142,
    11.410999894142147, 7.178000211715698, 13.38655772884357, 7.269335762611142, 15.345260800661414, 7.5425631725812465,
    17.27039814560614, 7.995351361701413, 19.14554517063501, 8.62383730079069, 20.954703782488696, 9.422658969414666,
    22.68243887778224, 10.38500110275324, 24.314010029864797, 11.502653337037025, 25.83549724894262, 12.766080257476217,
    27.23391974252378, 14.164502751057373, 28.49734666296297, 15.685989970135195, 29.614998897246757, 17.317561122217754,
    30.577341030585334, 19.045296217511297, 31.37616269920931, 20.854454829364983, 32.00464863829858, 22.729601854393852,
    32.457436827418746, 24.65473919933858, 32.73066423738886, 26.61344227115642
  };
}
