using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class ReviewsController(AppDbContext db) : ApiControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Customer,RestaurantOwner,Driver")]
    public async Task<IActionResult> Create(CreateReviewRequest req)
    {
        if (req.RestaurantRating is < 1 or > 5)
            return BadRequest(new { message = "Restaurant rating must be between 1 and 5." });
        if (req.DriverRating is < 1 or > 5)
            return BadRequest(new { message = "Driver rating must be between 1 and 5." });

        var order = await db.Orders.Include(o => o.Review)
            .FirstOrDefaultAsync(o => o.Id == req.OrderId && o.CustomerId == CurrentUserId);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Delivered)
            return BadRequest(new { message = "Only delivered orders can be reviewed." });
        if (order.Review is not null)
            return BadRequest(new { message = "This order was already reviewed." });

        db.Reviews.Add(new Review
        {
            OrderId = order.Id,
            RestaurantId = order.RestaurantId,
            CustomerId = order.CustomerId,
            DriverUserId = order.DriverUserId,
            RestaurantRating = req.RestaurantRating,
            DriverRating = order.DriverUserId is null ? null : req.DriverRating,
            Comment = req.Comment?.Trim(),
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The owner's own review feed.</summary>
    [HttpGet("restaurant")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<List<ReviewDto>> ForMyRestaurant() =>
        await db.Reviews.Where(r => r.RestaurantId == CurrentRestaurantId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Join(db.Users, r => r.CustomerId, u => u.Id, (r, u) =>
                new ReviewDto(r.Id, u.FullName, r.RestaurantRating, r.DriverRating, r.Comment, r.CreatedAt))
            .ToListAsync();
}
