using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// Creates the database if missing and fills it with a believable demo world:
/// four approved restaurants with menus, one awaiting approval, three drivers,
/// customers with addresses, coupons, two weeks of delivered orders (with reviews)
/// and a handful of live orders in every stage of the pipeline.
/// All demo passwords are Pas_123.
/// </summary>
public static class DbSeeder
{
    /// <param name="bigData">
    /// When true (the live server), a large generated world is layered on top of the
    /// hand-crafted one: ~20 more restaurants with menus, dozens of customers and
    /// drivers, and ~90 days of order history. Tests run with false so their
    /// assertions stay deterministic.
    /// </param>
    public static async Task SeedAsync(AppDbContext db, bool bigData = false)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;

        var hash = PasswordHasher.Hash("Pas_123");
        var now = DateTime.Now;

        // ---------- Cuisines ----------
        var cuisines = new[]
        {
            new Cuisine { Name = "Pizza & Italian", Emoji = "🍕" },
            new Cuisine { Name = "Burgers", Emoji = "🍔" },
            new Cuisine { Name = "Arabic & Grill", Emoji = "🌯" },
            new Cuisine { Name = "Japanese", Emoji = "🍣" },
            new Cuisine { Name = "Indian", Emoji = "🍛" },
            new Cuisine { Name = "Desserts", Emoji = "🍰" },
            new Cuisine { Name = "Coffee & Juice", Emoji = "☕" },
            new Cuisine { Name = "Healthy", Emoji = "🥗" }
        };
        db.Cuisines.AddRange(cuisines);

        // ---------- Users ----------
        User NewUser(string name, string email, string phone, UserRole role, int daysAgo = 120) => new()
        {
            FullName = name, Email = email, Phone = phone, PasswordHash = hash,
            Role = role, IsActive = true, CreatedAt = now.AddDays(-daysAgo)
        };

        var admin = NewUser("Majid Al Balushi", "admin@majidfood.com", "+968 9100 0001", UserRole.Administrator, 400);

        var marco = NewUser("Marco Rossi", "marco@majidfood.com", "+968 9210 1001", UserRole.RestaurantOwner, 300);
        var sara = NewUser("Sara Al Habsi", "sara@majidfood.com", "+968 9210 1002", UserRole.RestaurantOwner, 280);
        var khalid = NewUser("Khalid Al Farsi", "khalid@majidfood.com", "+968 9210 1003", UserRole.RestaurantOwner, 250);
        var yuki = NewUser("Yuki Tanaka", "yuki@majidfood.com", "+968 9210 1004", UserRole.RestaurantOwner, 200);
        var raj = NewUser("Raj Patel", "raj@majidfood.com", "+968 9210 1005", UserRole.RestaurantOwner, 3);

        var salim = NewUser("Salim Al Amri", "salim.driver@majidfood.com", "+968 9310 2001", UserRole.Driver, 180);
        var hassan = NewUser("Hassan Al Zadjali", "hassan.driver@majidfood.com", "+968 9310 2002", UserRole.Driver, 150);
        var ali = NewUser("Ali Al Riyami", "ali.driver@majidfood.com", "+968 9310 2003", UserRole.Driver, 90);

        var majed = NewUser("Demo Customer", "customer@majidfood.com", "+968 9410 3001", UserRole.Customer, 200);
        var ahmed = NewUser("Ahmed Al Lawati", "ahmed@majidfood.com", "+968 9410 3002", UserRole.Customer, 170);
        var fatima = NewUser("Fatima Al Busaidi", "fatima@majidfood.com", "+968 9410 3003", UserRole.Customer, 140);
        var mariam = NewUser("Mariam Al Hinai", "mariam@majidfood.com", "+968 9410 3004", UserRole.Customer, 60);

        db.Users.AddRange(admin, marco, sara, khalid, yuki, raj, salim, hassan, ali, majed, ahmed, fatima, mariam);

        db.DriverProfiles.AddRange(
            new DriverProfile { User = salim, VehicleType = VehicleType.Motorbike, IsOnline = true, Verification = DriverVerificationStatus.Approved },
            new DriverProfile { User = hassan, VehicleType = VehicleType.Car, IsOnline = true, Verification = DriverVerificationStatus.Approved },
            new DriverProfile { User = ali, VehicleType = VehicleType.Bicycle, IsOnline = false, Verification = DriverVerificationStatus.Approved });

        // ---------- Addresses ----------
        var majedHome = new Address { User = majed, Label = "Home", Area = "Al Khuwair", Street = "Al Khuwair Street 33", Building = "Villa 118", Notes = "White gate, ring twice" };
        var majedWork = new Address { User = majed, Label = "Office", Area = "Ruwi", Street = "Markaz Mutrah Al Tijari", Building = "Bldg 77, Floor 4" };
        var ahmedHome = new Address { User = ahmed, Label = "Home", Area = "Al Ghubra", Street = "18th November Street", Building = "Flat 22, Al Noor Tower" };
        var fatimaHome = new Address { User = fatima, Label = "Home", Area = "Qurum", Street = "Qurum Heights Road", Building = "Villa 9" };
        var mariamHome = new Address { User = mariam, Label = "Home", Area = "Al Mouj", Street = "Marina Walk", Building = "Apt 305, Lagoon Bldg", Notes = "Leave at reception" };
        db.Addresses.AddRange(majedHome, majedWork, ahmedHome, fatimaHome, mariamHome);

