using System.Text.Json;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Keeps a store's rows in the inverted word index (RestaurantWords) in step with its
/// names. The bulk catalog was indexed once at generation time; hand-created stores
/// get (re)indexed here — at creation, on every rename, and by the startup sweep.
/// Every language's name goes in, so "امیران" finds the store as surely as "Amiran".
/// </summary>
public static class StoreWordIndex
{
    public static async Task ReindexAsync(AppDbContext db, Restaurant store)
    {
        var words = new HashSet<string>();

        void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var token in SmartSearch.Tokens(text))
                if (token.Length >= 2)
                    words.Add(token);
        }

        void AddJsonValues(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        Add(prop.Value.GetString());
            }
            catch { /* a malformed blob must never block saving the store */ }
        }

        Add(store.Name);
        Add(store.Area);
        AddJsonValues(store.NameLocalized);
        AddJsonValues(store.AddressLocalized);

        var stale = await db.RestaurantWords.Where(w => w.RestaurantId == store.Id).ToListAsync();
        db.RestaurantWords.RemoveRange(stale);
        foreach (var word in words)
            db.RestaurantWords.Add(new RestaurantWord { Word = word, RestaurantId = store.Id });
        await db.SaveChangesAsync();
    }
}
