namespace LocalHandler.Models;

// LineChange and TicketStrings moved to OrderOrange.Shared/KitchenTicket.cs (2026-10-13) so the
// partner designer previews the kitchen ticket with the very code the till prints it with.

/// <summary>What the kitchen was last told about an open-invoice line: which tab it sits
/// on, how many, and its name/price (kept so a struck line can still be printed).</summary>
public sealed record TabLineMemo(int TabId, int Qty, string Name, decimal UnitPrice);