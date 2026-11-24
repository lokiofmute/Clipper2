using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestIsCollinearTests
  {
    private static void ExpectHi(ulong a, ulong b, ulong hi)
    {
      Assert.AreEqual(hi, InternalClipper.MultiplyUInt64(a, b).hi64);
    }

    [TestMethod]
    public void TesthiCalculation()
    {
      ExpectHi(0x51eaed81157de061, 0x3a271fb2745b6fe9, 0x129bbebdfae0464e);
      ExpectHi(0x3a271fb2745b6fe9, 0x51eaed81157de061, 0x129bbebdfae0464e);
      ExpectHi(0xc2055706a62883fa, 0x26c78bc79c2322cc, 0x1d640701d192519b);
      ExpectHi(0xc2055706a62883fa, 0x26c78bc79c2322cc, 0x1d640701d192519b);
      ExpectHi(0x26c78bc79c2322cc, 0xc2055706a62883fa, 0x1d640701d192519b);
      ExpectHi(0x874ddae32094b0de, 0x9b1559a06fdf83e0, 0x51f76c49563e5bfe);
      ExpectHi(0x9b1559a06fdf83e0, 0x874ddae32094b0de, 0x51f76c49563e5bfe);
      ExpectHi(0x81fb3ad3636ca900, 0x239c000a982a8da4, 0x12148e28207b83a3);
      ExpectHi(0x239c000a982a8da4, 0x81fb3ad3636ca900, 0x12148e28207b83a3);
      ExpectHi(0x4be0b4c5d2725c44, 0x990cd6db34a04c30, 0x2d5d1a4183fd6165);
      ExpectHi(0x990cd6db34a04c30, 0x4be0b4c5d2725c44, 0x2d5d1a4183fd6165);
      ExpectHi(0x978ec0c0433c01f6, 0x2df03d097966b536, 0x1b3251d91fe272a5);
      ExpectHi(0x2df03d097966b536, 0x978ec0c0433c01f6, 0x1b3251d91fe272a5);
      ExpectHi(0x49c5cbbcfd716344, 0xc489e3b34b007ad3, 0x38a32c74c8c191a4);
      ExpectHi(0xc489e3b34b007ad3, 0x49c5cbbcfd716344, 0x38a32c74c8c191a4);
      ExpectHi(0xd3361cdbeed655d5, 0x1240da41e324953a, 0x0f0f4fa11e7e8f2a);
      ExpectHi(0x1240da41e324953a, 0xd3361cdbeed655d5, 0x0f0f4fa11e7e8f2a);
      ExpectHi(0x51b854f8e71b0ae0, 0x6f8d438aae530af5, 0x239c04ee3c8cc248);
      ExpectHi(0x6f8d438aae530af5, 0x51b854f8e71b0ae0, 0x239c04ee3c8cc248);
      ExpectHi(0xbbecf7dbc6147480, 0xbb0f73d0f82e2236, 0x895170f4e9a216a7);
      ExpectHi(0xbb0f73d0f82e2236, 0xbbecf7dbc6147480, 0x895170f4e9a216a7);
    }

    [TestMethod]
    public void TestIsCollinear()
    {
      // a large integer not representable by double
      const long i = 9007199254740993;
      Point64 pt1 = new(0, 0);
      Point64 sharedPt = new(i, i * 10);
      Point64 pt2 = new(i * 10, i * 100);
      Assert.IsTrue(InternalClipper.IsCollinear(pt1, sharedPt, pt2));
    }

    [TestMethod]
    public void TestIsCollinear2()
    {
      // see https://github.com/AngusJohnson/Clipper2/issues/831
      const long i = 0x4000000000000;
      Path64 subject = new() { new Point64(-i, -i), new Point64(i, -i), new Point64(-i, i), new Point64(i, i) };
      Clipper64 clipper = new();
      clipper.AddSubject(new Paths64 { subject });
      Paths64 solution = new();
      clipper.Execute(ClipType.Union, FillRule.EvenOdd, solution);
      Assert.AreEqual(2, solution.Count);
    }
  }
}
