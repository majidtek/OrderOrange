namespace OrderOrange.Shared;

public record ChatMessageDto(
    int Id,
    int OrderId,
    int SenderUserId,
    string SenderName,
    UserRole SenderRole,
    string? Text,
    string? AttachmentData,
    string? AttachmentType,
    string? FileName,
    DateTime At);

/// <summary>
/// AttachmentType: "image", "file", "voice" (data-URL in AttachmentData) or
/// "location" (Text = "lat,lng", no attachment).
/// </summary>
public record SendChatRequest(string? Text, string? AttachmentData, string? AttachmentType, string? FileName);

/// <summary>One order conversation as the admin chat monitor sees it.</summary>
public record AdminChatSummaryDto(
    int OrderId,
    string OrderNumber,
    OrderStatus OrderStatus,
    string RestaurantName,
    string CustomerName,
    string? DriverName,
    int MessageCount,
    string LastSender,
    string? LastText,
    string? LastType,
    DateTime LastAt);
