using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Big virtual menus for the millions of bulk test stores. Storing real menu rows
/// for 5M stores would blow LocalDB's 10GB cap, so stores with no stored menu get
/// a deterministic template menu built at read time. Item ids are negative and
/// encode (restaurantId, slot) so ordering works end-to-end without stored rows.
/// </summary>
public static class MenuTemplates
{
    public record TItem(string Name, string Emoji, decimal Price);
    public record TCategory(string Name, TItem[] Items);

    private const int SlotsPerStore = 64;

    public static int VirtualId(int restaurantId, int slot) => -(restaurantId * SlotsPerStore + slot);

    public static (int RestaurantId, int Slot) Decode(int virtualId)
    {
        var v = -virtualId;
        return (v / SlotsPerStore, v % SlotsPerStore);
    }

    /// <summary>Every store prices the same template item a little differently.</summary>
    public static decimal PriceFor(int restaurantId, int slot, decimal basePrice)
    {
        var wobble = 1m + ((restaurantId + slot * 7) % 9 - 4) / 20m; // ±20%
        return Math.Round(basePrice * wobble, 3);
    }

    public static List<MenuCategoryDto> BuildMenu(int restaurantId, StoreType storeType, string cuisineName)
    {
        var template = TemplatesFor(storeType, cuisineName);
        var result = new List<MenuCategoryDto>();
        var slot = 0;
        for (var c = 0; c < template.Length; c++)
        {
            var items = new List<MenuItemDto>();
            foreach (var t in template[c].Items)
            {
                if (slot >= SlotsPerStore) break;
                items.Add(new MenuItemDto(
                    VirtualId(restaurantId, slot), -(c + 1), t.Name,
                    $"House {template[c].Name.ToLowerInvariant()} favourite, made fresh for you.",
                    PriceFor(restaurantId, slot, t.Price), true, t.Emoji,
                    (restaurantId + slot) % 9 == 0));
                slot++;
            }
            result.Add(new MenuCategoryDto(-(c + 1), template[c].Name, c, items));
        }
        return result;
    }

    /// <summary>The template item behind a virtual id's slot — null when out of range.</summary>
    public static TItem? ItemAt(StoreType storeType, string cuisineName, int slot)
    {
        var template = TemplatesFor(storeType, cuisineName);
        var i = 0;
        foreach (var category in template)
            foreach (var item in category.Items)
            {
                if (i == slot) return item;
                i++;
            }
        return null;
    }

    /// <summary>One searchable row per vertical-template product (market/pharmacy/
    /// flowers/shop) with its multilingual keywords — lets search find "خلاط" or
    /// "perfume" even though those products exist only as virtual menus.</summary>
    public sealed record SearchableItem(StoreType StoreType, int Slot, TItem Item, string Category, string Keywords);

    // Lazy on purpose: an eager field would run before the template arrays below are
    // initialized (C# static fields initialize in declaration order).
    private static List<SearchableItem>? _verticalItems;
    public static List<SearchableItem> VerticalItems => _verticalItems ??= BuildVerticalItems();

    private static List<SearchableItem> BuildVerticalItems()
    {
        var list = new List<SearchableItem>();
        foreach (var storeType in new[] { StoreType.Restaurant, StoreType.Grocery, StoreType.Pharmacy, StoreType.Flowers, StoreType.Shop })
        {
            var slot = 0;
            foreach (var category in TemplatesFor(storeType, ""))
                foreach (var item in category.Items)
                {
                    if (slot >= SlotsPerStore) break;
                    list.Add(new SearchableItem(storeType, slot, item, category.Name,
                        SearchAliases.KeywordsFor(item.Name, category.Name)));
                    slot++;
                }
        }
        return list;
    }

    private static TCategory[] TemplatesFor(StoreType storeType, string cuisineName) => storeType switch
    {
        StoreType.Grocery => Grocery,
        StoreType.Pharmacy => Pharmacy,
        StoreType.Flowers => Flowers,
        StoreType.Shop => Shop,
        _ => Restaurant(cuisineName)
    };

