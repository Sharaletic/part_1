using System.Linq;
using Moq;
using NUnit.Framework;

namespace async_tests.Tests;

[TestFixture]
public class PrimesAmountTests
{
    [Test]
    public void CountPrimesAmount_ReturnsAllPrimesBelowBorder()
    {
        const int border = 20;
        var expected = new[] { 2, 3, 5, 7, 11, 13, 17, 19 };
        var primeService = new Mock<PrimeService>();
        primeService
            .Setup(service => service.CheckIsPrime(It.IsAny<int>()))
            .Returns<int>(number => expected.Contains(number));

        var result = new PrimesAmount(border).CountPrimesAmount(primeService.Object);

        NUnit.Framework.Assert.That(result.OrderBy(number => number), Is.EqualTo(expected));
        primeService.Verify(service => service.ClearCache(), Times.Once());
    }
}