        // ---------- Restaurants + menus ----------
        var bella = new Restaurant
        {
            Owner = marco, Name = "Bella Napoli", Description = "Wood-fired Neapolitan pizza and fresh pasta, straight from the oven.",
            Cuisine = cuisines[0], LogoEmoji = "🍕", BannerColor = "#FFE3D3", Area = "Al Khuwair", Street = "Dohat Al Adab Street",
            Phone = "+968 2420 1111", DeliveryFee = 0.500m, MinOrder = 2.000m, AvgPrepMinutes = 25,
            IsOpen = true, IsApproved = true, CommissionPercent = 15m, CreatedAt = now.AddDays(-300)
        };
        var burger = new Restaurant
        {
            Owner = sara, Name = "Burger Bros", Description = "Smashed-to-order beef and chicken burgers with secret bros sauce.",
            Cuisine = cuisines[1], LogoEmoji = "🍔", BannerColor = "#FFF1C9", Area = "Al Ghubra", Street = "Beach Road",
            Phone = "+968 2420 2222", DeliveryFee = 0.400m, MinOrder = 1.500m, AvgPrepMinutes = 18,
            IsOpen = true, IsApproved = true, CommissionPercent = 15m, CreatedAt = now.AddDays(-280)
        };
        var shawarma = new Restaurant
        {
            Owner = khalid, Name = "Shawarma House", Description = "Authentic Levantine shawarma, grills and fresh saj wraps.",
            Cuisine = cuisines[2], LogoEmoji = "🌯", BannerColor = "#E4F2DC", Area = "Ruwi", Street = "Souq Ruwi Street",
            Phone = "+968 2420 3333", DeliveryFee = 0.300m, MinOrder = 1.000m, AvgPrepMinutes = 15,
            IsOpen = true, IsApproved = true, CommissionPercent = 12m, CreatedAt = now.AddDays(-250)
        };
        var sakura = new Restaurant
        {
            Owner = yuki, Name = "Sakura Sushi", Description = "Hand-rolled sushi and ramen made with fish flown in daily.",
            Cuisine = cuisines[3], LogoEmoji = "🍣", BannerColor = "#FCE1EA", Area = "Al Mouj", Street = "Marina Promenade",
            Phone = "+968 2420 4444", DeliveryFee = 0.700m, MinOrder = 3.000m, AvgPrepMinutes = 30,
            IsOpen = true, IsApproved = true, CommissionPercent = 18m, CreatedAt = now.AddDays(-200)
        };
        var curry = new Restaurant
        {
            Owner = raj, Name = "Curry Corner", Description = "Home-style North Indian curries, biryani and tandoor breads.",
            Cuisine = cuisines[4], LogoEmoji = "🍛", BannerColor = "#FFE9C7", Area = "Qurum", Street = "Commercial Area Road",
            Phone = "+968 2420 5555", DeliveryFee = 0.400m, MinOrder = 2.000m, AvgPrepMinutes = 22,
            IsOpen = false, IsApproved = false, CommissionPercent = 15m, CreatedAt = now.AddDays(-3)
        };
        db.Restaurants.AddRange(bella, burger, shawarma, sakura, curry);

        MenuItem Item(Restaurant r, MenuCategory c, string name, string desc, decimal price, string emoji, bool popular = false, bool available = true) =>
            new() { Restaurant = r, Category = c, Name = name, Description = desc, Price = price, ImageEmoji = emoji, IsPopular = popular, IsAvailable = available };

        MenuCategory Cat(Restaurant r, string name, int sort) => new() { Restaurant = r, Name = name, SortOrder = sort };

        // Bella Napoli
        var bPizza = Cat(bella, "Pizzas", 1);
        var bPasta = Cat(bella, "Pasta", 2);
        var bSides = Cat(bella, "Sides & Drinks", 3);
        db.MenuCategories.AddRange(bPizza, bPasta, bSides);
        db.MenuItems.AddRange(
            Item(bella, bPizza, "Margherita", "San Marzano tomatoes, fior di latte, basil", 2.800m, "🍕", popular: true),
            Item(bella, bPizza, "Pepperoni", "Double pepperoni, mozzarella, oregano", 3.400m, "🍕", popular: true),
            Item(bella, bPizza, "Quattro Formaggi", "Mozzarella, gorgonzola, parmesan, provolone", 3.800m, "🧀"),
            Item(bella, bPizza, "Diavola", "Spicy salami, chili oil, mozzarella", 3.600m, "🌶️"),
            Item(bella, bPasta, "Spaghetti Carbonara", "Guanciale, pecorino, egg yolk", 3.200m, "🍝", popular: true),
            Item(bella, bPasta, "Penne Arrabbiata", "Tomato, garlic, chili", 2.600m, "🍝"),
            Item(bella, bPasta, "Lasagna al Forno", "Slow-cooked beef ragù, béchamel", 3.500m, "🥘"),
            Item(bella, bSides, "Garlic Bread", "Wood-oven baked, herb butter", 1.200m, "🥖"),
            Item(bella, bSides, "Tiramisu", "Classic, made in-house", 1.800m, "🍰"),
            Item(bella, bSides, "Italian Lemonade", "Sparkling, fresh mint", 0.900m, "🍋"));

        // Burger Bros
        var gBurgers = Cat(burger, "Burgers", 1);
        var gChicken = Cat(burger, "Chicken", 2);
        var gSides = Cat(burger, "Fries & Shakes", 3);
        db.MenuCategories.AddRange(gBurgers, gChicken, gSides);
        db.MenuItems.AddRange(
            Item(burger, gBurgers, "Classic Smash", "Double smashed patty, american cheese, bros sauce", 2.200m, "🍔", popular: true),
            Item(burger, gBurgers, "Truffle Bro", "Truffle mayo, swiss cheese, crispy onions", 2.900m, "🍔", popular: true),
            Item(burger, gBurgers, "BBQ Bacon (Beef)", "Beef bacon, cheddar, smoky BBQ", 2.700m, "🥓"),
            Item(burger, gBurgers, "Mushroom Melt", "Sautéed mushrooms, melted swiss", 2.500m, "🍄"),
            Item(burger, gChicken, "Crispy Chicken", "Buttermilk fried thigh, slaw, pickles", 2.100m, "🍗", popular: true),
            Item(burger, gChicken, "Nashville Hot", "Fiery glaze, ranch, pickles", 2.300m, "🔥"),
            Item(burger, gChicken, "Grilled Chicken", "Marinated breast, avocado, honey mustard", 2.400m, "🥑"),
            Item(burger, gSides, "Loaded Fries", "Cheese sauce, jalapeños, bros sauce", 1.400m, "🍟", popular: true),
            Item(burger, gSides, "Classic Fries", "Sea salt, skin-on", 0.800m, "🍟"),
            Item(burger, gSides, "Oreo Shake", "Thick vanilla shake, crushed oreo", 1.500m, "🥤"));