    private static TCategory[] Restaurant(string cuisine)
    {
        var c = cuisine.ToLowerInvariant();
        TItem[] mains =
            c.Contains("pizza") || c.Contains("ital") ? [
                new("Margherita Pizza", "🍕", 2.8m), new("Pepperoni Pizza", "🍕", 3.4m), new("Quattro Formaggi", "🍕", 3.8m),
                new("Vegetarian Pizza", "🍕", 3.0m), new("Chicken Ranch Pizza", "🍕", 3.6m), new("Spaghetti Bolognese", "🍝", 3.2m),
                new("Fettuccine Alfredo", "🍝", 3.4m), new("Lasagna", "🍝", 3.6m), new("Mushroom Risotto", "🍚", 3.5m), new("Calzone", "🥟", 3.1m)]
            : c.Contains("burger") ? [
                new("Classic Beef Burger", "🍔", 2.5m), new("Double Cheese Burger", "🍔", 3.4m), new("Crispy Chicken Burger", "🍔", 2.6m),
                new("Spicy Zinger Burger", "🍔", 2.8m), new("Mushroom Swiss Burger", "🍔", 3.2m), new("BBQ Bacon-Style Burger", "🍔", 3.5m),
                new("Veggie Burger", "🍔", 2.2m), new("Smash Burger", "🍔", 3.0m), new("Chicken Wrap", "🌯", 2.0m), new("Loaded Fries", "🍟", 1.8m)]
            : c.Contains("japan") || c.Contains("sushi") ? [
                new("Salmon Nigiri (4pc)", "🍣", 3.2m), new("California Roll", "🍣", 2.8m), new("Spicy Tuna Roll", "🍣", 3.4m),
                new("Dragon Roll", "🍣", 4.2m), new("Chicken Teriyaki", "🍗", 3.5m), new("Beef Ramen", "🍜", 3.8m),
                new("Miso Ramen", "🍜", 3.2m), new("Chicken Katsu", "🍱", 3.6m), new("Gyoza (6pc)", "🥟", 2.4m), new("Salmon Poke Bowl", "🥗", 4.0m)]
            : c.Contains("indian") ? [
                new("Chicken Biryani", "🍚", 2.8m), new("Mutton Biryani", "🍚", 3.6m), new("Butter Chicken", "🍛", 3.2m),
                new("Chicken Tikka Masala", "🍛", 3.3m), new("Palak Paneer", "🍛", 2.8m), new("Dal Tadka", "🍛", 2.2m),
                new("Garlic Naan", "🫓", 0.6m), new("Chicken 65", "🍗", 2.6m), new("Veg Thali", "🍽️", 3.0m), new("Tandoori Chicken", "🍗", 3.4m)]
            : [
                new("Chicken Shawarma Plate", "🥙", 2.4m), new("Beef Shawarma Plate", "🥙", 2.8m), new("Mixed Grill", "🍢", 4.2m),
                new("Shish Tawook", "🍢", 3.2m), new("Lamb Kofta", "🍢", 3.4m), new("Half Grilled Chicken", "🍗", 2.8m),
                new("Falafel Plate", "🧆", 1.8m), new("Hummus with Meat", "🥣", 2.2m), new("Chicken Mandi", "🍚", 3.5m), new("Lamb Shuwa", "🍖", 4.8m)];

        return
        [
            new TCategory("Starters", [
                new("Fries", "🍟", 0.9m), new("Cheese Fries", "🍟", 1.3m), new("Onion Rings", "🧅", 1.2m),
                new("Garlic Bread", "🥖", 1.0m), new("Soup of the Day", "🥣", 1.4m), new("Chicken Wings (6pc)", "🍗", 2.2m),
                new("Chicken Nuggets (9pc)", "🍗", 1.8m), new("Mozzarella Sticks", "🧀", 1.8m), new("Nachos", "🧀", 2.0m)]),
            new TCategory("Main Dishes", mains),
            new TCategory("Salads & Sides", [
                new("Caesar Salad", "🥗", 2.2m), new("Greek Salad", "🥗", 2.0m), new("Fattoush", "🥗", 1.6m),
                new("Tabbouleh", "🥗", 1.5m), new("Coleslaw", "🥗", 0.8m), new("Steamed Rice", "🍚", 0.8m)]),
            new TCategory("Desserts", [
                new("Kunafa", "🍮", 1.8m), new("Umm Ali", "🍮", 1.6m), new("Chocolate Cake", "🍰", 1.9m),
                new("Cheesecake", "🍰", 2.1m), new("Luqaimat", "🍡", 1.2m), new("Ice Cream (2 scoops)", "🍨", 1.3m),
                new("Halwa", "🍮", 1.4m), new("Fruit Platter", "🍉", 2.0m)]),
            new TCategory("Drinks", [
                new("Fresh Orange Juice", "🍊", 1.2m), new("Mango Juice", "🥭", 1.3m), new("Lemon Mint", "🍋", 1.1m),
                new("Karak Tea", "☕", 0.4m), new("Arabic Coffee", "☕", 0.8m), new("Soft Drink", "🥤", 0.4m),
                new("Water", "💧", 0.2m), new("Milkshake", "🥤", 1.6m)])
        ];
    }

