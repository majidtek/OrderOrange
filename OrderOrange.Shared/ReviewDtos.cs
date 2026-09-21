namespace OrderOrange.Shared;

public record CreateReviewRequest(int OrderId, int RestaurantRating, int? DriverRating, string? Comment);

public record ReviewDto(
    int Id,
    string CustomerName,
    int RestaurantRating,
    int? DriverRating,
    string? Comment,
    DateTime CreatedAt);
