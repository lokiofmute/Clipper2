using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Clipper2Lib.UnitTests.TestHelpers;

namespace Clipper2Lib.UnitTests
{
  [TestClass]
  public class TestTrimCollinearTests
  {
    [TestMethod]
    public void TestTrimCollinear()
    {
      Path64 input1 = P(10, 10, 10, 10, 50, 10, 100, 10, 100, 100, 10, 100, 10, 10, 20, 10);
      Path64 output1 = Clipper.TrimCollinear(input1, false);
      Assert.AreEqual(4, output1.Count);
      Path64 input2 = P(10, 10, 10, 10, 100, 10, 100, 100, 10, 100, 10, 10, 10, 10);
      Path64 output2 = Clipper.TrimCollinear(input2, true);
      Assert.AreEqual(5, output2.Count);
      Path64 input3 = P(10, 10, 10, 50, 10, 10, 50, 10, 50, 50,
        50, 10, 70, 10, 70, 50, 70, 10, 50, 10, 100, 10, 100, 50, 100, 10);
      Path64 output3 = Clipper.TrimCollinear(input3);
      Assert.AreEqual(0, output3.Count);
      Path64 input4 = P(2, 3, 3, 4, 4, 4, 4, 5, 7, 5,
        8, 4, 8, 3, 9, 3, 8, 3, 7, 3, 6, 3, 5, 3, 4, 3, 3, 3, 2, 3);
      Path64 output4a = Clipper.TrimCollinear(input4);
      Path64 output4b = Clipper.TrimCollinear(output4a);
      int area4a = (int) Clipper.Area(output4a);
      int area4b = (int) Clipper.Area(output4b);
      Assert.AreEqual(7, output4a.Count);
      Assert.AreEqual(-9, area4a);
      Assert.AreEqual(output4a.Count, output4b.Count);
      Assert.AreEqual(area4a, area4b);
    }
  }
}
