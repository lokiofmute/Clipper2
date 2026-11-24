using System;
using System.IO;

namespace Clipper2Lib.UnitTests
{
  // Locates the shared test data in <repo>/Tests, which the C++ suite uses too.
  internal static class TestData
  {
    private static readonly Lazy<string> testsDir = new(FindTestsDir);

    public static string Path(string fileName) =>
      System.IO.Path.Combine(testsDir.Value, fileName);

    private static string FindTestsDir()
    {
      DirectoryInfo? dir = new(AppContext.BaseDirectory);
      while (dir != null)
      {
        string candidate = System.IO.Path.Combine(dir.FullName, "Tests");
        if (File.Exists(System.IO.Path.Combine(candidate, "Polygons.txt"))) return candidate;
        dir = dir.Parent;
      }
      throw new DirectoryNotFoundException(
        "No Tests/Polygons.txt above " + AppContext.BaseDirectory);
    }
  }
}
