using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using Order.API.Metrics;
using Order.API.Models;

namespace Order.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HttpClient _catalogClient;

    public OrdersController(AppDbContext db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _catalogClient = httpClientFactory.CreateClient("CatalogApi");
    }

    private int GetUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.Parse(sub!);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { message = "Order must contain at least one item." });

        var sw = Stopwatch.StartNew();

        var order = new OrderEntity
        {
            UserId = GetUserId(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        decimal total = 0;

        foreach (var item in req.Items)
        {
            // Call catalog-api to validate product and get price
            var response = await _catalogClient.GetAsync($"/api/catalog/products/{item.ProductId}");
            if (!response.IsSuccessStatusCode)
                return BadRequest(new { message = $"Product {item.ProductId} not found." });

            var product = await response.Content.ReadFromJsonAsync<CatalogProduct>();
            if (product == null)
                return BadRequest(new { message = $"Product {item.ProductId} not found." });

            if (product.Stock < item.Quantity)
                return BadRequest(new { message = $"Insufficient stock for '{product.Name}'. Available: {product.Stock}" });

            var lineTotal = product.Price * item.Quantity;
            total += lineTotal;

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = item.Quantity
            });
        }

        order.Total = total;
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        sw.Stop();
        AppMetrics.ProcessingDuration.Observe(sw.Elapsed.TotalSeconds);
        AppMetrics.OrderCreatedTotal.WithLabels("Pending").Inc();
        AppMetrics.OrderValueTotal.Inc(Convert.ToDouble(total));
        AppMetrics.ItemsPerOrder.Observe(order.Items.Count);

        return Created($"/api/orders/{order.Id}", order);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetUserId();
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var userId = GetUserId();
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null) return NotFound();
        return Ok(order);
    }

    [HttpPatch("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = GetUserId();
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null) return NotFound();

        if (order.Status == "Cancelled")
            return BadRequest(new { message = "Order is already cancelled." });

        if (order.Status != "Pending")
            return BadRequest(new { message = $"Cannot cancel order with status '{order.Status}'." });

        order.Status = "Cancelled";
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        AppMetrics.OrderCreatedTotal.WithLabels("Cancelled").Inc();

        return Ok(order);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null) return NotFound();

        if (order.Status != "Cancelled")
            return BadRequest(new { message = "Only cancelled orders can be removed." });

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
