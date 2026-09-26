using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class RestaurantsController(AppDbContext db, CatalogStore catalog, IConfiguration config,
    AdminAlerts alerts, StorePrefsStore prefs) : ApiControllerBase
{
    /// <summary>
    /// The shops the platform is putting forward, in their running order. Lives on the
    /// Restaurants row itself (SuggestedOrder, null = not featured) so the admin panel
    /// switches shops in and out live — this replaced the Suggested:RestaurantIds
    /// appsettings list, which needed an API restart per change. Suggested:Max still
    /// caps the strip.
    /// </summary>
    private Task<List<int>> SuggestedIdsAsync() =>
        db.Restaurants
            .Where(r => r.SuggestedOrder != null && r.IsApproved)
            .OrderBy(r => r.SuggestedOrder).ThenBy(r => r.Id)
            .Select(r => r.Id)
            .Take(Math.Clamp(config.GetValue("Suggested:Max", 24), 1, 60))
            .ToListAsync();

    // ---------- Customer: browse ----------

    [HttpGet]
    [AllowAnonymous]
    public async Task<List<RestaurantCardDto>> Browse(string? search = null, int? cuisineId = null,
        string? cuisineIds = null, StoreType? storeType = null, string? sort = null,
        bool favoritesOnly = false, int skip = 0, int take = 30, double? lat = null, double? lng = null)
    {
        // With millions of stores every page is served straight from the database.
        take = Math.Clamp(take, 1, 60);
        skip = Math.Max(0, skip);

        var query = db.Restaurants.Include(r => r.Cuisine).Where(r => r.IsApproved);
        // A store matches a cuisine filter on ANY of its types — the Iranian-and-Arabic
        // grill belongs under both circles, or its second type means nothing.
        if (cuisineId is not null)
            query = query.Where(r => r.CuisineId == cuisineId
                                     || r.ExtraCuisines.Any(rc => rc.CuisineId == cuisineId));
        if (!string.IsNullOrWhiteSpace(cuisineIds))
        {
            var wanted = cuisineIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
            if (wanted.Count > 0)
                query = query.Where(r => wanted.Contains(r.CuisineId)
                                         || r.ExtraCuisines.Any(rc => wanted.Contains(rc.CuisineId)));
        }
        if (storeType is not null)
            query = query.Where(r => r.StoreType == storeType);
        if (favoritesOnly)
        {
            var uid = CurrentUserId;
            query = query.Where(r => db.Favorites.Any(f => f.UserId == uid && f.RestaurantId == r.Id));
        }

        // Shops the platform is putting forward open page one. They come out of the
        // ordinary ordering here and go back at the front once the page is built, so
        // scrolling on never meets the same shop a second time.
        var featured = await FeaturedForBrowseAsync(search, cuisineId, cuisineIds, storeType, sort, favoritesOnly);
        if (featured.Count > 0)
        {
            var featuredIds = featured.Select(r => r.Id).ToList();
            query = query.Where(r => !featuredIds.Contains(r.Id));
        }

        List<Models.Restaurant> restaurants;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same smart engine as /search: full query understanding first, then
            // indexed candidate lookups, then in-memory ranking.
            var vocab = await SearchVocabCache.GetAsync(db);
            var (analyzedTokens, _) = SmartSearch.Analyze(search.Trim(), vocab.AllWords);
            var terms = analyzedTokens.ToList();
            foreach (var alias in SearchAliases.Expand(search.Trim()).Skip(1))
                terms.AddRange(SmartSearch.Tokens(alias));
            var queryTokens = terms.Where(t => t.Length >= 2).Distinct().ToArray();

            var found = new Dictionary<int, Models.Restaurant>();
            var dishMatched = new HashSet<int>();
            var useWordIndex = await db.RestaurantWords.AnyAsync();
            foreach (var term in queryTokens.Take(8))
            {
                if (vocab.RestaurantWords.Contains(term))
                {
                    List<Models.Restaurant> hits;
                    if (useWordIndex)
                    {
                        var wordIds = await db.RestaurantWords.Where(w => w.Word == term)
                            .Select(w => w.RestaurantId).Take(150).ToListAsync();
                        hits = wordIds.Count == 0 ? [] : await query.Where(r => wordIds.Contains(r.Id)).ToListAsync();
                    }
                    else
                    {
                        hits = await query.Where(r =>
                                r.Name.ToLower().Contains(term) || r.Area.ToLower().Contains(term) ||
                                r.Cuisine.Name.ToLower().Contains(term))
                            .Take(150).ToListAsync();
                    }
                    foreach (var hit in hits) found.TryAdd(hit.Id, hit);
                }

                // Dish names live in the small menu table — match there first, then
                // pull those stores in, instead of joining menus across millions of rows.
                var byDish = await db.MenuItems
                    .Where(i => i.Name.ToLower().Contains(term) || i.SearchKeywords.Contains(term))
                    .Select(i => i.RestaurantId).Distinct().Take(50).ToListAsync();
                if (byDish.Count > 0)
                {
                    foreach (var hit in await query.Where(r => byDish.Contains(r.Id)).ToListAsync())
                    {
                        found.TryAdd(hit.Id, hit);
                        dishMatched.Add(hit.Id);
                    }
                }
            }
            var needle = SmartSearch.Normalize(search.Trim());
            restaurants = found.Values
                .Select(r => (r, score: Math.Max(
                    // Localized names score too — "امیران" must rate as highly as "Amiran",
                    // and a store truly named this outranks one that merely mentions it.
                    SmartSearch.Score($"{r.Name} {r.Cuisine.Name} {r.Area} {r.NameLocalized}", queryTokens)
                        + NameMatchBonus(r, needle),
                    dishMatched.Contains(r.Id) ? 75 : 0)))
                .Where(x => x.score >= 45)
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.r.IsOpen)
                .GroupBy(x => ChainKey(x.r.Name))
                .SelectMany(g => g.Take(2))            // no chain may swallow the page
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.r.IsOpen)
                .Skip(skip).Take(take)
                .Select(x => x.r).ToList();
        }
        else if (sort == "rating")
        {
            // Rated stores are a small set — surface them first, then fill with the rest.
            var ratedIds = await db.Reviews.GroupBy(v => v.RestaurantId)
                .Select(g => new { Id = g.Key, Avg = g.Average(x => (double)x.RestaurantRating) })
                .OrderByDescending(x => x.Avg)
                .Select(x => x.Id)
                .ToListAsync();
            var rank = ratedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            var head = (await query.Where(r => ratedIds.Contains(r.Id)).ToListAsync())
                .OrderByDescending(r => r.IsOpen).ThenBy(r => rank[r.Id]).ToList();

            restaurants = head.Skip(skip).Take(take).ToList();
            if (restaurants.Count < take)
            {
                var tailSkip = Math.Max(0, skip - head.Count);
                restaurants.AddRange(await query.Where(r => !ratedIds.Contains(r.Id))
                    .OrderByDescending(r => r.IsOpen).ThenBy(r => r.Name).ThenBy(r => r.Id)
                    .Skip(tailSkip).Take(take - restaurants.Count).ToListAsync());
            }
        }
        else if (lat is not null && lng is not null && sort is null or "recommended" or "near")
        {
            // "Near you": expanding bounding box (indexable range), sorted on the
            // narrow geo index only (ids + coords), then the page rows fetched by id —
            // sorting full rows across a city-sized box took tens of seconds.
            restaurants = [];
            foreach (var delta in new[] { 0.05, 0.15, 0.6, 2.5, 10.0, 90.0 })
            {
                var la = lat.Value; var ln = lng.Value; var d = delta;
                var boxed = query.Where(r => r.Lat != null && r.Lng != null &&
                    r.Lat >= la - d && r.Lat <= la + d && r.Lng >= ln - d && r.Lng <= ln + d);
                if (delta < 90.0 && await boxed.CountAsync() < skip + take) continue;

                var pageIds = await boxed
                    .OrderBy(r => (r.Lat!.Value - la) * (r.Lat.Value - la) + (r.Lng!.Value - ln) * (r.Lng.Value - ln))
                    .ThenBy(r => r.Id)
                    .Skip(skip).Take(take)
                    .Select(r => r.Id)
                    .ToListAsync();
                var rank = pageIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
                restaurants = (await db.Restaurants.Include(r => r.Cuisine)
                        .Where(r => pageIds.Contains(r.Id)).ToListAsync())
                    .OrderBy(r => rank[r.Id]).ToList();
                break;
            }
        }
        else
        {
            // The browse indexes lead with (IsApproved, StoreType, …) — without a
            // storeType filter these orderings would force a full 5M-row sort.
            query = sort switch
            {
                "fastest" when storeType is not null => query.OrderByDescending(r => r.IsOpen).ThenBy(r => r.AvgPrepMinutes).ThenBy(r => r.Id),
                "fee" when storeType is not null => query.OrderByDescending(r => r.IsOpen).ThenBy(r => r.DeliveryFee).ThenBy(r => r.Id),
                _ when storeType is not null => query.OrderByDescending(r => r.IsOpen).ThenBy(r => r.Name).ThenBy(r => r.Id),
                _ => query.OrderBy(r => r.Id)
            };
            restaurants = await query.Skip(skip).Take(take).ToListAsync();
        }

        // Page one opens with them; every later page never sees them, because they were
        // filtered out of the query above. Trimmed back to `take` so the client's
        // "a full page means there is more" test still holds.
        if (featured.Count > 0 && skip == 0)
            restaurants = featured.Concat(restaurants).Take(take).ToList();

        var ids = restaurants.Select(r => r.Id).ToList();
        var ratings = await RatingsFor(ids);
        var hours = await HoursFor(ids);
        var extraCuisines = await ExtraCuisinesFor(ids);
        var photoStores = await PhotoStoresAsync();
        return restaurants.Select(r => Card(r, ratings, hours, lat, lng, extraCuisines, photoStores)).ToList();
    }

    /// <summary>
    /// The configured shops, when they are allowed to lead the browse list — otherwise
    /// empty. Only the plain "recommended" view gets them: "fastest", "cheapest" and
    /// "top rated" each promise an order, and quietly jumping two shops to the front of
    /// one would be a lie. A search, a cuisine filter or the favourites view is the
    /// customer asking for something specific, so those are left alone too.
    /// </summary>
    private async Task<List<Models.Restaurant>> FeaturedForBrowseAsync(
        string? search, int? cuisineId, string? cuisineIds,
        StoreType? storeType, string? sort, bool favoritesOnly)
    {
        if (!string.IsNullOrWhiteSpace(search) || favoritesOnly) return [];
        if (cuisineId is not null || !string.IsNullOrWhiteSpace(cuisineIds)) return [];
        if (sort is not (null or "recommended")) return [];

        var wanted = await SuggestedIdsAsync();
        if (wanted.Count == 0) return [];

        var featured = await db.Restaurants.Include(r => r.Cuisine)
            .Where(r => wanted.Contains(r.Id) && r.IsApproved
                        && (storeType == null || r.StoreType == storeType))
            .ToListAsync();

        var rank = wanted.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return featured.OrderBy(r => rank[r.Id]).ToList();
    }

    /// <summary>
    /// The stores worth putting in front of a search engine: the real, hand-onboarded
    /// shops, and nothing from the generated catalog.
    ///
    /// Of the ~4,900 rows in this table all but a handful are seeded or generated —
    /// thin, near-identical pages. Submitting them would spend Google's crawl budget on
    /// filler and drag the whole domain's quality down, so the sitemap lists only shops
    /// created above <c>Seo:RealStoreMinId</c>. Every genuinely onboarded store lands
    /// above that mark, so newly added ones appear without anybody editing a list.
    /// </summary>
    [HttpGet("sitemap")]
    [AllowAnonymous]
    public async Task<List<SitemapStoreDto>> Sitemap()
    {
        var floor = config.GetValue("Seo:RealStoreMinId", 5_000_200);
        var minItems = config.GetValue("Seo:SitemapMinMenuItems", 3);

        // Approved AND actually has something to order. The approval flag alone was not
        // enough: a half-onboarded shop with no menu was approved behind our back and
        // went straight into the sitemap. Offering Google a page with nothing on it
        // wastes a crawl and earns the domain a thin-content mark. Having dishes is a
        // fact about the shop that cannot drift.
        //
        // "Any dish" turned out to be too low a bar: registering a shop auto-creates a
        // single Water item, so a shop that was never actually onboarded still cleared it
        // — Tea Time (5000219) reached the sitemap on one 0.100 Water and nothing else.
        // A real menu is several dishes, so that is what is required.
        return await db.Restaurants
            .AsNoTracking()
            .Where(r => r.Id >= floor && r.IsApproved
                        && db.MenuItems.Count(m => m.RestaurantId == r.Id) >= minItems)
            .OrderBy(r => r.Id)
            .Select(r => new SitemapStoreDto(r.Id, r.Name, r.Cuisine.Name, r.Area, r.Slug))
            .ToListAsync();
    }

    /// <summary>Cards for specific ids — powers the "order again" row without loading the catalog.</summary>
    [HttpGet("by-ids")]
    [AllowAnonymous]
    public async Task<List<RestaurantCardDto>> ByIds(string ids = "")
    {
        var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().Take(20).ToList();
        if (wanted.Count == 0) return [];

        var restaurants = await db.Restaurants.Include(r => r.Cuisine)
            .Where(r => wanted.Contains(r.Id) && r.IsApproved).ToListAsync();
        var ratings = await RatingsFor(wanted);
        var hours = await HoursFor(wanted);
        var extraCuisines = await ExtraCuisinesFor(wanted);
        var photoStores = await PhotoStoresAsync();
        return restaurants.Select(r => Card(r, ratings, hours, null, null, extraCuisines, photoStores)).ToList();
    }

    /// <summary>
    /// The shops the platform is putting forward, with a few of their dishes — the
    /// customer home "Suggested" strip. Which shops appear — and in what order — is set
    /// from the admin panel and read off the Restaurants rows, live.
    /// </summary>
    [HttpGet("suggested")]
    [AllowAnonymous]
    public async Task<SuggestionsDto> Suggested(int take = 12)
    {
        take = Math.Clamp(take, 1, 30);

        var wanted = await SuggestedIdsAsync();
        if (wanted.Count == 0) return new SuggestionsDto([], []);

        // A shop that has closed its doors or lost its approval must not be advertised.
        var stores = await db.Restaurants.Include(r => r.Cuisine)
            .Where(r => wanted.Contains(r.Id) && r.IsApproved).ToListAsync();
        if (stores.Count == 0) return new SuggestionsDto([], []);

        var ids = stores.Select(r => r.Id).ToList();
        var ratings = await RatingsFor(ids);
        var hours = await HoursFor(ids);

        // Configured order is the running order — the first id listed leads the strip.
        var rank = wanted.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var extraCuisines = await ExtraCuisinesFor(ids);
        var photoStores = await PhotoStoresAsync();
        var cards = stores.OrderBy(r => rank[r.Id]).Select(r => Card(r, ratings, hours, null, null, extraCuisines, photoStores)).ToList();

        // An even spread rather than one shop's whole menu: the point is to introduce
        // both kitchens, so each contributes the same number of plates.
        var perStore = Math.Max(1, (int)Math.Ceiling(take / (double)stores.Count));
        var dishes = new List<DealDto>();
        var byStore = new List<List<DealDto>>();
        foreach (var store in stores.OrderBy(r => rank[r.Id]))
        {
            var menu = await catalog.PublicMenuAsync(store.Id);
            // A dish with a photo sells the shop, popular ones next — and after that the
            // menu's own running order, which leads with the main dishes. Sorting on price
            // instead put water and pickles at the front of the strip.
            var picks = menu.SelectMany(c => c.Items)
                .Where(i => i.IsAvailable)
                .Select((item, order) => (item, order))
                .OrderByDescending(x => x.item.Photo is { Length: > 0 })
                .ThenByDescending(x => x.item.IsPopular)
                .ThenBy(x => x.order)
                .Take(perStore)
                .Select(x => x.item);

            byStore.Add(picks.Select(i => new DealDto(
                i.Id, i.Name, i.ImageEmoji, MediaLinks.Dish(config, i.Id, i.Photo), i.Price, i.DiscountPercent,
                store.Id, store.Name, store.LogoEmoji, store.Cuisine.Name, store.Area, store.IsOpen)).ToList());
        }

        // Round-robin, so the cap below trims SECOND picks rather than whole shops. Taking
        // the list in shop order meant the first few shops filled the strip and everything
        // featured after them contributed nothing at all — which is exactly where a newly
        // added shop sits.
        for (var round = 0; round < perStore; round++)
            foreach (var list in byStore)
                if (round < list.Count) dishes.Add(list[round]);

        return new SuggestionsDto(cards, dishes.Take(Math.Max(take, byStore.Count)).ToList());
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<RestaurantDetailDto>> Detail(int id)
    {
        // No Categories Include here: the menu served below comes from MongoDB, and the
        // SQL mirror rows (photos included) were being dragged out of the database on
        // every store view only to be thrown away.
        var restaurant = await db.Restaurants
            .Include(r => r.Cuisine)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsApproved);
        if (restaurant is null) return NotFound();

        var ratings = await RatingsFor([id]);
        var hours = await HoursFor([id]);

        // The menu comes from MongoDB and only ever contains approved products — a
        // partner's newly submitted item stays invisible here until an admin clears it.
        var categories = await catalog.PublicMenuAsync(id);

        // Bulk stores have no stored rows — serve the big template menu instead.
        if (categories.Count == 0)
            categories = MenuTemplates.BuildMenu(restaurant.Id, restaurant.StoreType, restaurant.Cuisine.Name);

        categories = categories.Select(c => MediaLinks.Lighten(config, c)).ToList();
        return new RestaurantDetailDto(Card(restaurant, ratings, hours, photoStores: await PhotoStoresAsync()), categories);
    }

    // ---------- Owner: weekly working hours ----------

    [HttpGet("mine/hours")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<List<DayHoursDto>> MyHours()
    {
        var rows = await db.RestaurantHours.Where(h => h.RestaurantId == CurrentRestaurantId).ToListAsync();
        return Enumerable.Range(0, 7).Select(day =>
        {
            var row = rows.FirstOrDefault(h => h.Day == day);
            return row is null
                ? new DayHoursDto(day, false, "10:00", "23:00")
                : new DayHoursDto(day, row.IsClosed, row.Open.ToString(@"hh\:mm"), row.Close.ToString(@"hh\:mm"));
        }).ToList();
    }

    [HttpPut("mine/hours")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SaveHours(List<DayHoursDto> days)
    {
        if (days is not { Count: 7 }) return BadRequest(new { message = "All seven days are required." });

        var rows = await db.RestaurantHours.Where(h => h.RestaurantId == CurrentRestaurantId).ToListAsync();
        foreach (var day in days)
        {
            if (day.Day is < 0 or > 6) return BadRequest(new { message = "Invalid day." });
            if (!TimeSpan.TryParse(day.Open, out var open) || !TimeSpan.TryParse(day.Close, out var close))
                return BadRequest(new { message = "Times must be HH:mm." });

            var row = rows.FirstOrDefault(h => h.Day == day.Day);
            if (row is null)
            {
                row = new Models.RestaurantHours { RestaurantId = CurrentRestaurantId, Day = day.Day };
                db.RestaurantHours.Add(row);
            }
            row.IsClosed = day.IsClosed;
            row.Open = open;
            row.Close = close;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/reviews")]
    [AllowAnonymous]
    public async Task<List<ReviewDto>> Reviews(int id) =>
        await db.Reviews.Where(r => r.RestaurantId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Join(db.Users, r => r.CustomerId, u => u.Id, (r, u) =>
                new ReviewDto(r.Id, u.FullName, r.RestaurantRating, r.DriverRating, r.Comment, r.CreatedAt))
            .ToListAsync();

    /// <summary>
    /// Dishes currently on offer, best discount first — the customer home "Offers" strip.
    /// Capped and paged in SQL: with millions of stores this must never scan a whole menu table.
    /// </summary>
    [HttpGet("deals")]
    [AllowAnonymous]
    public async Task<List<DealDto>> Deals(int take = 12)
    {
        take = Math.Clamp(take, 1, 30);

        // Approved discounted items come from MongoDB; the stores that own them are
        // still resolved in SQL until the browse path moves across too.
        var discounted = await catalog.DealsAsync(take * 6);
        var storeIds = discounted.Select(d => d.RestaurantId).Distinct().ToList();
        var stores = await db.Restaurants.Where(r => storeIds.Contains(r.Id) && r.IsApproved)
            .Select(r => new { r.Id, r.Name, r.LogoEmoji, r.Area, r.IsOpen, Cuisine = r.Cuisine!.Name })
            .ToDictionaryAsync(r => r.Id);

        // Ranked by money saved, not by percent: 20% off a 9.900 bouquet beats 35% off a
        // 0.800 side, and it keeps the strip from showing twelve identical "-35%" ribbons.
        var rows = discounted
            .Where(m => stores.ContainsKey(m.RestaurantId) && ShowOnHome(m.Price, m.DiscountPercent))
            .OrderByDescending(m => m.Price * m.DiscountPercent).ThenBy(m => m.Id)
            .Select(m =>
            {
                var r = stores[m.RestaurantId];
                return new
                {
                    m.Id, m.Name, m.ImageEmoji, m.PhotoData, m.Price, m.DiscountPercent,
                    RestaurantId = r.Id, RestaurantName = r.Name, r.LogoEmoji, r.Area, r.IsOpen,
                    Cuisine = r.Cuisine
                };
            })
            .ToList();

        // At most two offers from the same store so the strip shows variety.
        return rows
            .GroupBy(x => x.RestaurantId)
            .SelectMany(g => g.Take(2))
            .OrderByDescending(x => x.Price * x.DiscountPercent).ThenBy(x => x.Id)
            .Take(take)
            .Select(x => new DealDto(
                x.Id, x.Name, x.ImageEmoji, MediaLinks.Dish(config, x.Id, x.PhotoData), x.Price, x.DiscountPercent,
                x.RestaurantId, x.RestaurantName, x.LogoEmoji, x.Cuisine, x.Area, x.IsOpen))
            .ToList();
    }

    /// <summary>
    /// Every available dish across every approved store, paged. The customer home shows the
    /// shop grid first and then keeps going into dishes once the shops run out, so this is
    /// the tail of that list rather than a strip of its own.
    /// </summary>
    [HttpGet("items")]
    [AllowAnonymous]
    public async Task<List<DealDto>> Items(int skip = 0, int take = 24)
    {
        take = Math.Clamp(take, 1, 60);
        skip = Math.Max(0, skip);

        var docs = await catalog.AllItemsAsync(skip, take, HomeMinPrice);
        if (docs.Count == 0) return [];

        // The items live in Mongo, the stores that own them in SQL. An item whose store is
        // gone or unapproved is dropped rather than shown with a blank shop name.
        var storeIds = docs.Select(d => d.RestaurantId).Distinct().ToList();
        var stores = await db.Restaurants
            .Where(r => storeIds.Contains(r.Id) && r.IsApproved)
            .Select(r => new { r.Id, r.Name, r.LogoEmoji, r.Area, r.IsOpen, Cuisine = r.Cuisine!.Name })
            .ToDictionaryAsync(r => r.Id);

        return docs
            .Where(d => stores.ContainsKey(d.RestaurantId) && ShowOnHome(d.Price, d.DiscountPercent))
            .Select(d =>
            {
                var s = stores[d.RestaurantId];
                return new DealDto(d.Id, d.Name, d.ImageEmoji, MediaLinks.Dish(config, d.Id, d.PhotoData),
                                   d.Price, d.DiscountPercent,
                                   s.Id, s.Name, s.LogoEmoji, s.Cuisine, s.Area, s.IsOpen);
            })
            .ToList();
    }

    /// <summary>
    /// The customer home shows no dish cheaper than <c>Home:MinItemPrice</c> (0.500 OMR by
    /// default): the auto-created Water, bags, sauces and other odds and ends are real
    /// products in the shop's menu but not a reason to open the app. Store pages still list them.
    /// </summary>
    private decimal HomeMinPrice => config.GetValue("Home:MinItemPrice", 0.5m);
    // Judged on the list price so the Mongo page filter and this one agree — a discount
    // never hides an item the page already counted.
    private bool ShowOnHome(decimal price, decimal discountPercent) => price >= HomeMinPrice;

    /// <summary>Smart search: restaurants AND individual dishes in one call.</summary>
    /// <summary>
    /// The id above which a store is a REAL, hand-onboarded shop rather than part of the
    /// generated demo catalog. One number, shared with the sitemap, so "real" can never
    /// come to mean two different things in two places.
    /// </summary>
    private int RealStoreFloor => config.GetValue("Seo:RealStoreMinId", 5_000_200);

    /// <param name="realOnly">
    /// Offer only real kitchens. The ordering assistant sets this: sending a customer to
    /// a generated demo shop means promising food nobody can cook. Browse and the search
    /// box leave it off, so the catalog still fills the app.
    /// </param>
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<SearchResultsDto> Search(string query = "", bool realOnly = false)
    {
        var s = (query ?? "").Trim();
        if (s.Length < 2) return new SearchResultsDto([], []);

        // A real shop is one created above the floor that has a menu — the same test the
        // sitemap applies, so a shop the bot will offer is exactly a shop Google is told about.
        var floor = realOnly ? RealStoreFloor : int.MinValue;

        // Understand the query FIRST: typos, Arabizi digits, wrong keyboard layout,
        // merged/split words and partial (as-you-type) prefixes.
        var vocab = await SearchVocabCache.GetAsync(db);
        var (analyzedTokens, didYouMean) = SmartSearch.Analyze(s, vocab.AllWords);

        // Query tokens = the analyzed words + multilingual equivalents.
        var tokenList = analyzedTokens.ToList();
        foreach (var alias in SearchAliases.Expand(s).Skip(1))
            tokenList.AddRange(SmartSearch.Tokens(alias));
        var queryTokens = tokenList.Where(t => t.Length >= 2).Distinct().ToArray();

        // Candidates come from capped SQL lookups — never the whole catalog.
        var candidates = new Dictionary<int, Models.Restaurant>();
        var matchedCuisineIds = (await db.Cuisines.ToListAsync())
            .Where(c => queryTokens.Any(t => SmartSearch.Normalize(c.Name).Contains(t)))
            .Select(c => c.Id).ToList();
        if (matchedCuisineIds.Count > 0)
            foreach (var hit in await db.Restaurants.Include(r => r.Cuisine)
                         .Where(r => r.IsApproved && r.Id >= floor &&
                                     (matchedCuisineIds.Contains(r.CuisineId)
                                      || r.ExtraCuisines.Any(rc => matchedCuisineIds.Contains(rc.CuisineId))))
                         .Take(150).ToListAsync())
                candidates.TryAdd(hit.Id, hit);
        // Candidate stores come from the inverted word index (seek, never a scan);
        // small/fresh databases without the index fall back to plain LIKE.
        var useWordIndex = await db.RestaurantWords.AnyAsync();
        foreach (var term in queryTokens.Take(8))
        {
            // A word no store name contains can't match one — skip typos and
            // dish-only/keyword-only words entirely.
            if (!vocab.RestaurantWords.Contains(term)) continue;
            List<Models.Restaurant> hits;
            if (useWordIndex)
            {
                var wordIds = await db.RestaurantWords.Where(w => w.Word == term)
                    .Select(w => w.RestaurantId).Take(200).ToListAsync();
                hits = wordIds.Count == 0
                    ? []
                    : await db.Restaurants.Include(r => r.Cuisine)
                        .Where(r => r.IsApproved && r.Id >= floor && wordIds.Contains(r.Id)).ToListAsync();
            }
            else
            {
                hits = await db.Restaurants.Include(r => r.Cuisine)
                    .Where(r => r.IsApproved && r.Id >= floor
                                && (r.Name.ToLower().Contains(term) || r.Area.ToLower().Contains(term)))
                    .Take(200).ToListAsync();
            }
            foreach (var hit in hits) candidates.TryAdd(hit.Id, hit);
        }

        var allDishes = await db.MenuItems
            .Where(i => i.IsAvailable && i.Restaurant.IsApproved && i.RestaurantId >= floor)
            .Select(i => new
            {
                Dto = new DishHitDto(i.Id, i.Name, i.Description, i.ImageEmoji, i.Price,
                    i.RestaurantId, i.Restaurant.Name, i.Restaurant.LogoEmoji, i.Restaurant.IsOpen,
                    i.Restaurant.StoreType),
                i.IsPopular,
                i.SearchKeywords
            })
            .ToListAsync();

        // A shop actually CALLED what was typed must beat a generated chain that merely
        // shares a word — and one chain must not swallow every slot, or the single real
        // "Saffron Catering" never surfaces behind eight "Saffron Shawarma House"es.
        var needle = SmartSearch.Normalize(s);
        var rankedRestaurants = candidates.Values
            .Select(r => (Restaurant: r, Score: SmartSearch.Score(
                    $"{r.Name} {r.Cuisine.Name} {r.Area} {r.Description} {r.NameLocalized}", queryTokens)
                + NameMatchBonus(r, needle)))
            .Where(x => x.Score >= 45)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Restaurant.IsOpen)
            .GroupBy(x => ChainKey(x.Restaurant.Name))
            .SelectMany(g => g.Take(2))            // at most two of any one chain
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Restaurant.IsOpen)
            .Take(8)
            .ToList();

        var rankedDishes = allDishes
            .Select(d => (d.Dto, d.IsPopular, Score: SmartSearch.Score(
                $"{d.Dto.Name} {d.SearchKeywords} {d.Dto.Description} {d.Dto.RestaurantName}", queryTokens)))
            .Where(x => x.Score >= 45)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.IsPopular)
            .Take(12)
            .ToList();

        var ids = rankedRestaurants.Select(x => x.Restaurant.Id).ToList();
        var ratings = await RatingsFor(ids);
        var hours = await HoursFor(ids);
        var extraCuisines = await ExtraCuisinesFor(ids);
        var photoStores = await PhotoStoresAsync();
        var cards = rankedRestaurants.Select(x => Card(x.Restaurant, ratings, hours, null, null, extraCuisines, photoStores)).ToList();

        // Virtual catalog: the vertical template menus (every bulk market/pharmacy/
        // flower/shop store shares them) compete on SCORE with real products, so a
        // perfect virtual hit ("makeup", "خلاط") beats a weak real one.
        var scored = rankedDishes.Select(x => (x.Dto, x.Score)).ToList();
        // These belong to the generated vertical stores, so they have no place in an
        // answer that promised real kitchens only.
        var virtualHits = realOnly ? [] : MenuTemplates.VerticalItems
            .Select(v => (v, Score: SmartSearch.Score($"{v.Item.Name} {v.Keywords} {v.Category}", queryTokens)))
            .Where(x => x.Score >= 45)
            .OrderByDescending(x => x.Score)
            .Take(12)
            .ToList();
        foreach (var group in virtualHits.GroupBy(x => x.v.StoreType))
        {
            var store = await db.Restaurants
                .Where(r => r.IsApproved && r.StoreType == group.Key && r.IsOpen)
                .OrderBy(r => r.Id).FirstOrDefaultAsync();
            if (store is null) continue;
            foreach (var (v, score) in group)
                scored.Add((new DishHitDto(
                    MenuTemplates.VirtualId(store.Id, v.Slot), v.Item.Name,
                    $"{v.Category} — available at {store.Name} and every {group.Key} store.",
                    v.Item.Emoji, MenuTemplates.PriceFor(store.Id, v.Slot, v.Item.Price),
                    store.Id, store.Name, store.LogoEmoji, true, group.Key), score));
        }
        var dishResults = scored.OrderByDescending(x => x.Item2).Take(12).Select(x => x.Dto).ToList();

        // Results answer in the query's language: پیتزا in → پیتزا out, whatever
        // the app language is. Recognized words are swapped for the typed alias.
        var typed = SearchAliases.MatchedAliases(s);
        if (typed.Count > 0)
        {
            var keywordsById = allDishes.ToDictionary(d => d.Dto.MenuItemId, d => d.SearchKeywords);
            cards = cards.Select(c => c with { Name = SearchAliases.LocalizeName(c.Name, typed) }).ToList();
            dishResults = dishResults.Select(d => d with
            {
                Name = SearchAliases.LocalizeName(d.Name, typed, keywordsById.GetValueOrDefault(d.MenuItemId) ?? d.Description)
            }).ToList();
        }

        if (didYouMean == SmartSearch.Normalize(s).Trim()) didYouMean = null;
        return new SearchResultsDto(cards, dishResults, didYouMean);
    }

    // ---------- Customer: favorites ----------

    [HttpGet("favorites")]
    [Authorize(Roles = "Customer,RestaurantOwner,Driver")]
    public async Task<List<int>> Favorites() =>
        await db.Favorites.Where(f => f.UserId == CurrentUserId).Select(f => f.RestaurantId).ToListAsync();

    [HttpPost("{id:int}/favorite")]
    [Authorize(Roles = "Customer,RestaurantOwner,Driver")]
    public async Task<IActionResult> ToggleFavorite(int id)
    {
        var existing = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == CurrentUserId && f.RestaurantId == id);
        if (existing is not null)
        {
            db.Favorites.Remove(existing);
            await db.SaveChangesAsync();
            return Ok(new { isFavorite = false });
        }
        if (!await db.Restaurants.AnyAsync(r => r.Id == id && r.IsApproved))
            return NotFound();
        db.Favorites.Add(new Models.FavoriteRestaurant { UserId = CurrentUserId, RestaurantId = id });
        await db.SaveChangesAsync();
        return Ok(new { isFavorite = true });
    }

    // ---------- Owner: my restaurant ----------

    /// <summary>
    /// Every business this account owns — the picker after login and the app-bar
    /// switcher. The active one is whichever the current token carries.
    /// </summary>
    /// <summary>
    /// A partner opens another business under their own account. No admin involved in the
    /// CREATION — but the store starts unapproved and closed, so nothing reaches a
    /// customer until an administrator says so. The owner is always the caller: there is
    /// no way to create a store for anyone else from here.
    /// </summary>
    [HttpPost("my-stores")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ActionResult<StoreSummaryDto>> CreateMyStore(CreateMyStoreRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 2)
            return BadRequest(new { message = "The business needs a name." });
        if (!await db.Cuisines.AnyAsync(c => c.Id == req.CuisineId))
            return BadRequest(new { message = "Unknown cuisine." });

        // A runaway loop or a confused tap shouldn't mint businesses without limit.
        if (await db.Restaurants.CountAsync(r => r.OwnerUserId == CurrentUserId) >= 20)
            return BadRequest(new { message = "This account already has the maximum number of businesses." });

        var createNames = CleanNames(req.Names);
        if (req.Names is not null && !createNames.ContainsKey("ar"))
            return BadRequest(new { message = "The Arabic name is required." });
        var store = new Restaurant
        {
            OwnerUserId = CurrentUserId,
            Name = CanonicalName(createNames) ?? name,
            NameLocalized = NamesToJson(createNames),
            Description = "",
            CuisineId = req.CuisineId,
            StoreType = req.StoreType,
            Area = (req.Area ?? "").Trim(),
            Street = "",
            Phone = "",
            DeliveryFee = 0.500m,
            MinOrder = 1.000m,
            AvgPrepMinutes = 20,
            IsOpen = false,
            IsApproved = false,
            AllowsPickup = true,
            CommissionPercent = 0m,
            Lat = req.Lat,
            Lng = req.Lng,
            CreatedAt = DateTime.Now,
        };
        db.Restaurants.Add(store);
        await db.SaveChangesAsync();

        // The extra types the owner ticked in the wizard, if any — same rules as the
        // settings save: distinct, never the main one, and only real food cuisines.
        if (req.CuisineIds is { Count: > 0 } && req.StoreType == StoreType.Restaurant)
        {
            var wanted = req.CuisineIds.Where(id => id != store.CuisineId).Distinct().ToList();
            string[] verticals = ["Grocery", "Pharmacy", "Flowers & Gifts", "Flowers", "Shopping"];
            var valid = await db.Cuisines
                .Where(c => wanted.Contains(c.Id) && !verticals.Contains(c.Name))
                .Select(c => c.Id).ToListAsync();
            foreach (var id in valid)
                db.RestaurantCuisines.Add(new RestaurantCuisine { RestaurantId = store.Id, CuisineId = id });
            if (valid.Count > 0) await db.SaveChangesAsync();
        }

        await catalog.SeedDefaultMenuAsync(store.Id); // the house Drinks/Water shelf
        await DefaultFloor.SeedAsync(db, store.Id);          // two salons, sixteen tables
        await StoreWordIndex.ReindexAsync(db, store);        // findable from day one
        alerts.NewBusiness(store.Id, store.Name, CurrentUserName, "an existing owner");

        return Ok(new StoreSummaryDto(store.Id, store.Name, store.LogoEmoji, store.StoreType,
            store.Area, store.IsApproved, store.IsOpen));
    }

    [HttpGet("my-stores")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<List<StoreSummaryDto>> MyStores()
    {
        var owned = await db.Restaurants
            .Where(r => r.OwnerUserId == CurrentUserId)
            .Select(r => new StoreSummaryDto(r.Id, r.Name, r.LogoEmoji, r.StoreType, r.Area, r.IsApproved, r.IsOpen, r.LogoData))
            .ToListAsync();
        var joined = await db.StoreMembers
            .Where(m => m.UserId == CurrentUserId && m.IsActive)
            .Join(db.Restaurants, m => m.RestaurantId, r => r.Id,
                (m, r) => new StoreSummaryDto(r.Id, r.Name, r.LogoEmoji, r.StoreType, r.Area, r.IsApproved, r.IsOpen, r.LogoData))
            .ToListAsync();
        // Logos travel as api/media links where the API has a public origin (production):
        // the switcher lists every store the owner has, and each inline logo is ~30 KB.
        return owned.Concat(joined).DistinctBy(s => s.Id).OrderBy(s => s.Id)
            .Select(s => s with { LogoData = MediaLinks.Logo(config, s.Id, s.LogoData) }).ToList();
    }

    [HttpGet("mine")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ActionResult<MyRestaurantDto>> Mine()
    {
        var r = await db.Restaurants.Include(x => x.Cuisine)
            .FirstOrDefaultAsync(x => x.Id == CurrentRestaurantId);
        if (r is null) return NotFound();
        var ratings = await RatingsFor([r.Id]);
        var (rating, count) = ratings.GetValueOrDefault(r.Id);
        // Main type first, then any extras — the same order the settings page shows.
        var cuisineIds = new List<int> { r.CuisineId };
        cuisineIds.AddRange(await db.RestaurantCuisines
            .Where(rc => rc.RestaurantId == r.Id).Select(rc => rc.CuisineId).ToListAsync());
        return new MyRestaurantDto(r.Id, r.Name, r.Description, r.CuisineId, r.Cuisine.Name,
            r.LogoEmoji, r.BannerColor, r.Area, r.Street, r.Phone, r.DeliveryFee, r.MinOrder,
            r.AvgPrepMinutes, r.IsOpen, r.IsApproved, r.CommissionPercent, rating, count, r.StoreType,
            r.AllowsPickup, r.TaxPercent, r.PosDefaultOpen,
            r.Email, r.Whatsapp, r.Website, r.Instagram, r.CrNumber, r.Lat, r.Lng,
            NamesFromJson(r.NameLocalized), r.SetupStep, NamesFromJson(r.AddressLocalized), r.LogoData, r.SelfDelivery,
            cuisineIds, r.Slug, r.DirectPrint, r.VatNumber, r.OnlineReservations,
            r.ReservePricePerTable, r.ReservePricePerMinute,
            r.SmtpHost, r.SmtpPort, r.SmtpUser, r.SmtpPassword, r.SmtpFrom, r.QrCardText,
            r.DishStockPhotos, r.KitchenLanguage, await prefs.CurrencyAsync(r.Id));
    }

    /// <summary>
    /// Words a shop may not take as its handle: every first path segment the client app
    /// itself uses, plus the obvious impersonations. A shop called "checkout" would
    /// shadow the checkout page.
    /// </summary>
    internal static bool IsValidSlug(string slug) => ValidSlug(slug);
    internal static bool IsReservedSlug(string slug) => ReservedSlugs.Contains(slug);

    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "restaurant", "restaurants", "checkout", "orders", "profile", "addresses",
        "signin", "signup", "login", "track", "bill", "reserve", "chat", "search",
        "admin", "partner", "delivery", "api", "qr", "sitemap.xml", "robots.txt",
        "orderorange", "www", "app", "help", "about", "contact", "terms", "privacy",
    };

    /// <summary>Lowercase a-z, 0-9 and single hyphens, 3–40 chars, no leading/trailing hyphen.</summary>
    private static bool ValidSlug(string slug) =>
        System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9](?:-?[a-z0-9]){2,39}$");

    /// <summary>Is this handle free? The partner settings page asks while the owner types.</summary>
    [HttpGet("slug-available")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SlugAvailable(string slug)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (!ValidSlug(slug)) return Ok(new { available = false, reason = "invalid" });
        if (ReservedSlugs.Contains(slug)) return Ok(new { available = false, reason = "reserved" });
        var taken = await db.Restaurants.AnyAsync(r => r.Slug == slug && r.Id != CurrentRestaurantId);
        return Ok(new { available = !taken, reason = taken ? "taken" : "" });
    }

    /// <summary>The owner claims (or clears) the shop's handle.</summary>
    /// <summary>The owner's own line on the printed table-QR cards. Empty = default.</summary>
    [HttpPut("mine/qr-text")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SetQrText(QrTextRequest req)
    {
        var r = await db.Restaurants.FindAsync(CurrentRestaurantId);
        if (r is null) return NotFound();
        r.QrCardText = (req.Text ?? "").Trim() is { Length: > 300 } long_ ? long_[..300] : (req.Text ?? "").Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("mine/slug")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SetSlug(SetSlugRequest req)
    {
        var r = await db.Restaurants.FindAsync(CurrentRestaurantId);
        if (r is null) return NotFound();

        var slug = (req.Slug ?? "").Trim().ToLowerInvariant();
        if (slug.Length == 0) { r.Slug = ""; await db.SaveChangesAsync(); return NoContent(); }

        if (!ValidSlug(slug))
            return BadRequest(new { message = "3–40 characters: lowercase letters, digits and hyphens." });
        if (ReservedSlugs.Contains(slug))
            return BadRequest(new { message = "That address is reserved." });
        if (await db.Restaurants.AnyAsync(x => x.Slug == slug && x.Id != CurrentRestaurantId))
            return BadRequest(new { message = "That address is already taken." });

        r.Slug = slug;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Two owners can pass the check above at the same moment; the unique index is
            // the real referee, and losing that race reads the same as "taken".
            return BadRequest(new { message = "That address is already taken." });
        }
        return NoContent();
    }

    /// <summary>
    /// Resolves a handle to a store id — how orderorange.com/&lt;slug&gt; finds its shop.
    /// 404 for unknown or blank, so the client can fall back to its normal routing.
    /// </summary>
    [HttpGet("by-slug/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> BySlug(string slug)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (slug.Length == 0 || !ValidSlug(slug)) return NotFound();
        var id = await db.Restaurants
            .Where(r => r.Slug == slug && r.IsApproved)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();
        return id is null ? NotFound() : Ok(new { id });
    }

    [HttpPut("mine")]
    [Authorize(Roles = "RestaurantOwner")]
    [RequirePerm(Perm.Settings)]
    public async Task<IActionResult> Update(UpdateRestaurantRequest req)
    {
        var r = await db.Restaurants.FindAsync(CurrentRestaurantId);
        if (r is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Restaurant name is required." });
        if (req.DeliveryFee < 0 || req.MinOrder < 0) return BadRequest(new { message = "Fees cannot be negative." });
        if (!await db.Cuisines.AnyAsync(c => c.Id == req.CuisineId))
            return BadRequest(new { message = "Unknown cuisine." });

        // Each store type has its own categories — a vertical store is snapped to its
        // vertical category, and vertical categories are refused for food restaurants.
        var cuisineId = req.CuisineId;
        var verticalName = req.StoreType switch
        {
            StoreType.Grocery => "Grocery",
            StoreType.Pharmacy => "Pharmacy",
            StoreType.Flowers => "Flowers & Gifts",
            StoreType.Shop => "Shopping",
            _ => null
        };
        if (verticalName is not null)
        {
            var vertical = await db.Cuisines.FirstOrDefaultAsync(c => c.Name == verticalName);
            if (vertical is not null) cuisineId = vertical.Id;
        }
        else
        {
            var chosen = await db.Cuisines.FirstAsync(c => c.Id == cuisineId);
            string[] verticals = ["Grocery", "Pharmacy", "Flowers & Gifts", "Flowers", "Shopping"];
            if (verticals.Contains(chosen.Name))
                return BadRequest(new { message = "Pick a food cuisine for a restaurant." });
        }

        if (req.Names is not null)
        {
            var names = CleanNames(req.Names);
            if (!names.ContainsKey("ar"))
                return BadRequest(new { message = "The Arabic name is required." });
            r.NameLocalized = NamesToJson(names);
            r.Name = CanonicalName(names) ?? req.Name.Trim();
        }
        if (req.Addresses is not null)
        {
            var addresses = CleanNames(req.Addresses);
            r.AddressLocalized = NamesToJson(addresses);
            r.Area = addresses.GetValueOrDefault("ar") ?? CanonicalName(addresses) ?? r.Area;
        }
        else
        {
            r.Name = req.Name.Trim();
        }
        r.Description = req.Description.Trim();
        r.CuisineId = cuisineId;
        r.LogoEmoji = req.LogoEmoji.Trim();
        r.BannerColor = req.BannerColor.Trim();
        r.Area = req.Area.Trim();
        r.Street = req.Street.Trim();
        r.Phone = req.Phone.Trim();
        r.DeliveryFee = req.DeliveryFee;
        r.MinOrder = req.MinOrder;
        r.AvgPrepMinutes = req.AvgPrepMinutes;
        r.StoreType = req.StoreType;
        r.AllowsPickup = req.AllowsPickup;
        r.TaxPercent = Math.Clamp(req.TaxPercent, 0m, 25m);
        r.PosDefaultOpen = req.PosDefaultOpen;
        r.DirectPrint = req.DirectPrint;
        r.OnlineReservations = req.OnlineReservations;
        r.DishStockPhotos = req.DishStockPhotos;
        r.KitchenLanguage = (req.KitchenLanguage ?? "").Trim().ToLowerInvariant() is { Length: <= 8 } kl ? kl : "";

        // The currency symbol is a display preference, so it lives in Mongo rather than as
        // a new SQL column: this schema is EnsureCreated, which never adds a column to a
        // database that already exists.
        await prefs.SetCurrencyAsync(r.Id, req.Currency);
        // A price the house never set stays nothing; negatives are simply refused.
        r.ReservePricePerTable = Math.Max(0m, req.ReservePricePerTable);
        r.ReservePricePerMinute = Math.Max(0m, req.ReservePricePerMinute);
        // The store's own mailbox. Whitespace is never part of a host or an app password.
        r.SmtpHost = (req.SmtpHost ?? "").Trim();
        r.SmtpPort = Math.Clamp(req.SmtpPort, 1, 65535);
        r.SmtpUser = (req.SmtpUser ?? "").Trim();
        r.SmtpPassword = (req.SmtpPassword ?? "").Trim();
        r.SmtpFrom = (req.SmtpFrom ?? "").Trim();
        // Locked ON platform-wide: every store carries its own orders for now. The
        // request's value is ignored so no client — the app or a raw call — can turn
        // a store back onto a rider fleet that isn't running.
        r.SelfDelivery = true;
        r.Email = (req.Email ?? "").Trim();
        r.Whatsapp = (req.Whatsapp ?? "").Trim();
        r.Website = (req.Website ?? "").Trim();
        r.Instagram = (req.Instagram ?? "").Trim().TrimStart('@');
        r.CrNumber = (req.CrNumber ?? "").Trim();
        r.VatNumber = (req.VatNumber ?? "").Trim();
        r.Lat = req.Lat;
        r.Lng = req.Lng;

        // A kitchen can carry more than one type — Iranian AND Arabic grill. The first
        // id stays the main CuisineId set above; the rest go to the junction, replaced
        // wholesale so the list is exactly what the owner last chose. A vertical store's
        // type is fixed by its vertical, so extras only apply to food restaurants.
        if (req.CuisineIds is { Count: > 0 } && verticalName is null)
        {
            var wanted = req.CuisineIds.Where(id => id != cuisineId).Distinct().ToList();
            string[] verticals = ["Grocery", "Pharmacy", "Flowers & Gifts", "Flowers", "Shopping"];
            var valid = await db.Cuisines
                .Where(c => wanted.Contains(c.Id) && !verticals.Contains(c.Name))
                .Select(c => c.Id).ToListAsync();

            var current = await db.RestaurantCuisines.Where(rc => rc.RestaurantId == r.Id).ToListAsync();
            db.RestaurantCuisines.RemoveRange(current.Where(rc => !valid.Contains(rc.CuisineId)));
            foreach (var id in valid.Where(id => current.All(rc => rc.CuisineId != id)))
                db.RestaurantCuisines.Add(new RestaurantCuisine { RestaurantId = r.Id, CuisineId = id });
        }

        await db.SaveChangesAsync();
        // The renamed store must be findable under its new words — in every language.
        await StoreWordIndex.ReindexAsync(db, r);
        return NoContent();
    }

    /// <summary>
    /// Reads a location out of a Google Maps link — including the short goo.gl
    /// ones, which are followed server-side to their full form first.
    /// </summary>
    [HttpPost("maplink")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ActionResult<MapPointDto>> ParseMapLink(MapLinkRequest req)
    {
        // Anonymous (the registration form needs it), so the server only ever
        // follows GOOGLE link hosts — it must never become a generic URL fetcher.
        var url = (req.Url ?? "").Trim();
        if (url.Length is 0 or > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !IsGoogleMapHost(uri.Host))
            return BadRequest(new { message = "Paste a Google Maps link." });

        var point = TryReadMapPoint(url);
        if (point is null)
        {
            try
            {
                using var response = await _mapClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var final = response.RequestMessage?.RequestUri?.ToString() ?? "";
                point = TryReadMapPoint(final);
                if (point is null && response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    point = TryReadMapPoint(body[..Math.Min(body.Length, 200_000)]);
                }
            }
            catch { /* unreachable link — fall through to the 400 below */ }
        }
        return point is null ? BadRequest(new { message = "No location found in that link." }) : Ok(point);
    }

    private static bool IsGoogleMapHost(string host) =>
        host.Equals("goo.gl", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".goo.gl", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("maps.google.", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("www.google.", StringComparison.OrdinalIgnoreCase);

    private static readonly HttpClient _mapClient = new(new HttpClientHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(10) };

    // ---------- The name in every tongue ----------

    /// <summary>Keeps only sane entries: two-letter language keys, trimmed, 120 chars max.</summary>
    internal static Dictionary<string, string> CleanNames(Dictionary<string, string>? names) =>
        CleanLocalized(names, 120);

    /// <summary>
    /// Same filter as CleanNames but for prose. Descriptions were going through the NAME
    /// cleaner, whose 120-character cap silently DROPPED any language whose sentence ran
    /// long — a dish could come back with eleven of its fourteen descriptions missing and
    /// nothing to say why. A description is a paragraph, not a label, so it gets room.
    /// </summary>
    internal static Dictionary<string, string> CleanTexts(Dictionary<string, string>? texts) =>
        CleanLocalized(texts, 1500);

    private static Dictionary<string, string> CleanLocalized(Dictionary<string, string>? map, int maxLength) =>
        (map ?? new Dictionary<string, string>())
            .Where(kv => System.Text.RegularExpressions.Regex.IsMatch(kv.Key ?? "", "^[a-z]{2}$") &&
                         !string.IsNullOrWhiteSpace(kv.Value) && kv.Value.Trim().Length <= maxLength)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

    internal static string NamesToJson(Dictionary<string, string> names) =>
        System.Text.Json.JsonSerializer.Serialize(names);

    internal static Dictionary<string, string> NamesFromJson(string? json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                string.IsNullOrWhiteSpace(json) ? "{}" : json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The one name the old single-name fields keep showing: English first, then Arabic, then anything.</summary>
    internal static string? CanonicalName(Dictionary<string, string> names) =>
        names.GetValueOrDefault("en") ?? names.GetValueOrDefault("ar") ?? names.Values.FirstOrDefault();

    /// <summary>The shapes Google hides coordinates in: @lat,lng · q=/ll= pairs · !3d…!4d….</summary>
    internal static MapPointDto? TryReadMapPoint(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)");
        if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(text, @"[?&](?:q|ll|query|destination)=(-?\d{1,2}\.\d+)(?:,|%2C)(-?\d{1,3}\.\d+)");
        if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(text, @"!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var lng))
            return null;
        return lat is < -90 or > 90 || lng is < -180 or > 180 ? null : new MapPointDto(lat, lng);
    }

    /// <summary>The wizard writes its bookmark here; -1 closes the book.</summary>
    [HttpPost("mine/setup-step/{step:int}")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SetSetupStep(int step)
    {
        var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == CurrentRestaurantId && x.OwnerUserId == CurrentUserId);
        if (r is null) return NotFound();
        r.SetupStep = Math.Clamp(step, -1, 5);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("mine/toggle-open")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> ToggleOpen()
    {
        var r = await db.Restaurants.FindAsync(CurrentRestaurantId);
        if (r is null) return NotFound();
        r.IsOpen = !r.IsOpen;
        await db.SaveChangesAsync();
        return Ok(new { isOpen = r.IsOpen });
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Rewards a store whose NAME — in any language it publishes — really is what was
    /// typed. An exact name outranks a prefix, a prefix outranks a mention.
    /// </summary>
    private static int NameMatchBonus(Models.Restaurant r, string needle)
    {
        if (needle.Length < 2) return 0;
        var best = 0;
        foreach (var candidate in NameVariants(r))
        {
            var name = SmartSearch.Normalize(candidate);
            if (name.Length == 0) continue;
            if (name == needle) best = Math.Max(best, 60);
            else if (name.StartsWith(needle, StringComparison.Ordinal)) best = Math.Max(best, 45);
            else if (name.Contains(needle, StringComparison.Ordinal)) best = Math.Max(best, 28);
        }
        return best;
    }

    private static IEnumerable<string> NameVariants(Models.Restaurant r)
    {
        yield return r.Name;
        var localized = NamesFromJson(r.NameLocalized);
        if (localized is null) yield break;
        foreach (var name in localized.Values)
            yield return name;
    }

    /// <summary>
    /// The generated catalog names branches "Brand - Area"; everything before the dash
    /// is the chain. Used to stop one chain filling the whole result list.
    /// </summary>
    private static string ChainKey(string name)
    {
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? name[..dash] : name;
    }

    private RestaurantCardDto Card(Models.Restaurant r, Dictionary<int, (double avg, int count)> ratings,
        Dictionary<int, List<Models.RestaurantHours>> hoursByRestaurant, double? lat = null, double? lng = null,
        Dictionary<int, List<string>>? extraCuisines = null, HashSet<int>? photoStores = null)
    {
        var (rating, count) = ratings.GetValueOrDefault(r.Id);
        var hours = hoursByRestaurant.GetValueOrDefault(r.Id, []);
        // Effectively open = the owner's switch AND today's schedule window.
        var isOpen = r.IsOpen && HoursHelper.IsWithinHours(hours, DateTime.Now);
        double? distance = lat is not null && lng is not null && r.Lat is not null && r.Lng is not null
            ? HaversineKm(lat.Value, lng.Value, r.Lat.Value, r.Lng.Value)
            : null;
        // Every type the shop carries, main first; null when the main one is all there is,
        // so the payload does not grow for the thousands of single-type stores.
        List<string>? cuisines = null;
        if (extraCuisines?.GetValueOrDefault(r.Id) is { Count: > 0 } extras)
            cuisines = [r.Cuisine.Name, .. extras];
        return new RestaurantCardDto(r.Id, r.Name, r.Description, r.Cuisine.Name, r.LogoEmoji,
            r.BannerColor, r.Area, rating, count, r.DeliveryFee, r.MinOrder, r.AvgPrepMinutes, isOpen,
            HoursHelper.TodayLabel(hours, DateTime.Now), r.StoreType, distance, r.AllowsPickup, r.TaxPercent,
            MediaLinks.Logo(config, r.Id, r.LogoData), NamesFromJson(r.NameLocalized), cuisines,
            photoStores?.Contains(r.Id) == true, r.OnlineReservations,
            r.ReservePricePerTable, r.ReservePricePerMinute, r.DishStockPhotos,
            r.Phone, r.Instagram, r.Lat, r.Lng, r.Slug);
    }

    // Which stores carry real banner photos — a tiny table, cached so browse pays nothing.
    private static HashSet<int> _photoStores = [];
    private static DateTime _photoStoresAt;
    private async Task<HashSet<int>> PhotoStoresAsync()
    {
        if (DateTime.Now - _photoStoresAt > TimeSpan.FromMinutes(5))
        {
            _photoStores = (await db.RestaurantPhotos.Select(p => p.RestaurantId).Distinct().ToListAsync()).ToHashSet();
            _photoStoresAt = DateTime.Now;
        }
        return _photoStores;
    }

    // ---------- Store photo gallery (max 4, shown as a banner slideshow) ----------

    [HttpGet("{id:int}/photos")]
    [AllowAnonymous]
    public async Task<List<RestaurantPhotoDto>> Photos(int id) =>
        (await db.RestaurantPhotos.Where(p => p.RestaurantId == id)
            .OrderByDescending(p => p.IsMain).ThenBy(p => p.Id)
            .Take(4).ToListAsync())
        .Select(p => new RestaurantPhotoDto(p.Id, MediaLinks.Banner(config, p.Id, p.Data), p.IsMain)).ToList();

    /// <summary>Product photo set for the dish sheet — main first. Template items (negative ids) have none.</summary>
    [HttpGet("menu-items/{id:int}/photos")]
    [AllowAnonymous]
    public async Task<List<DishPhotoDto>> DishPhotos(int id) =>
        id <= 0
            ? []
            : (await db.MenuItemPhotos.Where(p => p.MenuItemId == id)
                .OrderByDescending(p => p.IsMain).ThenBy(p => p.Id)
                .Take(4).ToListAsync())
              .Select(p => new DishPhotoDto(p.Id, MediaLinks.DishSetPhoto(config, p.Id, p.Data), p.IsMain)).ToList();

    [HttpGet("mine/photos")]
    [Authorize(Roles = "RestaurantOwner")]
    public Task<List<RestaurantPhotoDto>> MyPhotos() => Photos(CurrentRestaurantId);

    /// <summary>
    /// The shop's own logo, shown wherever the store introduces itself. Sending an
    /// empty body clears it and the emoji takes over again.
    /// </summary>
    [HttpPut("mine/logo")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SetLogo(AddStorePhotoRequest req)
    {
        var store = await db.Restaurants.FindAsync(CurrentRestaurantId);
        if (store is null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Data))
        {
            store.LogoData = null;
        }
        else
        {
            if (!req.Data.StartsWith("data:image/"))
                return BadRequest(new { message = "Only images are allowed." });
            if (req.Data.Length > 400_000)
                return BadRequest(new { message = "Logo is too large — it must be under ~250 KB." });
            store.LogoData = req.Data;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("mine/photos")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> AddPhoto(AddStorePhotoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Data) || !req.Data.StartsWith("data:image/"))
            return BadRequest(new { message = "Only images are allowed." });
        if (req.Data.Length > 800_000)
            return BadRequest(new { message = "Photo is too large — it must be under ~500 KB." });
        var count = await db.RestaurantPhotos.CountAsync(p => p.RestaurantId == CurrentRestaurantId);
        if (count >= 4)
            return BadRequest(new { message = "A store can have at most 4 photos — delete one first." });

        db.RestaurantPhotos.Add(new Models.RestaurantPhoto
        {
            RestaurantId = CurrentRestaurantId,
            Data = req.Data,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// The notification center: everything that wants the owner's eyes, from every
    /// corner of the store — new orders, guest chats, stock alerts, payments coming
    /// due, reservations about to arrive. One feed, severity-sorted, polled.
    /// </summary>
    [HttpGet("mine/notifications")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<NotificationsDto> MyNotifications([FromServices] TableChatStore chat,
        [FromServices] CommunityChatStore teamChat, [FromServices] SupportStore support)
    {
        var items = new List<NotificationDto>();
        var now = DateTime.Now;

        // ── New orders waiting for a yes ──
        var pending = await db.Orders
            .Where(o => o.RestaurantId == CurrentRestaurantId && o.Status == OrderStatus.Pending)
            .OrderByDescending(o => o.PlacedAt)
            .Take(10)
            .Select(o => new { o.Number, o.PlacedAt, o.Total, o.TableName, Customer = o.Customer.FullName })
            .ToListAsync();
        // A table's order and an online order ring different bells on the app bar:
        // "table" for anything ordered at a table (QR / dine-in), "order" for the rest.
        items.AddRange(pending.Select(o => new NotificationDto(
            o.TableName != null ? "table" : "order", o.Number,
            (o.TableName != null ? $"🍽 {o.TableName} · " : "") + $"{o.Customer} · {o.Total:0.###} OMR",
            o.PlacedAt, "/orders", "urgent")));

        // ── Rounds ordered from a table's QR: they land on the open invoice, never as
        // an Order, so the feed reads them straight off the tab lines. One entry per
        // round (all lines of a POST share AddedAt), gone once the invoice is closed. ──
        var qrRounds = await db.StoreTabLines
            .Where(l => l.Source == "qr" && l.AddedAt > now.AddHours(-12) && l.Tab.RestaurantId == CurrentRestaurantId)
            .GroupBy(l => new { l.StoreTabId, l.Tab.TableId, l.AddedAt })
            .Select(g => new { g.Key.StoreTabId, g.Key.TableId, g.Key.AddedAt,
                Count = g.Sum(l => l.Quantity), Amount = g.Sum(l => l.Quantity * l.UnitPrice) })
            .OrderByDescending(g => g.AddedAt).Take(10)
            .ToListAsync();
        if (qrRounds.Count > 0)
        {
            var roundTables = await db.StoreTables
                .Where(t => t.RestaurantId == CurrentRestaurantId && qrRounds.Select(r => r.TableId).Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            items.AddRange(qrRounds.Select(r => new NotificationDto(
                "table",
                roundTables.TryGetValue(r.TableId, out var tn) ? tn : $"#{r.TableId}",
                $"📱 QR · {r.Count} × · {r.Amount:0.###} OMR",
                r.AddedAt, $"/pos?tab={r.StoreTabId}", "urgent")));
        }

        // ── Guest chats from live tables, newest per table ──
        var messages = await chat.RecentGuestAsync(CurrentRestaurantId, now.AddHours(-6));
        var tableIds = messages.Select(m => m.TableId).Distinct().ToList();
        var tables = await db.StoreTables
            .Where(t => t.RestaurantId == CurrentRestaurantId && tableIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);
        items.AddRange(messages
            .GroupBy(m => m.TableId)
            .Select(g => g.OrderByDescending(m => m.Id).First())
            .Select(m => new NotificationDto(
                "chat",
                tables.TryGetValue(m.TableId, out var tableName) ? tableName : $"#{m.TableId}",
                m.Deleted ? "🚫" : m.Text.Length > 0 ? (m.Text.Length > 60 ? m.Text[..60] + "…" : m.Text)
                    : m.Audio is not null ? "🎤" : m.File is not null ? "📄" : "📷",
                m.At, $"/table-chats/{m.TableId}", "info")));

        // ── Team direct messages waiting for me, newest per sender ──
        var dms = await teamChat.RecentUnreadForAsync(CurrentRestaurantId, CurrentUserId);
        items.AddRange(dms.Select(m => new NotificationDto(
            "chat",
            m.FromName,
            m.Deleted ? "🚫" : m.Text.Length > 0 ? (m.Text.Length > 60 ? m.Text[..60] + "…" : m.Text)
                : m.Audio is not null ? "🎤" : m.File is not null ? "📄" : "📷",
            m.At, $"/chat/{m.FromUserId}", "info")));

        // ── The shelf's own complaints, still unread ──
        var alerts = await db.StoreAlerts
            .Where(a => a.RestaurantId == CurrentRestaurantId && a.SeenAt == null)
            .OrderByDescending(a => a.Id).Take(10)
            .ToListAsync();
        items.AddRange(alerts.Select(a => new NotificationDto(
            "stock", a.MaterialName, $"{a.Quantity:0.##} {a.Unit}", a.CreatedAt, "/materials",
            a.Type == "out" ? "urgent" : "warn")));

        // ── Supplier money: overdue always, plus what lands within 3 days ──
        var soon = DateTime.Today.AddDays(4);
        var payments = await db.PurchasePayments
            .Where(x => x.PaidAt == null && x.DueDate < soon &&
                db.MaterialPurchases.Any(p => p.Id == x.PurchaseId && p.RestaurantId == CurrentRestaurantId))
            .OrderBy(x => x.DueDate).Take(10)
            .Select(x => new { x.Amount, x.DueDate, x.Purchase.Supplier })
            .ToListAsync();
        items.AddRange(payments.Select(p => new NotificationDto(
            "payment", p.Supplier, $"{p.Amount:0.###} OMR · {p.DueDate:dd MMM}", p.DueDate,
            "/materials/payments", p.DueDate.Date < DateTime.Today ? "urgent" : "warn")));

        // ── Parties arriving within three hours ──
        var reservations = await db.TableReservations
            .Where(r => r.RestaurantId == CurrentRestaurantId
                        && (r.Status == "pending" || r.Status == "confirmed")
                        && r.At > now && r.At < now.AddHours(3))
            .OrderBy(r => r.At).Take(10)
            .ToListAsync();
        items.AddRange(reservations.Select(r => new NotificationDto(
            "reservation", r.TableName, $"{r.Name} · 👥 {r.Guests} · {r.At:HH:mm}", r.At,
            "/reservations", "info")));

        // ── Calendar reminders whose moment has come (and the event has not long passed) ──
        var reminders = await db.StoreCalendarEvents
            .Where(e => e.RestaurantId == CurrentRestaurantId && e.RemindMinutes != null
                        && e.StartAt > now.AddHours(-2) && e.StartAt < now.AddDays(8))
            .OrderBy(e => e.StartAt).Take(20)
            .ToListAsync();
        items.AddRange(reminders
            .Where(e => e.StartAt.AddMinutes(-e.RemindMinutes!.Value) <= now)
            .Select(e => new NotificationDto(
                "calendar", e.Title,
                e.AllDay ? e.StartAt.ToString("ddd d MMM") : (e.StartAt.Date == now.Date ? e.StartAt.ToString("HH:mm") : e.StartAt.ToString("ddd d MMM · HH:mm")),
                e.StartAt, "/calendar", e.StartAt <= now.AddMinutes(30) ? "warn" : "info")));

        // ── Support wrote back, or moved a ticket, and nobody here has read it yet ──
        var tickets = await support.UnreadForPartnerAsync(CurrentRestaurantId, 10);
        items.AddRange(tickets.Select(t => new NotificationDto(
            "support", $"{t.Number} · {t.Subject}",
            t.LastPreview.Length > 0 ? t.LastPreview : t.Status,
            t.LastMessageAt, $"/support/{t.Id}", t.Status == "waiting" ? "warn" : "info")));

        var rank = (string s) => s switch { "urgent" => 0, "warn" => 1, _ => 2 };
        var sorted = items.OrderBy(i => rank(i.Severity)).ThenByDescending(i => i.At).ToList();
        return new NotificationsDto(sorted.Count, sorted);
    }

    /// <summary>
    /// Anonymous visit analytics for the owner: counts per hour/day/month plus the
    /// most-viewed products. Never exposes who visited — there is nothing to expose.
    /// </summary>
    [RequirePerm(Perm.Reports)]
    [HttpGet("mine/visits")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<StoreVisitStatsDto> MyVisits(string period = "week")
    {
        var now = DateTime.Now;
        var from = period switch
        {
            "day" => now.Date,
            "month" => now.Date.AddDays(-29),
            "year" => new DateTime(now.Year, now.Month, 1).AddMonths(-11),
            _ => now.Date.AddDays(-6)
        };

        // The window immediately before this one, same length, for the trend badges.
        var previousFrom = period switch
        {
            "day" => from.AddDays(-1),
            "month" => from.AddDays(-30),
            "year" => from.AddMonths(-12),
            _ => from.AddDays(-7)
        };

        var previous = await db.StoreVisits
            .Where(v => v.RestaurantId == CurrentRestaurantId && v.At >= previousFrom && v.At < from)
            .GroupBy(v => v.MenuItemId == null)
            .Select(g => new { IsStore = g.Key, Count = g.Count() })
            .ToListAsync();

        var visits = await db.StoreVisits
            .Where(v => v.RestaurantId == CurrentRestaurantId && v.At >= from)
            .Select(v => new { v.At, v.MenuItemId, v.ItemName })
            .ToListAsync();

        var store = visits.Where(v => v.MenuItemId is null).ToList();
        var products = visits.Where(v => v.MenuItemId is not null).ToList();

        var buckets = new List<VisitBucketDto>();
        switch (period)
        {
            case "day":
                for (var h = 0; h < 24; h++)
                    buckets.Add(new VisitBucketDto($"{h:00}", store.Count(v => v.At.Hour == h)));
                break;
            case "year":
                for (var m = 0; m < 12; m++)
                {
                    var month = from.AddMonths(m);
                    buckets.Add(new VisitBucketDto(month.ToString("MMM"),
                        store.Count(v => v.At.Year == month.Year && v.At.Month == month.Month)));
                }
                break;
            default: // week 7 / month 30 daily buckets
                var days = period == "month" ? 30 : 7;
                for (var d = 0; d < days; d++)
                {
                    var day = from.AddDays(d);
                    buckets.Add(new VisitBucketDto(day.ToString(days == 7 ? "ddd" : "d MMM"),
                        store.Count(v => v.At.Date == day)));
                }
                break;
        }

        var top = products
            .GroupBy(v => v.ItemName ?? "—")
            .Select(g => new ProductViewsDto(g.Key, g.Count()))
            .OrderByDescending(p => p.Views)
            .Take(10)
            .ToList();

        return new StoreVisitStatsDto(store.Count, products.Count, buckets, top,
            previous.FirstOrDefault(p => p.IsStore)?.Count ?? 0,
            previous.FirstOrDefault(p => !p.IsStore)?.Count ?? 0);
    }

    // ---------- Partner reports: three separate endpoints, aggregated IN SQL and
    // paged server-side so no report ever loads the full order set into memory. ----------

    /// <summary>
    /// Resolves the report window. A custom from/to range wins over the period
    /// keyword and is CAPPED AT ONE MONTH (like the admin reports) so a report
    /// can never pull an unbounded window.
    /// </summary>
    private static (DateTime From, DateTime ToExclusive, string Kind, int Days) ResolveRange(
        string period, DateTime? from, DateTime? to)
    {
        var now = DateTime.Now;
        if (from.HasValue && to.HasValue && to.Value.Date >= from.Value.Date)
        {
            var start = from.Value.Date;
            var end = to.Value.Date;
            if ((end - start).TotalDays > 30) end = start.AddDays(30);
            return (start, end.AddDays(1), "range", (int)(end - start).TotalDays + 1);
        }
        return period switch
        {
            "day" => (now.Date, now.Date.AddDays(1), "day", 1),
            "month" => (now.Date.AddDays(-29), now.Date.AddDays(1), "range", 30),
            "year" => (new DateTime(now.Year, now.Month, 1).AddMonths(-11), now.Date.AddDays(1), "year", 365),
            _ => (now.Date.AddDays(-6), now.Date.AddDays(1), "range", 7)
        };
    }

    private IQueryable<Models.Order> GoodOrders(DateTime from, DateTime toEx) =>
        db.Orders.Where(o => o.RestaurantId == CurrentRestaurantId && o.PlacedAt >= from && o.PlacedAt < toEx
                             && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);

    [RequirePerm(Perm.Reports)]
    [HttpGet("mine/report/sales")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<SalesSummaryDto> ReportSales(string period = "week", DateTime? from = null, DateTime? to = null)
    {
        var (start, toEx, kind, days) = ResolveRange(period, from, to);

        var cancelled = await db.Orders.CountAsync(o => o.RestaurantId == CurrentRestaurantId
            && o.PlacedAt >= start && o.PlacedAt < toEx
            && (o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Rejected));
        var orders = await GoodOrders(start, toEx).CountAsync();
        var revenue = await GoodOrders(start, toEx).SumAsync(o => (decimal?)o.Subtotal) ?? 0;

        var buckets = new List<MoneyBucketDto>();
        switch (kind)
        {
            case "day":
                var byHour = await GoodOrders(start, toEx).GroupBy(o => o.PlacedAt.Hour)
                    .Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(o => o.Subtotal) }).ToListAsync();
                for (var h = 0; h < 24; h++)
                {
                    var slot = byHour.FirstOrDefault(x => x.Key == h);
                    buckets.Add(new MoneyBucketDto($"{h:00}:00", slot?.Sum ?? 0, slot?.Count ?? 0));
                }
                break;
            case "year":
                var byMonth = await GoodOrders(start, toEx).GroupBy(o => new { o.PlacedAt.Year, o.PlacedAt.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count(), Sum = g.Sum(o => o.Subtotal) }).ToListAsync();
                for (var m = 0; m < 12; m++)
                {
                    var month = start.AddMonths(m);
                    var slot = byMonth.FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month);
                    buckets.Add(new MoneyBucketDto(month.ToString("MMMM yyyy"), slot?.Sum ?? 0, slot?.Count ?? 0));
                }
                break;
            default:
                var byDay = await GoodOrders(start, toEx).GroupBy(o => o.PlacedAt.Date)
                    .Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(o => o.Subtotal) }).ToListAsync();
                for (var d = 0; d < days; d++)
                {
                    var day = start.AddDays(d);
                    var slot = byDay.FirstOrDefault(x => x.Key == day);
                    buckets.Add(new MoneyBucketDto(day.ToString("ddd, d MMM"), slot?.Sum ?? 0, slot?.Count ?? 0));
                }
                break;
        }

        return new SalesSummaryDto(orders, revenue, orders == 0 ? 0 : Math.Round(revenue / orders, 3), cancelled, buckets);
    }

    [RequirePerm(Perm.Reports)]
    [HttpGet("mine/report/products")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ProductSalesPageDto> ReportProducts(string period = "week", string? search = null, int skip = 0, int take = 10,
        DateTime? from = null, DateTime? to = null)
    {
        take = Math.Clamp(take, 1, 50);
        skip = Math.Max(0, skip);
        var (start, toEx, _, _) = ResolveRange(period, from, to);

        var lines = db.OrderItems.Where(i => i.Order.RestaurantId == CurrentRestaurantId
            && i.Order.PlacedAt >= start && i.Order.PlacedAt < toEx
            && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.Rejected);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            lines = lines.Where(i => i.Name.Contains(term));
        }

        var grouped = lines.GroupBy(i => i.Name)
            .Select(g => new { Name = g.Key, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.UnitPrice * i.Quantity) });

        var total = await grouped.CountAsync();
        var page = await grouped.OrderByDescending(x => x.Qty).ThenBy(x => x.Name).Skip(skip).Take(take).ToListAsync();
        return new ProductSalesPageDto(total, page.Select(x => new ProductSalesDto(x.Name, x.Qty, x.Revenue)).ToList());
    }

    [RequirePerm(Perm.Reports)]
    [HttpGet("mine/report/customers")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<CustomerSalesPageDto> ReportCustomers(string period = "week", string? search = null, int skip = 0, int take = 10,
        DateTime? from = null, DateTime? to = null)
    {
        take = Math.Clamp(take, 1, 50);
        skip = Math.Max(0, skip);
        var (start, toEx, _, _) = ResolveRange(period, from, to);

        var scope = GoodOrders(start, toEx);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            scope = scope.Where(o => o.Customer.FullName.Contains(term));
        }

        var grouped = scope.GroupBy(o => new { o.CustomerId, o.Customer.FullName })
            .Select(g => new { g.Key.CustomerId, g.Key.FullName, Orders = g.Count(), Total = g.Sum(o => o.Subtotal) });

        var total = await grouped.CountAsync();
        var page = await grouped.OrderByDescending(x => x.Total).ThenBy(x => x.FullName).Skip(skip).Take(take).ToListAsync();

        // Order rows only for the customers on THIS page — the detail expansion stays cheap.
        var ids = page.Select(p => p.CustomerId).ToList();
        var rows = await GoodOrders(start, toEx).Where(o => ids.Contains(o.CustomerId))
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new { o.CustomerId, o.Number, o.PlacedAt, o.Status, o.Subtotal })
            .ToListAsync();

        var customers = page.Select(p => new CustomerSalesDto(p.FullName, p.Orders, p.Total,
            rows.Where(r => r.CustomerId == p.CustomerId).Take(15)
                .Select(r => new CustomerOrderRowDto(r.Number, r.PlacedAt, r.Status, r.Subtotal)).ToList())).ToList();
        return new CustomerSalesPageDto(total, customers);
    }

    // ---------- Receipt design (partner "Invoice designer") ----------

    [HttpGet("mine/receipt")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ReceiptDesignDto> GetReceiptDesign()
    {
        var d = await db.ReceiptDesigns.FirstOrDefaultAsync(r => r.RestaurantId == CurrentRestaurantId);
        return d is null
            ? ReceiptDesignDto.Default
            : new ReceiptDesignDto(d.LogoData, d.HeaderMessage, d.FooterMessage, d.Promo,
                d.Font, d.FontSize, d.PaperWidth, d.ShowBarcode, d.ShowVat, d.ShowCourier,
                d.Separator, d.TotalStyle, d.Spacing, d.LabelStyle, d.ShowQr, d.ShowAddress,
                d.HeaderStyle, d.ItemStyle, d.Frame, d.Ink, d.LogoSize, d.Stamp, d.Copies, d.QrLink,
                d.QrMode, d.QrCaption, d.QrSize);
    }

    [HttpPut("mine/receipt")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SaveReceiptDesign(ReceiptDesignDto req)
    {
        if (!string.IsNullOrEmpty(req.Logo))
        {
            if (!req.Logo.StartsWith("data:image/")) return BadRequest(new { message = "Only images are allowed for the logo." });
            if (req.Logo.Length > 200_000) return BadRequest(new { message = "The logo is too large." });
        }
        string[] fonts = ["mono", "sans", "serif", "cairo"];
        string[] sizes = ["small", "normal", "large"];

        var d = await db.ReceiptDesigns.FirstOrDefaultAsync(r => r.RestaurantId == CurrentRestaurantId);
        if (d is null)
        {
            d = new Models.ReceiptDesign { RestaurantId = CurrentRestaurantId };
            db.ReceiptDesigns.Add(d);
        }
        d.LogoData = string.IsNullOrEmpty(req.Logo) ? null : req.Logo;
        d.HeaderMessage = string.IsNullOrWhiteSpace(req.HeaderMessage) ? null : req.HeaderMessage.Trim()[..Math.Min(req.HeaderMessage.Trim().Length, 200)];
        d.FooterMessage = string.IsNullOrWhiteSpace(req.FooterMessage) ? null : req.FooterMessage.Trim()[..Math.Min(req.FooterMessage.Trim().Length, 300)];
        d.Promo = string.IsNullOrWhiteSpace(req.Promo) ? null : req.Promo.Trim()[..Math.Min(req.Promo.Trim().Length, 300)];
        d.Font = fonts.Contains(req.Font) ? req.Font : "mono";
        d.FontSize = sizes.Contains(req.FontSize) ? req.FontSize : "normal";
        d.PaperWidth = req.PaperWidth == 58 ? 58 : 80;
        d.ShowBarcode = req.ShowBarcode;
        d.ShowVat = req.ShowVat;
        d.ShowCourier = req.ShowCourier;
        string[] separators = ["dash", "dots", "stars", "solid"];
        string[] totalStyles = ["plain", "invert"];
        string[] spacings = ["compact", "normal", "relaxed"];
        string[] labelStyles = ["en", "bilingual"];
        d.Separator = separators.Contains(req.Separator) ? req.Separator : "dash";
        d.TotalStyle = totalStyles.Contains(req.TotalStyle) ? req.TotalStyle : "plain";
        d.Spacing = spacings.Contains(req.Spacing) ? req.Spacing : "normal";
        d.LabelStyle = labelStyles.Contains(req.LabelStyle) ? req.LabelStyle : "en";
        d.ShowQr = req.ShowQr;
        d.ShowAddress = req.ShowAddress;
        string[] headerStyles = ["normal", "big", "boxed"];
        string[] itemStyles = ["lines", "table"];
        string[] frames = ["none", "box", "double"];
        string[] inks = ["normal", "bold"];
        string[] logoSizes = ["small", "normal", "large"];
        string[] stamps = ["none", "auto"];
        d.HeaderStyle = headerStyles.Contains(req.HeaderStyle) ? req.HeaderStyle : "normal";
        d.ItemStyle = itemStyles.Contains(req.ItemStyle) ? req.ItemStyle : "lines";
        d.Frame = frames.Contains(req.Frame) ? req.Frame : "none";
        d.Ink = inks.Contains(req.Ink) ? req.Ink : "normal";
        d.LogoSize = logoSizes.Contains(req.LogoSize) ? req.LogoSize : "normal";
        d.Stamp = stamps.Contains(req.Stamp) ? req.Stamp : "none";
        d.Copies = req.Copies == 2 ? 2 : 1;
        var qrLink = req.QrLink?.Trim();
        if (!string.IsNullOrEmpty(qrLink) && !qrLink.StartsWith("http://") && !qrLink.StartsWith("https://"))
            qrLink = "https://" + qrLink;
        d.QrLink = string.IsNullOrEmpty(qrLink) ? null : qrLink[..Math.Min(qrLink.Length, 300)];
        string[] qrModes = ["verify", "store", "link"];
        d.QrMode = qrModes.Contains(req.QrMode) ? req.QrMode : "verify";
        d.QrSize = sizes.Contains(req.QrSize) ? req.QrSize : "normal";
        var qrCaption = req.QrCaption?.Trim();
        d.QrCaption = string.IsNullOrEmpty(qrCaption) ? null : qrCaption[..Math.Min(qrCaption.Length, 80)];
        d.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Marks one gallery photo as the main — it leads the customer slideshow.</summary>
    [HttpPost("mine/photos/{photoId:int}/main")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> SetMainPhoto(int photoId)
    {
        var photos = await db.RestaurantPhotos.Where(p => p.RestaurantId == CurrentRestaurantId).ToListAsync();
        var target = photos.FirstOrDefault(p => p.Id == photoId);
        if (target is null) return NotFound();
        foreach (var p in photos) p.IsMain = p.Id == photoId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("mine/photos/{photoId:int}")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> DeletePhoto(int photoId)
    {
        var photo = await db.RestaurantPhotos.FirstOrDefaultAsync(
            p => p.Id == photoId && p.RestaurantId == CurrentRestaurantId);
        if (photo is null) return NotFound();
        db.RestaurantPhotos.Remove(photo);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Product → printer routing (consumed by the shop till / LocalHandler) ----------

    /// <summary>Which print station each product's kitchen ticket goes to.</summary>
    [HttpGet("mine/print-routes")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<List<PrintRouteDto>> PrintRoutesGet() =>
        await db.PrintRoutes.Where(p => p.RestaurantId == CurrentRestaurantId)
            .Select(p => new PrintRouteDto(p.MenuItemId, p.Station))
            .ToListAsync();

    /// <summary>Replaces the whole routing map — the page always saves the full picture.</summary>
    [HttpPut("mine/print-routes")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<IActionResult> PrintRoutesSave(List<PrintRouteDto> routes)
    {
        var mine = CurrentRestaurantId;
        db.PrintRoutes.RemoveRange(db.PrintRoutes.Where(p => p.RestaurantId == mine));
        foreach (var route in routes.Where(r => r.Station is "kitchen" or "bar"))
            db.PrintRoutes.Add(new Models.PrintRoute { RestaurantId = mine, MenuItemId = route.MenuItemId, Station = route.Station });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return Math.Round(R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)), 1);
    }

    private async Task<Dictionary<int, List<Models.RestaurantHours>>> HoursFor(List<int> restaurantIds) =>
        (await db.RestaurantHours.Where(h => restaurantIds.Contains(h.RestaurantId)).ToListAsync())
        .GroupBy(h => h.RestaurantId)
        .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// The extra cuisine names for one page of stores, fetched in a single query the way
    /// ratings and hours are. Most stores have none and simply miss from the dictionary.
    /// </summary>
    private async Task<Dictionary<int, List<string>>> ExtraCuisinesFor(List<int> restaurantIds) =>
        (await db.RestaurantCuisines
            .Where(rc => restaurantIds.Contains(rc.RestaurantId))
            .Select(rc => new { rc.RestaurantId, rc.Cuisine.Name })
            .ToListAsync())
        .GroupBy(x => x.RestaurantId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

    private async Task<Dictionary<int, (double avg, int count)>> RatingsFor(List<int> restaurantIds) =>
        (await db.Reviews.Where(r => restaurantIds.Contains(r.RestaurantId))
            .GroupBy(r => r.RestaurantId)
            .Select(g => new { g.Key, Avg = g.Average(x => (double)x.RestaurantRating), Count = g.Count() })
            .ToListAsync())
        .ToDictionary(x => x.Key, x => (Math.Round(x.Avg, 1), x.Count));
}