        // Shawarma House
        var sWraps = Cat(shawarma, "Shawarma", 1);
        var sGrills = Cat(shawarma, "Grills", 2);
        var sExtras = Cat(shawarma, "Extras", 3);
        db.MenuCategories.AddRange(sWraps, sGrills, sExtras);
        db.MenuItems.AddRange(
            Item(shawarma, sWraps, "Chicken Shawarma", "Garlic toum, pickles, saj bread", 0.800m, "🌯", popular: true),
            Item(shawarma, sWraps, "Beef Shawarma", "Tahini, onion, parsley, sumac", 1.000m, "🌯", popular: true),
            Item(shawarma, sWraps, "Shawarma Plate", "Choice of meat, rice, salads, sauces", 2.500m, "🍛"),
            Item(shawarma, sWraps, "Family Box", "10 mixed mini shawarmas + fries", 4.500m, "📦"),
            Item(shawarma, sGrills, "Mixed Grill", "Shish tawook, kofta, lamb kebab", 4.200m, "🍢", popular: true),
            Item(shawarma, sGrills, "Shish Tawook", "Charcoal-grilled, garlic sauce", 2.800m, "🍢"),
            Item(shawarma, sGrills, "Lamb Kofta", "Spiced, grilled over coals", 3.000m, "🥩"),
            Item(shawarma, sExtras, "Hummus", "Silky, olive oil, warm bread", 0.700m, "🥣"),
            Item(shawarma, sExtras, "Fattoush", "Crispy bread, pomegranate dressing", 0.900m, "🥗"),
            Item(shawarma, sExtras, "Fresh Laban", "Chilled, lightly salted", 0.400m, "🥛"));

        // Sakura Sushi
        var kRolls = Cat(sakura, "Signature Rolls", 1);
        var kNigiri = Cat(sakura, "Nigiri & Sashimi", 2);
        var kHot = Cat(sakura, "Hot Kitchen", 3);
        db.MenuCategories.AddRange(kRolls, kNigiri, kHot);
        db.MenuItems.AddRange(
            Item(sakura, kRolls, "Dragon Roll", "Eel, avocado, tobiko, unagi glaze (8 pc)", 4.800m, "🐉", popular: true),
            Item(sakura, kRolls, "Salmon Crunch", "Salmon, tempura flakes, spicy mayo (8 pc)", 4.200m, "🍣", popular: true),
            Item(sakura, kRolls, "California Roll", "Crab, avocado, cucumber (8 pc)", 3.500m, "🥑"),
            Item(sakura, kRolls, "Veggie Garden", "Avocado, cucumber, pickled radish (8 pc)", 2.800m, "🥒"),
            Item(sakura, kNigiri, "Salmon Nigiri", "2 pieces, fresh cut", 1.600m, "🍣", popular: true),
            Item(sakura, kNigiri, "Tuna Nigiri", "2 pieces, bluefin", 2.000m, "🍣"),
            Item(sakura, kNigiri, "Sashimi Platter", "12 pieces, chef's selection", 6.500m, "🐟"),
            Item(sakura, kHot, "Chicken Ramen", "Rich broth, ajitama egg, nori", 3.800m, "🍜", popular: true),
            Item(sakura, kHot, "Beef Gyoza", "6 pan-fried dumplings", 2.200m, "🥟"),
            Item(sakura, kHot, "Chicken Katsu", "Panko-crusted, tonkatsu sauce, rice", 3.600m, "🍱"));

        // Curry Corner (pending approval — menu ready for day one)
        var cMains = Cat(curry, "Curries", 1);
        var cBiryani = Cat(curry, "Biryani & Breads", 2);
        db.MenuCategories.AddRange(cMains, cBiryani);
        db.MenuItems.AddRange(
            Item(curry, cMains, "Butter Chicken", "Creamy tomato gravy, kasuri methi", 2.900m, "🍛", popular: true),
            Item(curry, cMains, "Palak Paneer", "Cottage cheese, spinach gravy", 2.400m, "🥬"),
            Item(curry, cMains, "Rogan Josh", "Slow-cooked lamb, Kashmiri spices", 3.400m, "🍖"),
            Item(curry, cBiryani, "Chicken Biryani", "Dum-style, saffron rice, raita", 2.800m, "🍚", popular: true),
            Item(curry, cBiryani, "Garlic Naan", "Tandoor-baked, butter brushed", 0.500m, "🫓"));

        // ---------- Coupons ----------
        db.Coupons.AddRange(
            new Coupon { Code = "WELCOME10", Percent = 10m, MinOrder = 2.000m, ExpiresAt = null, IsActive = true, MaxUses = 1000, Uses = 46 },
            new Coupon { Code = "SAVE20", Percent = 20m, MinOrder = 5.000m, ExpiresAt = now.AddDays(30), IsActive = true, MaxUses = 500, Uses = 112 },
            new Coupon { Code = "EID15", Percent = 15m, MinOrder = 3.000m, ExpiresAt = now.AddDays(-10), IsActive = false, MaxUses = 300, Uses = 300 });

        await db.SaveChangesAsync();

        // ---------- Orders ----------
        // Two weeks of delivered history plus live orders in every stage, so each app
        // opens onto real data. Deterministic random keeps reseeded databases identical.
        var rnd = new Random(7);
        var number = 1000;
        var customers = new[] { (majed, majedHome), (majed, majedWork), (ahmed, ahmedHome), (fatima, fatimaHome), (mariam, mariamHome) };
        var restaurants = new[] { bella, burger, shawarma, sakura };
        var drivers = new[] { salim, hassan, ali };
        var comments = new[]
        {
            "Amazing food, arrived hot!", "Really tasty, will order again.", "Good but delivery took a while.",
            "Best in Muscat, hands down.", "Generous portions, great value.", null, null, ""
        };

