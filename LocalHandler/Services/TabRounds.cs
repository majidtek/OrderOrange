using LocalHandler.Models;
using OrderOrange.Shared;

namespace LocalHandler.Services;

/// <summary>
/// Compares the open invoices with what the kitchen has already been told and says what
/// to print: whole new lines, and for an invoice the kitchen already has, every change —
/// a line that grew (1 → 3), shrank (3 → 1) or was struck (5 → 0). Pure logic, no printer,
/// so it can be exercised without a till.
/// </summary>
public static class TabRounds
{
    public sealed record Round(StoreTabDto Tab, List<OrderItemDto> Items, Dictionary<int, LineChange> Changes, bool Edited);

    /// <param name="seen">Line ids the kitchen has had at least once (the caller adds the printed ones afterwards).</param>
    /// <param name="memos">Line id → last told quantity; updated in place to the new truth.</param>
    public static List<Round> Diff(IReadOnlyList<StoreTabDto> tabs, IReadOnlySet<int> seen,
        Dictionary<int, TabLineMemo> memos, out bool memosChanged)
    {
        memosChanged = false;
        var rounds = new List<Round>();
        var openIds = tabs.Select(t => t.Id).ToHashSet();

        foreach (var tab in tabs)
        {
            // The kitchen already has this invoice → whatever follows is an EDIT of it.
            var known = tab.Lines.Any(l => seen.Contains(l.Id)) || memos.Values.Any(m => m.TabId == tab.Id);
            var items = new List<OrderItemDto>();
            var changes = new Dictionary<int, LineChange>();

            foreach (var l in tab.Lines)
            {
                if (!seen.Contains(l.Id))
                {
                    items.Add(new OrderItemDto(l.Id, l.Name, l.UnitPrice, l.Quantity, l.Notes));
                    if (known) changes[l.Id] = new LineChange(0, l.Quantity);
                }
                else if (memos.TryGetValue(l.Id, out var memo) && memo.Qty != l.Quantity)
                {
                    items.Add(new OrderItemDto(l.Id, l.Name, l.UnitPrice, l.Quantity, l.Notes));
                    changes[l.Id] = new LineChange(memo.Qty, l.Quantity);
                }
            }

            // Lines struck from an invoice that is still open → "5 → 0".
            var present = tab.Lines.Select(l => l.Id).ToHashSet();
            foreach (var (lid, memo) in memos.Where(kv => kv.Value.TabId == tab.Id && !present.Contains(kv.Key)).ToList())
            {
                items.Add(new OrderItemDto(lid, memo.Name, memo.UnitPrice, 0, null));
                changes[lid] = new LineChange(memo.Qty, 0);
                memos.Remove(lid);
                memosChanged = true;
            }

            foreach (var l in tab.Lines)
            {
                if (!memos.TryGetValue(l.Id, out var was) || was.Qty != l.Quantity || was.TabId != tab.Id)
                {
                    memos[l.Id] = new TabLineMemo(tab.Id, l.Quantity, l.Name, l.UnitPrice);
                    memosChanged = true;
                }
            }

            if (items.Count > 0) rounds.Add(new Round(tab, items, changes, known));
        }

        // A closed invoice takes its memos with it — nothing to strike, the table is done.
        foreach (var lid in memos.Where(kv => !openIds.Contains(kv.Value.TabId)).Select(kv => kv.Key).ToList())
        {
            memos.Remove(lid);
            memosChanged = true;
        }
        return rounds;
    }
}
