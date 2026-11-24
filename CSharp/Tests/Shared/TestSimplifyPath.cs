using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestSimplifyPathTests
  {
    [TestMethod]
    public void TestSimplifyPath()
    {
      Path64 input1 = P(0, 0, 1, 1, 0, 20, 0, 21, 1, 40, 0, 41, 0, 60, 0, 61, 0, 80, 1, 81, 0, 100);
      Path64 output1 = Clipper.SimplifyPath(input1, 2, false);
      Assert.AreEqual(100, Length(output1));
      Assert.AreEqual(2, output1.Count);
    }
  }
}