        Order MakeOrder(User cust, Address addr, Restaurant rest, User? drv, DateTime placedAt, OrderStatus status, string? coupon = null)
        {
            var items = new List<OrderItem>();
            var menu = db.MenuItems.Local.Where(m => m.Restaurant == rest).ToList();
            var count = rnd.Next(1, 4);
            for (var i = 0; i < count; i++)
            {
                var dish = menu[rnd.Next(menu.Count)];
                var existing = items.FirstOrDefault(x => x.Name == dish.Name);
                if (existing is not null) { existing.Quantity++; continue; }
                items.Add(new OrderItem { MenuItemId = dish.Id, Name = dish.Name, UnitPrice = dish.Price, Quantity = rnd.Next(1, 3) });
            }

            var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);
            var discount = 0m;
            if (coupon == "WELCOME10" && subtotal >= 2m) discount = Math.Round(subtotal * 0.10m, 3);

            var order = new Order
            {
                Number = $"MF-{++number}",
                Customer = cust, Restaurant = rest, Driver = drv,
                Status = status, PaymentMethod = (PaymentMethod)rnd.Next(0, 2),
                DeliveryAddress = $"{addr.Label} — {addr.Building}, {addr.Street}, {addr.Area}",
                Subtotal = subtotal, DeliveryFee = rest.DeliveryFee, ServiceFee = Pricing.ServiceFee,
                Discount = discount, Total = subtotal + rest.DeliveryFee + Pricing.ServiceFee - discount,
                CouponCode = coupon, EstimatedMinutes = Pricing.EstimateMinutes(rest.AvgPrepMinutes),
                PlacedAt = placedAt, Items = items
            };

