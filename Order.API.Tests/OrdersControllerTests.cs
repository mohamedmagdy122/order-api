using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Order.API.Controllers;
using Order.API.Data;
using Order.API.Models;
using Xunit;

namespace Order.API.Tests;

public class OrdersControllerTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private OrdersController CreateController(AppDbContext db, HttpClient? catalogClient = null)
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("CatalogApi"))
            .Returns(catalogClient ?? new HttpClient());

        var controller = new OrdersController(db, mockFactory.Object);

        // Set up a fake user with userId=1
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new(ClaimTypes.NameIdentifier, "1")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private HttpClient CreateMockCatalogClient(int productId = 1, string name = "Wireless Mouse", decimal price = 29.99m, int stock = 100)
    {
        var handler = new MockCatalogHandler(productId, name, price, stock);
        return new HttpClient(handler) { BaseAddress = new Uri("http://catalog-api:8080") };
    }

    [Fact]
    public async Task Create_ValidOrder_ReturnsCreated()
    {
        var db = CreateDb();
        var catalogClient = CreateMockCatalogClient();
        var controller = CreateController(db, catalogClient);

        var result = await controller.Create(new CreateOrderRequest
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 2 }
            }
        });

        var created = Assert.IsType<CreatedResult>(result);
        var order = Assert.IsType<OrderEntity>(created.Value);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(59.98m, order.Total);
        Assert.Single(order.Items);
    }

    [Fact]
    public async Task Create_EmptyItems_ReturnsBadRequest()
    {
        var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.Create(new CreateOrderRequest
        {
            Items = new List<OrderItemRequest>()
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetMyOrders_ReturnsUserOrders()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            UserId = 1,
            Status = "Pending",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new() { ProductId = 1, ProductName = "Mouse", UnitPrice = 29.99m, Quantity = 1 }
            }
        });
        db.Orders.Add(new OrderEntity
        {
            UserId = 2, // different user
            Status = "Pending",
            Total = 10m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetMyOrders();

        var ok = Assert.IsType<OkObjectResult>(result);
        var orders = Assert.IsAssignableFrom<IEnumerable<OrderEntity>>(ok.Value);
        Assert.Single(orders);
    }

    [Fact]
    public async Task GetById_OwnOrder_ReturnsOrder()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Pending",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetById(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var order = Assert.IsType<OrderEntity>(ok.Value);
        Assert.Equal(1, order.Id);
    }

    [Fact]
    public async Task GetById_OtherUsersOrder_ReturnsNotFound()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 999,
            Status = "Pending",
            Total = 10m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetById(1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Cancel_PendingOrder_ReturnsCancelled()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Pending",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Cancel(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var order = Assert.IsType<OrderEntity>(ok.Value);
        Assert.Equal("Cancelled", order.Status);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsBadRequest()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Cancelled",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Cancel(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cancel_CompletedOrder_ReturnsBadRequest()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Completed",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Cancel(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cancel_NonExistentOrder_ReturnsNotFound()
    {
        var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.Cancel(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_CancelledOrder_ReturnsNoContent()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Cancelled",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Delete(1);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Delete_PendingOrder_ReturnsBadRequest()
    {
        var db = CreateDb();
        db.Orders.Add(new OrderEntity
        {
            Id = 1,
            UserId = 1,
            Status = "Pending",
            Total = 29.99m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Delete(1);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(1, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Delete_NonExistentOrder_ReturnsNotFound()
    {
        var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }
}

// Simple mock HTTP handler for catalog API calls
internal class MockCatalogHandler : HttpMessageHandler
{
    private readonly int _productId;
    private readonly string _name;
    private readonly decimal _price;
    private readonly int _stock;

    public MockCatalogHandler(int productId, string name, decimal price, int stock)
    {
        _productId = productId;
        _name = name;
        _price = price;
        _stock = stock;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = JsonContent.Create(new CatalogProduct
        {
            Id = _productId,
            Name = _name,
            Price = _price,
            Stock = _stock
        });
        return Task.FromResult(response);
    }
}
