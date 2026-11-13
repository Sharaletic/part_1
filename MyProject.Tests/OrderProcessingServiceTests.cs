using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shop.Orders;

namespace Shop.Orders.Tests;

[TestFixture]
public class OrderProcessingServiceTests
{
    [Test]
    public async Task PlaceOrderAsync_FirstLineOutOfStock_ReturnsOutOfStockWithoutRelease()
    {
        var (service, inventory) = CreateSut();
        inventory.Setup(i => i.TryReserveAsync("A", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await service.PlaceOrderAsync(Request(new OrderLine("A", 2, 10m)));

        NUnit.Framework.Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        inventory.Verify(i => i.ReleaseAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Test]
    public async Task PlaceOrderAsync_LaterLineOutOfStock_ReleasesPriorReservationsAndStops()
    {
        var (service, inventory) = CreateSut();
        inventory.Setup(i => i.TryReserveAsync("C", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await service.PlaceOrderAsync(Request(
            new OrderLine("A", 1, 10m),
            new OrderLine("B", 2, 10m),
            new OrderLine("C", 3, 10m),
            new OrderLine("D", 4, 10m)));

        NUnit.Framework.Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        inventory.Verify(i => i.ReleaseAsync("A", 1, CancellationToken.None), Times.Once());
        inventory.Verify(i => i.ReleaseAsync("B", 2, CancellationToken.None), Times.Once());
        inventory.Verify(i => i.ReleaseAsync("C", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        inventory.Verify(i => i.TryReserveAsync("D", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static (OrderProcessingService Service, Mock<IInventoryService> Inventory) CreateSut()
    {
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(i => i.TryReserveAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        inventory.Setup(i => i.ReleaseAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var discounts = new Mock<IDiscountEngine>();
        discounts.Setup(d => d.GetApplicableAsync(
                It.IsAny<Customer>(), It.IsAny<IReadOnlyList<OrderLine>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Discount>)Array.Empty<Discount>());

        var tax = new Mock<ITaxCalculator>();
        tax.Setup(t => t.Calculate(It.IsAny<string>(), It.IsAny<decimal>())).Returns(0m);

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        var service = new OrderProcessingService(
            orders.Object,
            inventory.Object,
            new Mock<IPaymentGateway>().Object,
            discounts.Object,
            tax.Object,
            new Mock<INotificationService>().Object,
            new Mock<IUnitOfWork>().Object,
            clock.Object,
            new Mock<IDelayer>().Object,
            new OrderOptions(),
            new Mock<ILogger<OrderProcessingService>>().Object);

        return (service, inventory);
    }

    private static PlaceOrderRequest Request(params OrderLine[] lines) =>
        new("key", new Customer(Guid.NewGuid(), "test@example.com", CustomerTier.Standard, "FI"), lines, "token");
}