            // Timeline: each stage a few minutes after the last.
            var t = placedAt;
            void Ev(OrderStatus s, string by, int plusMin)
            {
                t = t.AddMinutes(plusMin);
                order.Events.Add(new OrderEvent { Status = s, At = t, By = by });
            }
            Ev(OrderStatus.Pending, cust.FullName, 0);
            if (status is OrderStatus.Cancelled) { Ev(OrderStatus.Cancelled, cust.FullName, 4); order.CancelReason = "Changed my mind"; return order; }
            if (status is OrderStatus.Rejected) { Ev(OrderStatus.Rejected, rest.Name, 5); order.CancelReason = "Kitchen at full capacity"; return order; }
            if (status == OrderStatus.Pending) return order;
            Ev(OrderStatus.Accepted, rest.Name, rnd.Next(2, 6));
            if (status == OrderStatus.Accepted) return order;
            Ev(OrderStatus.Preparing, rest.Name, rnd.Next(1, 4));
            if (status == OrderStatus.Preparing) return order;
            Ev(OrderStatus.Ready, rest.Name, rnd.Next(8, rest.AvgPrepMinutes + 5));
            if (status == OrderStatus.Ready) return order;
            Ev(OrderStatus.PickedUp, drv!.FullName, rnd.Next(3, 9));
            if (status == OrderStatus.PickedUp) return order;
            Ev(OrderStatus.OnTheWay, drv.FullName, rnd.Next(1, 3));
            if (status == OrderStatus.OnTheWay) return order;
            Ev(OrderStatus.Delivered, drv.FullName, rnd.Next(8, 18));
            order.DeliveredAt = t;
            return order;
        }

        // Delivered history — ~26 orders over the last 14 days.
        for (var day = 14; day >= 1; day--)
        {
            var perDay = rnd.Next(1, 4);
            for (var i = 0; i < perDay; i++)
            {
                var (cust, addr) = customers[rnd.Next(customers.Length)];
                var rest = restaurants[rnd.Next(restaurants.Length)];
                var drv = drivers[rnd.Next(drivers.Length)];
                var placed = now.Date.AddDays(-day).AddHours(rnd.Next(11, 22)).AddMinutes(rnd.Next(0, 60));
                var order = MakeOrder(cust, addr, rest, drv, placed, OrderStatus.Delivered, rnd.Next(4) == 0 ? "WELCOME10" : null);
                db.Orders.Add(order);

                if (rnd.Next(3) != 0)
                {
                    var comment = comments[rnd.Next(comments.Length)];
                    db.Reviews.Add(new Review
                    {
                        Order = order, RestaurantId = 0, CustomerId = 0, // fixed up after save below
                        RestaurantRating = rnd.Next(3, 6), DriverRating = rnd.Next(4, 6),
                        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
                        CreatedAt = order.DeliveredAt!.Value.AddHours(rnd.Next(1, 20))
                    });
                }
            }
        }

        // One of each terminal edge case for realism.
        db.Orders.Add(MakeOrder(ahmed, ahmedHome, sakura, null, now.AddDays(-6).Date.AddHours(19), OrderStatus.Rejected));
        db.Orders.Add(MakeOrder(fatima, fatimaHome, bella, null, now.AddDays(-4).Date.AddHours(13), OrderStatus.Cancelled));

        // Live pipeline right now — one order at every active stage.
        db.Orders.Add(MakeOrder(majed, majedHome, bella, null, now.AddMinutes(-3), OrderStatus.Pending));
        db.Orders.Add(MakeOrder(ahmed, ahmedHome, burger, null, now.AddMinutes(-12), OrderStatus.Preparing));
        db.Orders.Add(MakeOrder(fatima, fatimaHome, shawarma, null, now.AddMinutes(-25), OrderStatus.Ready));
        db.Orders.Add(MakeOrder(mariam, mariamHome, sakura, hassan, now.AddMinutes(-35), OrderStatus.OnTheWay));

        // A few hearts for the hand-crafted world.
        db.Favorites.AddRange(
            new FavoriteRestaurant { User = majed, Restaurant = bella },
            new FavoriteRestaurant { User = majed, Restaurant = sakura },
            new FavoriteRestaurant { User = fatima, Restaurant = shawarma });

        // Template TEST Visa for every customer — ready for one-tap online payment.
        foreach (var customer in new[] { majed, ahmed, fatima, mariam })
            db.SavedCards.Add(new SavedCard
            {
                User = customer, Brand = "Visa", HolderName = customer.FullName,
                Last4 = "4242", ExpMonth = 12, ExpYear = now.Year + 3
            });

        await db.SaveChangesAsync();

        if (bigData)
            await SeedBigWorldAsync(db, hash, now);

        // Reviews were added before order FKs existed — point them at the right rows now.
        foreach (var review in db.Reviews.Local)
        {
            review.RestaurantId = review.Order.RestaurantId;
            review.CustomerId = review.Order.CustomerId;
            review.DriverUserId = review.Order.DriverUserId;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Layers a big generated world on top of the hand-crafted one: 20 restaurants with
    /// menus, 50 customers, 10 drivers and ~90 days of orders with reviews. Deterministic
    /// (fixed random seed) so reseeded databases are identical.
    /// </summary>
    private static async Task SeedBigWorldAsync(AppDbContext db, string hash, DateTime now)
    {
        var rnd = new Random(99);
        var areas = new[] { "Al Khuwair", "Qurum", "Ruwi", "Al Mouj", "Al Ghubra", "Madinat Sultan Qaboos", "Bousher", "Seeb", "Al Amerat", "Muttrah" };
        var firstNames = new[] { "Said", "Nasser", "Talal", "Hamad", "Yousuf", "Ibrahim", "Sultan", "Waleed", "Aisha", "Laila", "Noor", "Huda", "Salma", "Zainab", "Rania", "Amal", "Badr", "Faisal", "Muna", "Asma" };
        var lastNames = new[] { "Al Balushi", "Al Habsi", "Al Farsi", "Al Amri", "Al Riyami", "Al Hinai", "Al Busaidi", "Al Zadjali", "Al Lawati", "Al Kindi", "Al Maskari", "Al Rawahi" };

        var cuisines = db.Cuisines.Local.OrderBy(c => c.Id).ToList();
        int CuisineIdx(string name) => cuisines.FindIndex(c => c.Name == name);

        // (name, emoji, banner, cuisine index, categories, dish pool)
        var dishPools = new Dictionary<string, (string[] categories, (string name, string desc, string emoji, decimal price)[] dishes)>
        {
            ["Pizza & Italian"] = (new[] { "Pizzas", "Pasta & Mains", "Sides & Drinks" }, new (string, string, string, decimal)[]
            {
                ("Margherita Classica", "Tomato, mozzarella, basil", "🍕", 2.6m), ("Pepperoni Piccante", "Spicy pepperoni, mozzarella", "🍕", 3.2m),
                ("Funghi Trifolati", "Mushrooms, thyme, taleggio", "🍄", 3.4m), ("Prosciutto e Rucola", "Cured beef, rocket, parmesan", "🥓", 3.9m),
                ("Spaghetti Pomodoro", "Slow tomato sauce, basil", "🍝", 2.7m), ("Fettuccine Alfredo", "Cream, parmesan, chicken", "🍝", 3.3m),
                ("Risotto Milanese", "Saffron, butter, parmesan", "🍚", 3.8m), ("Chicken Parmigiana", "Crumbed chicken, napoli, mozzarella", "🍗", 3.9m),
                ("Bruschetta", "Grilled bread, tomato, garlic", "🥖", 1.3m), ("Panna Cotta", "Vanilla bean, berry coulis", "🍮", 1.6m),
                ("San Pellegrino", "Sparkling water 500ml", "🫧", 0.7m), ("Affogato", "Espresso over gelato", "☕", 1.4m)
            }),
            ["Burgers"] = (new[] { "Beef Burgers", "Chicken & More", "Fries & Shakes" }, new (string, string, string, decimal)[]
            {
                ("Double Smash", "Two smashed patties, house sauce", "🍔", 2.4m), ("Cheese Royale", "Aged cheddar, caramelised onion", "🍔", 2.6m),
                ("Spicy Diablo", "Jalapeño, sriracha mayo", "🌶️", 2.7m), ("Mushroom Swiss", "Sautéed mushrooms, swiss", "🍄", 2.8m),
                ("Crispy Zinger", "Buttermilk fried chicken, slaw", "🍗", 2.2m), ("Grilled Cajun Chicken", "Cajun rub, avocado", "🥑", 2.5m),
                ("Beef Bacon Stack", "Beef bacon, BBQ glaze", "🥓", 3.0m), ("Truffle Fries", "Truffle oil, parmesan", "🍟", 1.6m),
                ("Cheese Fries", "Cheese sauce, chives", "🍟", 1.3m), ("Vanilla Shake", "Real vanilla, whipped cream", "🥤", 1.4m),
                ("Chocolate Shake", "Belgian chocolate", "🥤", 1.5m), ("Onion Rings", "Beer-battered, ranch dip", "🧅", 1.2m)
            }),
            ["Arabic & Grill"] = (new[] { "Shawarma & Wraps", "Charcoal Grills", "Mezze & Drinks" }, new (string, string, string, decimal)[]
            {
                ("Chicken Shawarma Saj", "Garlic toum, pickles", "🌯", 0.9m), ("Beef Shawarma Plate", "Rice, tahini, salad", "🍛", 2.4m),
                ("Mixed Grill Platter", "Tawook, kofta, kebab", "🍢", 4.4m), ("Lamb Chops", "Charcoal grilled, sumac onion", "🥩", 5.2m),
                ("Shish Tawook", "Saffron marinade, toum", "🍢", 2.9m), ("Kofta Meshwi", "Spiced lamb, grilled tomato", "🥩", 3.1m),
                ("Hummus Beiruti", "Chickpeas, chili, parsley", "🥣", 0.8m), ("Moutabal", "Smoked eggplant, tahini", "🍆", 0.9m),
                ("Fattoush", "Crispy bread, pomegranate", "🥗", 1.0m), ("Warak Enab", "Stuffed vine leaves", "🍃", 1.2m),
                ("Fresh Lemon Mint", "Blended, ice cold", "🍋", 0.8m), ("Mandi Chicken Half", "Smoked rice, dakous", "🍗", 2.8m)
            }),
            ["Japanese"] = (new[] { "Sushi Rolls", "Nigiri & Sashimi", "Hot Dishes" }, new (string, string, string, decimal)[]
            {
                ("Spicy Tuna Roll", "Tuna, togarashi mayo (8pc)", "🍣", 4.4m), ("Ebi Tempura Roll", "Crispy prawn, avocado (8pc)", "🍤", 4.6m),
                ("Rainbow Roll", "Assorted fish over california (8pc)", "🌈", 5.2m), ("Avocado Maki", "Simple and fresh (6pc)", "🥑", 2.4m),
                ("Salmon Nigiri Pair", "Fresh cut, 2 pieces", "🍣", 1.7m), ("Tuna Sashimi", "6 slices, bluefin", "🐟", 4.8m),
                ("Chicken Teriyaki Don", "Glazed chicken, steamed rice", "🍱", 3.7m), ("Beef Yakisoba", "Stir-fried noodles", "🍜", 3.9m),
                ("Miso Soup", "Tofu, wakame, spring onion", "🥣", 0.9m), ("Edamame", "Sea salt, steamed", "🫛", 1.1m),
                ("Tonkotsu Ramen", "Rich broth, ajitama egg", "🍜", 4.1m), ("Katsu Curry", "Panko chicken, curry rice", "🍛", 3.9m)
            }),
            ["Indian"] = (new[] { "Curries", "Biryani & Rice", "Breads & Sides" }, new (string, string, string, decimal)[]
            {
                ("Butter Chicken", "Creamy tomato, kasuri methi", "🍛", 2.9m), ("Chicken Tikka Masala", "Charred tikka, spiced gravy", "🍛", 3.0m),
                ("Palak Paneer", "Spinach, cottage cheese", "🥬", 2.5m), ("Dal Tadka", "Yellow lentils, ghee tempering", "🫘", 1.8m),
                ("Rogan Josh", "Kashmiri lamb curry", "🍖", 3.5m), ("Chicken Biryani", "Dum-style, saffron, raita", "🍚", 2.9m),
                ("Mutton Biryani", "Slow-cooked, burani", "🍚", 3.6m), ("Veg Biryani", "Seasonal vegetables, mint", "🥕", 2.3m),
                ("Garlic Naan", "Tandoor baked, butter", "🫓", 0.5m), ("Cheese Naan", "Stuffed, melty", "🫓", 0.8m),
                ("Samosa (2pc)", "Spiced potato, chutney", "🥟", 0.7m), ("Mango Lassi", "Alphonso mango, yogurt", "🥭", 1.0m)
            }),
            ["Desserts"] = (new[] { "Signature Sweets", "Cakes", "Cold Treats" }, new (string, string, string, decimal)[]
            {
                ("Kunafa Nabulsia", "Crispy, cheese, orange blossom", "🧁", 1.8m), ("Umm Ali", "Warm, pistachio, cream", "🥧", 1.4m),
                ("Basbousa", "Semolina, rose syrup", "🍯", 1.0m), ("Molten Lava Cake", "Warm chocolate centre", "🍫", 1.9m),
                ("San Sebastian Cheesecake", "Burnt top, silky centre", "🍰", 2.2m), ("Tres Leches", "Milk-soaked sponge", "🍰", 1.9m),
                ("Pistachio Cake Slice", "Layers of pistachio cream", "🍰", 2.1m), ("Gelato Duo", "Two scoops, choice of flavour", "🍨", 1.3m),
                ("Affogato Sundae", "Espresso, vanilla gelato", "🍧", 1.6m), ("Fruit Trifle", "Layered cream and fruit", "🍓", 1.5m),
                ("Luqaimat (8pc)", "Crispy dumplings, date syrup", "🍡", 1.1m), ("Karak Chai", "Spiced, sweet", "☕", 0.4m)
            }),
            ["Coffee & Juice"] = (new[] { "Coffee", "Fresh Juices", "Light Bites" }, new (string, string, string, decimal)[]
            {
                ("Flat White", "Double shot, silky milk", "☕", 1.4m), ("Spanish Latte", "Condensed milk, espresso", "☕", 1.6m),
                ("Iced Caramel Macchiato", "Vanilla, caramel drizzle", "🧋", 1.8m), ("V60 Pour Over", "Single origin, floral", "☕", 1.9m),
                ("Fresh Orange", "Squeezed to order", "🍊", 1.2m), ("Mango Avocado Smoothie", "Thick and creamy", "🥭", 1.7m),
                ("Green Detox", "Cucumber, celery, apple", "🥒", 1.6m), ("Watermelon Mint Cooler", "Summer favourite", "🍉", 1.3m),
                ("Halloumi Croissant", "Toasted, zaatar honey", "🥐", 1.5m), ("Acai Bowl", "Granola, banana, coconut", "🫐", 2.4m),
                ("Date Cake Slice", "Toffee sauce", "🍰", 1.3m), ("Turkish Coffee", "Cardamom, traditional pot", "☕", 0.9m)
            }),
            ["Healthy"] = (new[] { "Bowls", "Wraps & Proteins", "Snacks & Drinks" }, new (string, string, string, decimal)[]
            {
                ("Chicken Quinoa Bowl", "Grilled chicken, avocado, quinoa", "🥗", 2.9m), ("Salmon Poke Bowl", "Sushi rice, edamame, ponzu", "🍣", 3.9m),
                ("Falafel Buddha Bowl", "Hummus, tabbouleh, falafel", "🧆", 2.4m), ("Steak & Sweet Potato", "Lean beef, roasted sweets", "🥩", 3.8m),
                ("Grilled Chicken Wrap", "Whole wheat, yogurt sauce", "🌯", 2.2m), ("Tuna Protein Box", "Egg, olives, greens", "🐟", 2.7m),
                ("Zucchini Noodles", "Basil pesto, cherry tomato", "🥒", 2.3m), ("Protein Pancakes", "Oat, banana, honey", "🥞", 2.0m),
                ("Kale Caesar", "Grilled chicken, light dressing", "🥬", 2.5m), ("Energy Balls (4pc)", "Dates, cocoa, oats", "⚡", 1.1m),
                ("Cold-Pressed Beet Juice", "Beet, apple, ginger", "🧃", 1.5m), ("Overnight Oats", "Chia, berries, almond milk", "🥣", 1.7m)
            })
        };

        var restaurantSpecs = new (string name, string emoji, string banner, string cuisine)[]
        {
            ("Napoli Nights", "🍕", "#FFE0CC", "Pizza & Italian"), ("Pasta Fresca", "🍝", "#FFF3D6", "Pizza & Italian"),
            ("Smash Republic", "🍔", "#FFEBD1", "Burgers"), ("Grill Brothers", "🍔", "#FDE7DA", "Burgers"),
            ("Beit Al Mandi", "🍖", "#EAF3DC", "Arabic & Grill"), ("Al Sultan Grills", "🍢", "#F4E9D8", "Arabic & Grill"),
            ("Saj Corner", "🌯", "#E8F0E0", "Arabic & Grill"), ("Mashawi Zone", "🥩", "#F6E3DC", "Arabic & Grill"),
            ("Tokyo Bites", "🍱", "#FBE3EA", "Japanese"), ("Ramen Ya", "🍜", "#F3E6F5", "Japanese"),
            ("Tandoor Nights", "🍛", "#FFE9C7", "Indian"), ("Biryani Express", "🍚", "#FFF0DB", "Indian"),
            ("Sweet Layla", "🍰", "#FDE4EF", "Desserts"), ("Kunafa King", "🧁", "#FFEED6", "Desserts"),
            ("Gelato Mio", "🍨", "#E3F1F8", "Desserts"), ("Qahwa House", "☕", "#EFE6DC", "Coffee & Juice"),
            ("Fresh Press", "🧃", "#E4F5E5", "Coffee & Juice"), ("Green Bowl", "🥗", "#E2F3E4", "Healthy"),
            ("Fit Fuel", "🥙", "#EAF4DE", "Healthy"), ("Salad Stop", "🥬", "#E7F5EC", "Healthy")
        };

        // ---------- Restaurants + owners + menus ----------
        var bigRestaurants = new List<Restaurant>();
        for (var i = 0; i < restaurantSpecs.Length; i++)
        {
            var spec = restaurantSpecs[i];
            var owner = new User
            {
                FullName = $"{firstNames[rnd.Next(firstNames.Length)]} {lastNames[rnd.Next(lastNames.Length)]}",
                Email = $"owner{i + 10}@majidfood.com", Phone = $"+968 92{100 + i:000} {1000 + i:0000}",
                PasswordHash = hash, Role = UserRole.RestaurantOwner, IsActive = true,
                CreatedAt = now.AddDays(-rnd.Next(90, 400))
            };
            db.Users.Add(owner);

            var restaurant = new Restaurant
            {
                Owner = owner, Name = spec.name,
                Description = $"{spec.name} — {dishPools[spec.cuisine].categories[0].ToLower()} and more, made fresh every day.",
                Cuisine = cuisines[CuisineIdx(spec.cuisine)], LogoEmoji = spec.emoji, BannerColor = spec.banner,
                Area = areas[rnd.Next(areas.Length)], Street = $"Street {rnd.Next(1, 60)}", Phone = $"+968 24{300 + i:000}0",
                DeliveryFee = new[] { 0m, 0.300m, 0.400m, 0.500m, 0.600m, 0.700m }[rnd.Next(6)],
                MinOrder = new[] { 1.000m, 1.500m, 2.000m, 2.500m, 3.000m }[rnd.Next(5)],
                AvgPrepMinutes = rnd.Next(12, 35), IsOpen = rnd.Next(10) > 1, IsApproved = true,
                CommissionPercent = new[] { 12m, 15m, 15m, 18m, 20m }[rnd.Next(5)],
                CreatedAt = now.AddDays(-rnd.Next(60, 350))
            };
            db.Restaurants.Add(restaurant);
            bigRestaurants.Add(restaurant);

            var pool = dishPools[spec.cuisine];
            var factor = 0.9m + (decimal)rnd.NextDouble() * 0.3m;
            var perCategory = pool.dishes.Length / pool.categories.Length;
            for (var c = 0; c < pool.categories.Length; c++)
            {
                var category = new MenuCategory { Restaurant = restaurant, Name = pool.categories[c], SortOrder = c + 1 };
                db.MenuCategories.Add(category);
                foreach (var dish in pool.dishes.Skip(c * perCategory).Take(perCategory))
                {
                    db.MenuItems.Add(new MenuItem
                    {
                        Restaurant = restaurant, Category = category, Name = dish.name, Description = dish.desc,
                        Price = Math.Round(dish.price * factor / 0.1m) * 0.1m, ImageEmoji = dish.emoji,
                        IsPopular = rnd.Next(4) == 0, IsAvailable = rnd.Next(12) > 0
                    });
                }
            }
        }

        // ---------- Customers + drivers ----------
        var bigCustomers = new List<(User user, Address address)>();
        for (var i = 0; i < 50; i++)
        {
            var customer = new User
            {
                FullName = $"{firstNames[rnd.Next(firstNames.Length)]} {lastNames[rnd.Next(lastNames.Length)]}",
                Email = $"customer{i + 10}@majidfood.com", Phone = $"+968 94{100 + i:000} {2000 + i:0000}",
                PasswordHash = hash, Role = UserRole.Customer, IsActive = true,
                CreatedAt = now.AddDays(-rnd.Next(5, 300))
            };
            var address = new Address
            {
                User = customer, Label = rnd.Next(4) == 0 ? "Office" : "Home",
                Area = areas[rnd.Next(areas.Length)], Street = $"Way {rnd.Next(1000, 6000)}",
                Building = $"House {rnd.Next(1, 400)}"
            };
            db.Users.Add(customer);
            db.Addresses.Add(address);
            db.SavedCards.Add(new SavedCard
            {
                User = customer, Brand = "Visa", HolderName = customer.FullName,
                Last4 = "4242", ExpMonth = 12, ExpYear = now.Year + 3
            });
            bigCustomers.Add((customer, address));
        }

        var bigDrivers = new List<User>();
        for (var i = 0; i < 10; i++)
        {
            var driver = new User
            {
                FullName = $"{firstNames[rnd.Next(firstNames.Length)]} {lastNames[rnd.Next(lastNames.Length)]}",
                Email = $"driver{i + 10}@majidfood.com", Phone = $"+968 93{100 + i:000} {3000 + i:0000}",
                PasswordHash = hash, Role = UserRole.Driver, IsActive = true,
                CreatedAt = now.AddDays(-rnd.Next(10, 250))
            };
            db.Users.Add(driver);
            db.DriverProfiles.Add(new DriverProfile
            {
                User = driver, VehicleType = (VehicleType)rnd.Next(0, 3), IsOnline = rnd.Next(10) < 6,
                Verification = DriverVerificationStatus.Approved
            });
            bigDrivers.Add(driver);
        }

        // Favorites — a few hearts per customer.
        foreach (var (customer, _) in bigCustomers)
        {
            foreach (var restaurant in bigRestaurants.OrderBy(_ => rnd.Next()).Take(rnd.Next(0, 5)))
                db.Favorites.Add(new FavoriteRestaurant { User = customer, Restaurant = restaurant });
        }

        await db.SaveChangesAsync();

        // ---------- ~90 days of order history ----------
        var comments = new[]
        {
            "Amazing food, arrived hot!", "Really tasty, will order again.", "Good but delivery took a while.",
            "Best in Muscat, hands down.", "Generous portions, great value.", "Packaging could be better.",
            "Driver was super friendly!", "A bit salty for me, still good.", null, null, null
        };
        var number = 1000 + db.Orders.Local.Count;
        var menuByRestaurant = db.MenuItems.Local.GroupBy(m => m.Restaurant).ToDictionary(g => g.Key, g => g.ToList());
        var pendingSince = 0;

        for (var day = 90; day >= 1; day--)
        {
            var ordersToday = rnd.Next(18, 42);
            for (var i = 0; i < ordersToday; i++)
            {
                var (customer, address) = bigCustomers[rnd.Next(bigCustomers.Count)];
                var restaurant = bigRestaurants[rnd.Next(bigRestaurants.Count)];
                var driver = bigDrivers[rnd.Next(bigDrivers.Count)];
                var placedAt = now.Date.AddDays(-day).AddHours(rnd.Next(10, 23)).AddMinutes(rnd.Next(0, 60));

                var roll = rnd.Next(100);
                var status = roll < 90 ? OrderStatus.Delivered : roll < 95 ? OrderStatus.Cancelled : OrderStatus.Rejected;

                var menu = menuByRestaurant[restaurant];
                var items = new List<OrderItem>();
                for (var j = 0; j < rnd.Next(1, 5); j++)
                {
                    var dish = menu[rnd.Next(menu.Count)];
                    var existing = items.FirstOrDefault(x => x.Name == dish.Name);
                    if (existing is not null) { existing.Quantity++; continue; }
                    items.Add(new OrderItem { MenuItemId = dish.Id, Name = dish.Name, UnitPrice = dish.Price, Quantity = rnd.Next(1, 3) });
                }

                var subtotal = items.Sum(x => x.UnitPrice * x.Quantity);
                var order = new Order
                {
                    Number = $"MF-{++number}",
                    Customer = customer, Restaurant = restaurant,
                    Driver = status == OrderStatus.Delivered ? driver : null,
                    Status = status, PaymentMethod = (PaymentMethod)rnd.Next(0, 2),
                    DeliveryAddress = $"{address.Label} — {address.Building}, {address.Street}, {address.Area}",
                    Subtotal = subtotal, DeliveryFee = restaurant.DeliveryFee, ServiceFee = Pricing.ServiceFee,
                    Discount = 0m, Total = subtotal + restaurant.DeliveryFee + Pricing.ServiceFee,
                    EstimatedMinutes = Pricing.EstimateMinutes(restaurant.AvgPrepMinutes),
                    PlacedAt = placedAt, Items = items
                };

                var t = placedAt;
                void Ev(OrderStatus s, string by, int plusMinutes)
                {
                    t = t.AddMinutes(plusMinutes);
                    order.Events.Add(new OrderEvent { Status = s, At = t, By = by });
                }
                Ev(OrderStatus.Pending, customer.FullName, 0);
                if (status == OrderStatus.Cancelled) { Ev(OrderStatus.Cancelled, customer.FullName, rnd.Next(2, 8)); order.CancelReason = "Changed my mind"; }
                else if (status == OrderStatus.Rejected) { Ev(OrderStatus.Rejected, restaurant.Name, rnd.Next(3, 9)); order.CancelReason = "Kitchen at full capacity"; }
                else
                {
                    Ev(OrderStatus.Accepted, restaurant.Name, rnd.Next(2, 6));
                    Ev(OrderStatus.Preparing, restaurant.Name, rnd.Next(1, 4));
                    Ev(OrderStatus.Ready, restaurant.Name, rnd.Next(8, restaurant.AvgPrepMinutes + 5));
                    Ev(OrderStatus.PickedUp, driver.FullName, rnd.Next(3, 9));
                    Ev(OrderStatus.OnTheWay, driver.FullName, rnd.Next(1, 3));
                    Ev(OrderStatus.Delivered, driver.FullName, rnd.Next(8, 22));
                    order.DeliveredAt = t;

                    if (rnd.Next(10) < 6)
                    {
                        var comment = comments[rnd.Next(comments.Length)];
                        db.Reviews.Add(new Review
                        {
                            Order = order, RestaurantId = 0, CustomerId = 0, // fixed up by the caller after save
                            RestaurantRating = rnd.Next(100) < 70 ? rnd.Next(4, 6) : rnd.Next(1, 4),
                            DriverRating = rnd.Next(3, 6),
                            Comment = comment,
                            CreatedAt = order.DeliveredAt!.Value.AddHours(rnd.Next(1, 30))
                        });
                    }
                }

                db.Orders.Add(order);
                if (++pendingSince >= 300)
                {
                    pendingSince = 0;
                    await db.SaveChangesAsync();
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
