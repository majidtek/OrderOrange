using OrderOrange.ApiServer.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// The floor every store wakes up with: two salons, eight four-seat tables in
/// each, laid out side by side on the plan. Positions are computed against the
/// real canvas (1700×1000 px) with chair margins, so every table stands fully
/// inside its salon from the first render. A store that already drew ANY room
/// or table is left untouched — this only dresses a bare floor.
/// </summary>
public static class DefaultFloor
{
    private const double CanvasW = 1700, CanvasH = 1000;

    public static async Task SeedAsync(AppDbContext db, int restaurantId)
    {
        if (await db.StoreRooms.AnyAsync(r => r.RestaurantId == restaurantId) ||
            await db.StoreTables.AnyAsync(t => t.RestaurantId == restaurantId))
            return;

        var now = DateTime.Now;
        // (name, x%, y%, w%, h%, first table number, columns)
        var salons = new (string Name, double X, double Y, double W, double H, int First, int Cols)[]
        {
            ("Salon 1", 4, 6, 44, 88, 1, 2),
            ("Salon 2", 52, 6, 44, 88, 9, 2),
        };

        foreach (var salon in salons)
        {
            var room = new StoreRoom
            {
                RestaurantId = restaurantId, Name = salon.Name, Floor = "",
                X = salon.X, Y = salon.Y, W = salon.W, H = salon.H, CreatedAt = now,
            };
            db.StoreRooms.Add(room);
            await db.SaveChangesAsync(); // the tables below need the salon's key

            const int count = 8;
            var rows = (int)Math.Ceiling(count / (double)salon.Cols);
            var leftPx = salon.X / 100 * CanvasW;
            var topPx = salon.Y / 100 * CanvasH;
            var widthPx = salon.W / 100 * CanvasW;
            var heightPx = salon.H / 100 * CanvasH;
            // Half the table box + its chairs must clear the wall; a 10% inset
            // keeps the grid off the very edge in generous rooms.
            var marginX = Math.Max(16 + 92 / 2.0, widthPx * 0.10);
            var marginY = Math.Max(16 + 72 / 2.0, heightPx * 0.10);

            for (var i = 0; i < count; i++)
            {
                var col = i % salon.Cols;
                var row = i / salon.Cols;
                var fx = salon.Cols > 1 ? col / (salon.Cols - 1.0) : 0.5;
                var fy = rows > 1 ? row / (rows - 1.0) : 0.5;
                var centerX = leftPx + marginX + (widthPx - 2 * marginX) * fx;
                var centerY = topPx + marginY + (heightPx - 2 * marginY) * fy;
                db.StoreTables.Add(new StoreTable
                {
                    RestaurantId = restaurantId,
                    Name = $"T{salon.First + i}",
                    Type = "indoor",
                    Seats = 4,
                    Shape = "square",
                    RoomId = room.Id,
                    X = Math.Round(centerX / CanvasW * 100, 2),
                    Y = Math.Round(centerY / CanvasH * 100, 2),
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
