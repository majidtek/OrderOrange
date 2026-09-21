namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Template "made of" data for dishes. The catalogue has millions of items, so
/// composition is derived from the dish name: a keyword template supplies the
/// ingredient list (as localization keys) and flags, and a stable hash of the
/// name spreads the calorie figures so items don't all look identical.
/// </summary>
public static class DishInfo
{
    public sealed record Details(
        (string Emoji, string Key)[] Ingredients,
        int Calories,
        bool Vegetarian,
        bool Spicy);

    public static Details For(string? name)
    {
        var n = (name ?? "").ToLowerInvariant();
        var seed = 0;
        foreach (var ch in n) seed = (seed * 31 + ch) & 0x7FFFFFFF;

        ((string, string)[] ing, int baseCal, bool veg) = n switch
        {
            _ when Has(n, "burger") => (new[] { ("🍞", "ing.bun"), ("🥩", "ing.beef"), ("🧀", "ing.cheese"), ("🥬", "ing.lettuce"), ("🍅", "ing.tomato"), ("🧅", "ing.onion"), ("🥫", "ing.sauce") }, 520, false),
            _ when Has(n, "pizza", "margherita", "pepperoni") => (new[] { ("🌾", "ing.dough"), ("🥫", "ing.tomatoSauce"), ("🧀", "ing.mozzarella"), ("🌿", "ing.oregano"), ("🫒", "ing.oliveOil") }, 610, true),
            _ when Has(n, "shawarma", "wrap") => (new[] { ("🍗", "ing.chicken"), ("🫓", "ing.flatbread"), ("🧄", "ing.garlicSauce"), ("🥒", "ing.pickles"), ("🍅", "ing.tomato") }, 430, false),
            _ when Has(n, "kebab", "kabab", "kofta", "tikka", "grill", "mashawi") => (new[] { ("🥩", "ing.lamb"), ("🧅", "ing.onion"), ("🌿", "ing.parsley"), ("🧂", "ing.spices") }, 390, false),
            _ when Has(n, "biryani", "kabsa", "mandi", "majboos", "rice", "quzi", "plov") => (new[] { ("🍚", "ing.rice"), ("🍗", "ing.chicken"), ("🧅", "ing.onion"), ("🧂", "ing.spices"), ("🌿", "ing.herbs") }, 560, false),
            _ when Has(n, "curry", "masala", "karahi", "dal") => (new[] { ("🍗", "ing.chicken"), ("🥫", "ing.tomatoSauce"), ("🧄", "ing.garlic"), ("🧂", "ing.spices"), ("🥛", "ing.cream") }, 480, false),
            _ when Has(n, "pasta", "spaghetti", "penne", "lasagn") => (new[] { ("🍝", "ing.pasta"), ("🥫", "ing.tomatoSauce"), ("🧀", "ing.parmesan"), ("🌿", "ing.basil") }, 540, true),
            _ when Has(n, "noodle", "ramen", "mee ") => (new[] { ("🍜", "ing.noodles"), ("🥦", "ing.vegetables"), ("🥚", "ing.eggs"), ("🥢", "ing.soy") }, 460, false),
            _ when Has(n, "salad", "tabbouleh", "fattoush") => (new[] { ("🥬", "ing.lettuce"), ("🥒", "ing.cucumber"), ("🍅", "ing.tomato"), ("🫒", "ing.oliveOil"), ("🍋", "ing.lemon") }, 180, true),
            _ when Has(n, "fries", "chips") => (new[] { ("🥔", "ing.potato"), ("🧂", "ing.salt"), ("🫒", "ing.oil") }, 320, true),
            _ when Has(n, "onion ring") => (new[] { ("🧅", "ing.onion"), ("🌾", "ing.flour"), ("🫒", "ing.oil") }, 300, true),
            _ when Has(n, "sandwich", "club", "sub ") => (new[] { ("🍞", "ing.bread"), ("🍗", "ing.chicken"), ("🥬", "ing.lettuce"), ("🥫", "ing.mayo") }, 380, false),
            _ when Has(n, "steak", "beef", "ribeye", "brisket") => (new[] { ("🥩", "ing.beef"), ("🧈", "ing.butter"), ("🧄", "ing.garlic"), ("🧂", "ing.pepper") }, 610, false),
            _ when Has(n, "soup", "shorba", "harira") => (new[] { ("🍲", "ing.broth"), ("🥦", "ing.vegetables"), ("🌿", "ing.herbs") }, 190, true),
            _ when Has(n, "sushi", "maki", "nigiri", "sashimi") => (new[] { ("🍚", "ing.rice"), ("🐟", "ing.fish"), ("🌊", "ing.seaweed"), ("🥢", "ing.soy") }, 300, false),
            _ when Has(n, "fish", "shrimp", "prawn", "salmon", "calamari", "seafood", "masgouf") => (new[] { ("🦐", "ing.seafoodMix"), ("🧄", "ing.garlic"), ("🍋", "ing.lemon"), ("🧈", "ing.butter") }, 350, false),
            _ when Has(n, "cake", "brownie", "kunafa", "baklava", "basbousa", "pudding", "dessert", "cheesecake") => (new[] { ("🌾", "ing.flour"), ("🍬", "ing.sugar"), ("🧈", "ing.butter"), ("🍫", "ing.chocolate") }, 450, true),
            _ when Has(n, "ice cream", "gelato", "sundae") => (new[] { ("🥛", "ing.milk"), ("🥛", "ing.cream"), ("🍬", "ing.sugar") }, 280, true),
            _ when Has(n, "pancake", "waffle", "crepe") => (new[] { ("🌾", "ing.flour"), ("🥚", "ing.eggs"), ("🥛", "ing.milk"), ("🍯", "ing.syrup") }, 420, true),
            _ when Has(n, "juice", "smoothie", "shake", "lemonade") => (new[] { ("🍓", "ing.fruit"), ("🧊", "ing.ice"), ("🍬", "ing.sugar") }, 160, true),
            _ when Has(n, "coffee", "latte", "cappuccino", "espresso", "mocha") => (new[] { ("☕", "ing.coffeeBeans"), ("🥛", "ing.milk") }, 90, true),
            _ when Has(n, "tea", "chai", "karak") => (new[] { ("🍃", "ing.teaLeaves"), ("🥛", "ing.milk"), ("🧂", "ing.cardamom") }, 80, true),
            _ when Has(n, "bread", "toast", "croissant", "naan", "paratha", "roti", "manakish") => (new[] { ("🌾", "ing.flour"), ("🧈", "ing.butter"), ("🧂", "ing.salt") }, 260, true),
            _ => (new[] { ("🥘", "ing.freshIngredients"), ("🧂", "ing.spices"), ("🌿", "ing.herbs") }, 350, false)
        };

        var spicy = Has(n, "spicy", "hot", "chili", "harissa", "peri", "piri", "jalape", "buffalo", "dynamite", "شطة", "حار");
        var calories = baseCal + (seed % 7) * 15 - 45;   // stable ±10% spread per dish

        return new Details(
            ing.Select(i => (i.Item1, i.Item2)).ToArray(),
            Math.Max(60, calories),
            veg,
            spicy);
    }

    private static bool Has(string text, params string[] words)
    {
        foreach (var w in words)
            if (text.Contains(w)) return true;
        return false;
    }
}
