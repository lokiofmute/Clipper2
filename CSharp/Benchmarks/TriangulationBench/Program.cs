using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Clipper2Lib;
using TriangulationBench;

// usage:
//   TriangulationBench quick [filter] [--seconds s]   median ms and allocations per case, both modes
//   TriangulationBench profile <case> <true|false> <seconds>   a hot loop for dotnet-trace
//   TriangulationBench [BenchmarkDotNet args]          e.g. --filter *  (see README.md)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length > 0 && args[0] == "quick")
{
  string? filter = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
  double seconds = 1.0;
  int si = Array.IndexOf(args, "--seconds");
  if (si >= 0) seconds = double.Parse(args[si + 1], CultureInfo.InvariantCulture);
  Quick.Run(filter, seconds);
  return;
}
if (args.Length > 0 && args[0] == "profile")
{
  Case c = Inputs.All().First(x => x.Name == args[1]);
  bool del = bool.Parse(args[2]);
  double seconds = double.Parse(args[3], CultureInfo.InvariantCulture);
  Stopwatch sw = Stopwatch.StartNew();
  int n = 0;
  while (sw.Elapsed.TotalSeconds < seconds) { Clipper.Triangulate(c.Paths, c.DecPlaces, out _, del); n++; }
  Console.WriteLine($"{c.Name} del {del}: {n} runs, {sw.Elapsed.TotalMilliseconds / n:F3} ms each");
  return;
}
BenchmarkSwitcher.FromAssembly(typeof(TriangulateBench).Assembly).Run(args);

namespace TriangulationBench
{
  [MemoryDiagnoser]
  public class TriangulateBench
  {
    public static IEnumerable<Case> Cases() => Inputs.All();

    [ParamsSource(nameof(Cases))]
    public Case Input { get; set; } = null!;

    [Params(false, true)]
    public bool Delaunay { get; set; }

    [Benchmark]
    public TriangulateResult Triangulate() => Clipper.Triangulate(Input.Paths, Input.DecPlaces, out _, Delaunay);
  }

  public static class Quick
  {
    public static void Run(string? filter, double seconds)
    {
      Console.WriteLine($"{"case",-22} {"pts",7} {"del",5} {"result",-10} {"tris",7} {"median ms",10} {"min ms",9} {"alloc KB",10} {"runs",6}");
      foreach (Case c in Inputs.All())
      {
        if (filter != null && !c.Name.Contains(filter)) continue;
        foreach (bool del in new[] { false, true })
        {
          // warm up (tiered JIT), then time runs for about 'seconds'
          TriangulateResult res = TriangulateResult.fail;
          PathsD tris = new();
          for (int i = 0; i < 3; i++) res = Clipper.Triangulate(c.Paths, c.DecPlaces, out tris, del);
          List<double> times = new();
          long alloc = 0;
          Stopwatch total = Stopwatch.StartNew();
          while (times.Count < 5 || (total.Elapsed.TotalSeconds < seconds && times.Count < 10000))
          {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            Clipper.Triangulate(c.Paths, c.DecPlaces, out _, del);
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
          }
          times.Sort();
          Console.WriteLine($"{c.Name,-22} {c.Points,7} {del,5} {res,-10} {tris.Count,7} {times[times.Count / 2],10:F3} {times[0],9:F3} {alloc / 1024.0,10:F0} {times.Count,6}");
        }
      }
    }
  }
}