    private static readonly TCategory[] Grocery =
    [
        new("Fruits & Vegetables", [
            new("Bananas (1kg)", "🍌", 0.6m), new("Apples (1kg)", "🍎", 1.0m), new("Oranges (1kg)", "🍊", 0.9m),
            new("Tomatoes (1kg)", "🍅", 0.7m), new("Cucumbers (1kg)", "🥒", 0.6m), new("Potatoes (2kg)", "🥔", 1.0m),
            new("Onions (2kg)", "🧅", 0.9m), new("Dates (500g)", "🌴", 1.8m), new("Mangoes (1kg)", "🥭", 1.6m), new("Watermelon", "🍉", 1.4m)]),
        new("Bakery", [
            new("Arabic Bread (5pc)", "🫓", 0.3m), new("Toast Bread", "🍞", 0.6m), new("Croissants (4pc)", "🥐", 1.2m),
            new("Baguette", "🥖", 0.5m), new("Muffins (4pc)", "🧁", 1.4m), new("Cake Slice", "🍰", 0.9m),
            new("Samoon (6pc)", "🥖", 0.4m), new("Donuts (3pc)", "🍩", 1.1m)]),
        new("Dairy & Eggs", [
            new("Fresh Milk 2L", "🥛", 1.2m), new("Laban 1L", "🥛", 0.7m), new("Eggs (30pc)", "🥚", 1.9m),
            new("Cheddar Cheese", "🧀", 1.6m), new("Halloumi", "🧀", 1.8m), new("Greek Yogurt", "🥛", 1.1m),
            new("Butter 400g", "🧈", 1.5m), new("Cream Cheese", "🧀", 1.2m)]),
        new("Snacks & Sweets", [
            new("Potato Chips", "🍟", 0.5m), new("Mixed Nuts (500g)", "🥜", 2.4m), new("Chocolate Bar", "🍫", 0.6m),
            new("Biscuits", "🍪", 0.8m), new("Popcorn", "🍿", 0.7m), new("Halwa Box", "🍮", 2.2m),
            new("Gummy Candy", "🍬", 0.9m), new("Granola Bars (6pc)", "🍫", 1.6m)]),
        new("Beverages", [
            new("Water (12x500ml)", "💧", 1.1m), new("Orange Juice 1L", "🍊", 1.3m), new("Cola (6 cans)", "🥤", 1.5m),
            new("Karak Mix", "☕", 1.2m), new("Coffee Beans 250g", "☕", 2.8m), new("Green Tea (25 bags)", "🍵", 1.4m),
            new("Energy Drink", "⚡", 0.8m), new("Sparkling Water", "💧", 0.6m)]),
        new("Household", [
            new("Dish Soap", "🧼", 0.9m), new("Laundry Detergent", "🧺", 2.6m), new("Tissues (5 boxes)", "🧻", 1.8m),
            new("Trash Bags", "🗑️", 1.2m), new("Hand Soap", "🧼", 0.8m), new("Air Freshener", "🌸", 1.4m),
            new("Sponges (6pc)", "🧽", 0.7m), new("Bleach 1L", "🧴", 0.9m)])
    ];

