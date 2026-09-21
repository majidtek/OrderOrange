namespace OrderOrange.Shared;

/// <summary>One thing to cook, as the kitchen sees it.</summary>
/// <param name="Station">"kitchen", "bar", or null when the product was never routed.</param>
public record KdsLineDto(int Id, string Name, int Quantity, string? Notes, string? Station, bool Done);

/// <summary>
/// A kitchen ticket. Either a whole online order, or one round of food sent from a table —
/// the lines a waiter rang up together, which is what actually arrives at the pass as a job.
/// </summary>
/// <param name="Key">Stable id the kitchen acts on: "o:123" for an order, "t:45:637..." for a round.</param>
/// <param name="Source">"order" or "tab".</param>
/// <param name="Kind">"delivery", "pickup" or "dinein".</param>
/// <param name="State">"new" (not started), "cooking", or "done".</param>
public record KdsTicketDto(
    string Key,
    string Source,
    string Number,
    string Kind,
    string? Table,
    string? Customer,
    DateTime At,
    string State,
    List<KdsLineDto> Lines,
    string? Note,
    DateTime? ScheduledFor,
    DateTime? StartedAt,
    DateTime? DoneAt);

/// <summary>Everything one kitchen screen needs in a single call.</summary>
/// <param name="Stations">The stations this shop actually routes to, for the filter row.</param>
/// <param name="TargetMinutes">The shop's own prep promise — what "late" means here.</param>
public record KdsBoardDto(List<KdsTicketDto> Tickets, List<string> Stations, int TargetMinutes, DateTime ServerNow);

/// <summary>Which ticket the kitchen just tapped.</summary>
public record KdsActionRequest(string Key);

/// <summary>Which line on which ticket.</summary>
public record KdsLineActionRequest(string Key, int LineId);
