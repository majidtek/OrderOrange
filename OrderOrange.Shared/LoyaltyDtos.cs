namespace OrderOrange.Shared;

/// <summary>
/// How a shop rewards its regulars. Points per unit of money spent, a reward when enough
/// points are gathered, and two tiers above the entry one, reached by lifetime points.
/// </summary>
/// <param name="PointsPerUnit">Points earned for every 1 of the shop's currency spent.</param>
/// <param name="RewardPoints">How many points a reward costs.</param>
/// <param name="RewardValue">What that reward is worth, in the shop's currency, off the bill.</param>
/// <param name="WelcomePoints">A gift on the first visit, so the card is never empty.</param>
/// <param name="SilverAt">Lifetime points that make a guest Silver.</param>
/// <param name="GoldAt">Lifetime points that make a guest Gold.</param>
public record LoyaltyProgramDto(
    bool Enabled,
    decimal PointsPerUnit,
    int RewardPoints,
    decimal RewardValue,
    int WelcomePoints,
    int SilverAt,
    int GoldAt);

public record SaveLoyaltyProgramRequest(
    bool Enabled,
    decimal PointsPerUnit,
    int RewardPoints,
    decimal RewardValue,
    int WelcomePoints,
    int SilverAt,
    int GoldAt);

/// <summary>One guest on the programme. Keyed by the customer account, so web and till orders meet.</summary>
public record LoyaltyMemberDto(
    int UserId,
    string Name,
    string Phone,
    int Balance,
    int Lifetime,
    int Redeemed,
    int Visits,
    string Tier,
    DateTime? JoinedAt,
    DateTime? LastAt,
    /// <summary>Points still needed for the next reward; 0 when one is ready.</summary>
    int ToNextReward,
    /// <summary>How many rewards the balance covers right now.</summary>
    int RewardsReady);

/// <summary>Something that changed a balance: earn, redeem, welcome or a manual adjustment.</summary>
public record LoyaltyEventDto(
    int Id,
    int UserId,
    string Name,
    string Kind,
    int Points,
    decimal? Amount,
    string? OrderNumber,
    string? Note,
    string By,
    DateTime At);

public record LoyaltyDayDto(DateTime Day, int Earned, int Redeemed);

/// <summary>The whole programme at a glance.</summary>
public record LoyaltyBoardDto(
    LoyaltyProgramDto Program,
    int Members,
    int ActiveMembers30d,
    int PointsOutstanding,
    decimal Liability,
    int RedeemedPoints,
    decimal RedeemedValue,
    int EarnedPoints30d,
    List<LoyaltyMemberDto> Top,
    List<LoyaltyEventDto> Recent,
    List<LoyaltyDayDto> Days,
    /// <summary>Guests a few points short of a reward: one visit away, worth a nudge.</summary>
    List<LoyaltyMemberDto>? AlmostThere = null,
    /// <summary>Regulars who have not been in for a month, most valuable first.</summary>
    List<LoyaltyMemberDto>? Sleeping = null,
    int Bronze = 0,
    int Silver = 0,
    int Gold = 0,
    string StoreName = "");

/// <summary>A visit rung up by hand: money spent becomes points by the programme's rate.</summary>
public record LoyaltyEarnRequest(int UserId, decimal Amount, string? Note = null);

/// <summary>Points leave the card for a reward. Zero means "one reward's worth".</summary>
public record LoyaltyRedeemRequest(int UserId, int Points = 0, string? Note = null);

/// <summary>A correction, plus or minus, with a reason.</summary>
public record LoyaltyAdjustRequest(int UserId, int Points, string Note);

public static class LoyaltyTiers
{
    public const string Bronze = "bronze";
    public const string Silver = "silver";
    public const string Gold = "gold";

    public static string For(int lifetime, int silverAt, int goldAt) =>
        goldAt > 0 && lifetime >= goldAt ? Gold :
        silverAt > 0 && lifetime >= silverAt ? Silver : Bronze;
}

public static class LoyaltyKinds
{
    public const string Earn = "earn";
    public const string Redeem = "redeem";
    public const string Welcome = "welcome";
    public const string Adjust = "adjust";
}

/// <summary>Someone on the shop's pages in the last few minutes. Signed-in guests carry their card.</summary>
/// <param name="Doing">menu · track · reserve · survey · table (sitting in the restaurant).</param>
/// <param name="Table">The table they are sitting at, when they are in the room.</param>
public record LiveGuestDto(string Who, string Doing, DateTime LastAt, int Views, int? UserId, int Balance, string Tier, bool Signed, string? Table = null);

/// <summary>An order that has not been paid yet, with the guest's points beside it.</summary>
public record OpenInvoiceDto(int OrderId, string Number, string Customer, string Phone, decimal Total, DateTime PlacedAt,
    string Status, string Kind, string? Table, int? UserId, int Balance, int RewardsReady);

/// <param name="ServerNow">The shop's clock, so ages are right even where the browser has no time zone.</param>
public record LoyaltyLiveDto(List<LiveGuestDto> Online, List<OpenInvoiceDto> Open, DateTime ServerNow);