    private static readonly TCategory[] Pharmacy =
    [
        new("Pain Relief", [
            new("Panadol (24 tablets)", "💊", 0.9m), new("Panadol Extra", "💊", 1.2m), new("Ibuprofen 400mg", "💊", 1.1m),
            new("Aspirin", "💊", 0.8m), new("Muscle Rub Gel", "🧴", 1.8m), new("Migraine Relief", "💊", 1.6m),
            new("Back Pain Patches", "🩹", 2.2m), new("Paracetamol Syrup", "🧴", 1.0m)]),
        new("Cold & Flu", [
            new("Cough Syrup", "🧴", 1.4m), new("Throat Lozenges", "🍬", 0.9m), new("Nasal Spray", "🧴", 1.6m),
            new("Vitamin C Effervescent", "🍊", 1.8m), new("Flu Relief Sachets", "💊", 2.0m), new("Chest Rub", "🧴", 1.5m),
            new("Antihistamine", "💊", 1.3m), new("Thermometer", "🌡️", 2.4m)]),
        new("Vitamins & Supplements", [
            new("Multivitamin (60 tabs)", "💊", 3.2m), new("Vitamin D3", "☀️", 2.4m), new("Omega-3 Fish Oil", "🐟", 3.6m),
            new("Zinc Tablets", "💊", 1.8m), new("Iron Supplement", "💊", 2.1m), new("Calcium + D", "🦴", 2.6m),
            new("Biotin", "💊", 2.8m), new("Protein Powder", "🥤", 6.5m)]),
        new("First Aid", [
            new("Plasters (40pc)", "🩹", 0.8m), new("Antiseptic Spray", "🧴", 1.4m), new("Gauze Rolls", "🩹", 1.0m),
            new("Burn Gel", "🧴", 1.7m), new("First Aid Kit", "⛑️", 4.5m), new("Medical Tape", "🩹", 0.6m)]),
        new("Skin & Hair Care", [
            new("Moisturizing Cream", "🧴", 2.2m), new("Sunscreen SPF50", "☀️", 3.4m), new("Anti-Dandruff Shampoo", "🧴", 2.0m),
            new("Face Wash", "🧼", 1.8m), new("Lip Balm", "💄", 0.7m), new("Hand Cream", "🧴", 1.2m),
            new("Hair Oil", "🧴", 1.9m), new("Acne Gel", "🧴", 2.3m)]),
        new("Baby Care", [
            new("Baby Diapers (M, 44pc)", "👶", 3.8m), new("Baby Wipes (3 packs)", "👶", 2.1m), new("Baby Shampoo", "🧴", 1.6m),
            new("Baby Lotion", "🧴", 1.8m), new("Baby Formula (900g)", "🍼", 5.4m), new("Teething Gel", "🧴", 1.5m)]),
        new("Oral Care", [
            new("Signal Toothpaste 100ml", "🦷", 0.8m), new("Toothbrush Medium (2pc)", "🪥", 0.9m),
            new("Listerine Mouthwash 250ml", "🦷", 1.4m), new("Dental Floss 50m", "🦷", 0.7m)])
    ];

    private static readonly TCategory[] Flowers =
    [
        new("Bouquets", [
            new("Classic Rose Bouquet", "💐", 8.0m), new("Mixed Spring Bouquet", "💐", 6.5m), new("Tulip Bouquet", "🌷", 9.0m),
            new("Sunflower Bouquet", "🌻", 7.0m), new("Lily Bouquet", "🌸", 8.5m), new("Orchid Arrangement", "🌸", 12.0m),
            new("Baby's Breath Bouquet", "💐", 5.5m), new("Luxury 50-Rose Bouquet", "🌹", 25.0m)]),
        new("Roses", [
            new("Single Red Rose", "🌹", 1.0m), new("Dozen Red Roses", "🌹", 9.0m), new("White Roses (12)", "🤍", 9.5m),
            new("Pink Roses (12)", "🌸", 9.0m), new("Rose Box (25)", "🎁", 15.0m), new("Eternal Rose Dome", "🌹", 14.0m)]),
        new("Occasions", [
            new("Birthday Surprise", "🎂", 10.0m), new("Congratulations Stand", "🎉", 22.0m), new("New Baby Basket", "👶", 12.0m),
            new("Get Well Soon Bunch", "🌼", 8.0m), new("Anniversary Special", "💖", 15.0m), new("Graduation Bouquet", "🎓", 9.0m),
            new("Wedding Centerpiece", "💒", 18.0m), new("Sympathy Wreath", "🕊️", 20.0m)]),
        new("Plants", [
            new("Money Plant", "🪴", 4.5m), new("Peace Lily", "🪴", 6.0m), new("Succulent Trio", "🌵", 5.5m),
            new("Bonsai Tree", "🌳", 15.0m), new("Cactus Pot", "🌵", 3.5m), new("Areca Palm", "🌴", 8.0m)]),
        new("Gifts & Extras", [
            new("Chocolate Box", "🍫", 4.5m), new("Teddy Bear", "🧸", 5.0m), new("Scented Candle", "🕯️", 3.5m),
            new("Greeting Card", "💌", 1.0m), new("Balloon Set (5)", "🎈", 3.0m), new("Gift Wrapping", "🎁", 1.5m),
            new("Omani Frankincense (Luban)", "🪵", 3.5m), new("Oud Incense Sticks", "🪵", 2.5m)])
    ];

