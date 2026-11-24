using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestRounding
  {
    [TestMethod]
    public void TestRoundToInt64()
    {
      // The C++ test also loops over the FE_* rounding modes; .NET always rounds in
      // round-to-nearest mode, so only the fixed-value assertions are ported.
      // RoundToInt64 must round ties to even, like std::nearbyint in that mode.
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(0.0));
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(-0.0));
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(0.5));
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(-0.5));
      Assert.AreEqual(2L, InternalClipper.RoundToInt64(1.5));
      Assert.AreEqual(-2L, InternalClipper.RoundToInt64(-1.5));
      Assert.AreEqual(2L, InternalClipper.RoundToInt64(2.5));
      Assert.AreEqual(-2L, InternalClipper.RoundToInt64(-2.5));
      Assert.AreEqual(2L, InternalClipper.RoundToInt64(2.4));
      Assert.AreEqual(-3L, InternalClipper.RoundToInt64(-2.6));
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(0.49999999999999994));
      Assert.AreEqual(0L, InternalClipper.RoundToInt64(-0.49999999999999994));
      Assert.AreEqual(4503599627370496L, InternalClipper.RoundToInt64(4503599627370495.5));
      Assert.AreEqual(-4503599627370496L, InternalClipper.RoundToInt64(-4503599627370495.5));
      Assert.AreEqual(9007199254740992L, InternalClipper.RoundToInt64(9007199254740992.0));
      Assert.AreEqual(-9007199254740994L, InternalClipper.RoundToInt64(-9007199254740994.0));
      Assert.AreEqual(1000000000000000000L, InternalClipper.RoundToInt64(1e18));
      Assert.AreEqual((long) InternalClipper.max_coord, InternalClipper.RoundToInt64(InternalClipper.max_coord));
      Assert.AreEqual((long) InternalClipper.min_coord, InternalClipper.RoundToInt64(InternalClipper.min_coord));
    }
  }
}
