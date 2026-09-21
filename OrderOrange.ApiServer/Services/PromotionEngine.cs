using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>Ok, or a refusal as a localization key suffix (dc.e.*) with its placeholders.</summary>
public sealed record PromoVerdict(bool Ok, string? Code, string[] Args, decimal Discount, string Message);

/// <summary>
/// Decides whether one customer may use one store promotion on one order, and what it is
/// worth. Every rule that needs history reads the order book — a customer's past orders
/// at THIS store — so "first order" and "ten orders this month" are true facts, not
/// counters that can drift.
/// </summary>
public sealed class PromotionEngine(AppDbContext db)
{
    public async Task<PromoVerdict> EvaluateAsync(PromotionDoc p, int customerId, decimal subtotal, int itemCount, OrderType orderType, DateTime now)
    {
        if (!p.IsActive) return Fail("inactive", "This code is switched off.");
        if (p.StartsAt is DateTime s && now < s) return Fail("notStarted", $"This code starts on {s:yyyy-MM-dd}.", s.ToString("yyyy-MM-dd"));
        if (p.EndsAt is DateTime e && now > e) return Fail("expired", "This code has expired.");
        if (p.OrderTypes == "delivery" && orderType != OrderType.Delivery) return Fail("deliveryOnly", "This code is for delivery orders only.");
        if (p.OrderTypes == "pickup" && orderType != OrderType.Pickup) return Fail("pickupOnly", "This code is for collection orders only.");
        if (p.Days.Count > 0 && !p.Days.Contains((int)now.DayOfWeek)) return Fail("day", "This code is not valid today.");
        if (p.StartHour is int sh && p.EndHour is int eh && sh != eh)
        {
            var h = now.Hour;
            var inWindow = sh < eh ? (h >= sh && h < eh) : (h >= sh || h < eh);   // 22→02 wraps midnight
            if (!inWindow) return Fail("hours", $"This code works between {sh:00}:00 and {eh:00}:00.", $"{sh:00}:00", $"{eh:00}:00");
        }
        if (subtotal < p.MinOrder) return Fail("minOrder", $"This code needs a minimum order of {p.MinOrder:0.000} OMR.", p.MinOrder.ToString("0.000"));
        if (p.Rule == "min_items" && itemCount < p.RuleValue) return Fail("minItems", $"This code needs at least {p.RuleValue} items.", p.RuleValue.ToString());

        // The customer's history at this store — cancelled and rejected orders never count.
        var history = db.Orders.Where(o => o.RestaurantId == p.RestaurantId && o.CustomerId == customerId
                                           && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);
        switch (p.Rule)
        {
            case "first_order":
                if (customerId > 0 && await history.AnyAsync()) return Fail("firstOnly", "This code is for your first order here.");
                break;
            case "orders_month":
            {
                var monthStart = new DateTime(now.Year, now.Month, 1);
                var n = customerId > 0 ? await history.CountAsync(o => o.PlacedAt >= monthStart) : 0;
                if (n < p.RuleValue) return Fail("needOrdersMonth", $"This code unlocks after {p.RuleValue} orders this month — you have {n}.", p.RuleValue.ToString(), n.ToString());
                break;
            }
            case "orders_total":
            {
                var n = customerId > 0 ? await history.CountAsync() : 0;
                if (n < p.RuleValue) return Fail("needOrders", $"This code unlocks after {p.RuleValue} orders here — you have {n}.", p.RuleValue.ToString(), n.ToString());
                break;
            }
            case "spent_total":
            {
                var sum = customerId > 0 ? (await history.SumAsync(o => (decimal?)o.Total) ?? 0m) : 0m;
                if (sum < p.RuleAmount) return Fail("needSpent", $"This code unlocks after {p.RuleAmount:0.000} OMR of orders here.", p.RuleAmount.ToString("0.000"));
                break;
            }
        }

        // Limits — read from the orders that actually carried the code.
        var used = db.Orders.Where(o => o.RestaurantId == p.RestaurantId && o.CouponCode == p.Code
                                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);
        if (p.MaxUses > 0 && await used.CountAsync() >= p.MaxUses) return Fail("maxUses", "This code has been fully redeemed.");
        if (p.MaxUsesPerCustomer > 0 && customerId > 0 && await used.CountAsync(o => o.CustomerId == customerId) >= p.MaxUsesPerCustomer)
            return Fail("perCustomer", $"You have already used this code {p.MaxUsesPerCustomer} time(s).", p.MaxUsesPerCustomer.ToString());

        var discount = p.Type == "amount"
            ? Math.Min(p.Value, subtotal)
            : Math.Round(subtotal * p.Value / 100m, 3);
        if (p.Type == "percent" && p.MaxDiscount > 0) discount = Math.Min(discount, p.MaxDiscount);
        if (discount <= 0) return Fail("nothing", "This code gives nothing on this order.");

        var label = p.Type == "amount" ? $"{p.Value:0.###} OMR off" : $"{p.Value:0.#}% off";
        return new PromoVerdict(true, null, [], discount, $"{p.Title} — {label}, you save {discount:0.000} OMR.");
    }

    private static PromoVerdict Fail(string code, string message, params string[] args) => new(false, code, args, 0m, message);
}