    private static readonly TCategory[] Shop =
    [
        new("Electronics", [
            new("Wireless Earbuds", "🎧", 8.9m), new("Phone Charger (Type-C)", "🔌", 2.5m), new("Power Bank 20000mAh", "🔋", 7.5m),
            new("Bluetooth Speaker", "🔊", 9.9m), new("Phone Case", "📱", 2.0m), new("Smart Watch", "⌚", 15.0m),
            new("USB Flash 64GB", "💾", 3.2m), new("LED Desk Lamp", "💡", 4.8m),
            new("PlayStation Controller", "🎮", 12.0m), new("Gaming Headset", "🎧", 8.0m)]),
        new("Home Appliances", [
            new("Blender 1.5L", "🥤", 6.5m), new("Air Fryer 4L", "🍟", 12.0m), new("Vacuum Cleaner", "🧹", 15.0m),
            new("Steam Iron", "👔", 5.5m), new("Microwave Oven 20L", "🍽️", 18.0m), new("Stand Fan 16\"", "💨", 7.0m),
            new("Mini Fridge 45L", "🧊", 25.0m), new("Portable Air Conditioner", "❄️", 22.0m), new("Washing Machine 7kg", "🧺", 45.0m)]),
        new("Home & Kitchen", [
            new("Non-Stick Pan", "🍳", 4.5m), new("Knife Set", "🔪", 6.0m), new("Storage Boxes (3)", "📦", 3.5m),
            new("Bath Towel Set", "🛁", 5.5m), new("Bed Sheet Set", "🛏️", 8.0m), new("Electric Kettle", "☕", 6.5m),
            new("Dinner Set (20pc)", "🍽️", 12.0m), new("Vacuum Flask 1L", "🏺", 4.2m), new("Memory Foam Pillow", "🛏️", 4.9m)]),
        new("Beauty & Personal Care", [
            new("Perfume 100ml", "✨", 12.0m), new("Makeup Kit", "💄", 9.5m), new("Hair Dryer", "💇", 7.0m),
            new("Beard Trimmer", "🪒", 8.5m), new("Skincare Set", "🧴", 10.0m), new("Nail Polish Set", "💅", 3.5m),
            new("Hair Straightener", "💇", 9.0m), new("Body Lotion Set", "🧴", 5.0m)]),
        new("Toys & Kids", [
            new("Building Blocks (100pc)", "🧱", 5.5m), new("RC Car", "🚗", 8.0m), new("Doll House", "🏠", 12.0m),
            new("Puzzle 1000pc", "🧩", 4.0m), new("Plush Toy", "🧸", 3.5m), new("Board Game", "🎲", 6.0m),
            new("Kids Bicycle 16\"", "🚲", 14.0m), new("Kick Scooter", "🛴", 9.0m)]),
        new("Stationery", [
            new("Notebook Set (5)", "📓", 2.5m), new("Pen Pack (10)", "🖊️", 1.5m), new("School Backpack", "🎒", 6.5m),
            new("Art Supplies Kit", "🎨", 5.0m), new("Desk Organizer", "🗂️", 3.0m), new("Scientific Calculator", "🧮", 4.5m)])
    ];
}
