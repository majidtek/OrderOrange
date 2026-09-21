namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Maps cuisine names and dish names to the real photography shipped with the
/// customer app (wwwroot/img/rest and img/dishes). Returns a relative image
/// path, or null for dishes with no confident match (the 3D emoji stays).
/// </summary>
public static class FoodPhoto
{
    /// <summary>Banner photo for a restaurant card — always returns something.
    /// Categories ship several photos; the seed (e.g. restaurant id) spreads
    /// them out so neighbouring cards don't all look identical.</summary>
    public static string ForRestaurant(string? cuisine, int seed = 0)
    {
        var (baseName, count) = Category(cuisine);
        var pick = count <= 1 ? 0 : Math.Abs(seed) % count;
        return pick == 0 ? $"{baseName}.jpg" : $"{baseName}{pick + 1}.jpg";
    }

    /// <summary>
    /// Banner slideshow for a store page — always several distinct photos so every
    /// restaurant animates: the category's own variants first (rotated by seed),
    /// food stores padded to four with appetizing generics. Verticals (grocery,
    /// pharmacy, flowers, shop) stay strictly on-theme.
    /// </summary>
    public static List<string> SlidesForRestaurant(string? cuisine, int seed = 0)
    {
        var (baseName, count) = Category(cuisine);
        var slides = new List<string>();
        var start = count <= 1 ? 0 : Math.Abs(seed) % count;
        for (var i = 0; i < count; i++)
        {
            var pick = (start + i) % count;
            slides.Add(pick == 0 ? $"{baseName}.jpg" : $"{baseName}{pick + 1}.jpg");
        }

        var isVertical = Has(baseName, "grocery", "pharmacy", "flowers", "shop");
        if (!isVertical)
        {
            string[] fillers =
            [
                "img/rest/food.jpg", "img/rest/food2.jpg", "img/rest/food3.jpg",
                "img/rest/salad.jpg", "img/rest/dessert.jpg", "img/rest/grill.jpg",
                "img/rest/curry.jpg", "img/rest/seafood.jpg",
                "img/dishes/pasta.jpg", "img/dishes/steak.jpg"
            ];
            for (var i = 0; slides.Count < 4 && i < fillers.Length; i++)
            {
                var filler = fillers[(Math.Abs(seed) + i) % fillers.Length];
                if (!slides.Contains(filler)) slides.Add(filler);
            }
        }
        return slides.Take(4).ToList();
    }

    private static (string BaseName, int Count) Category(string? cuisine)
    {
        var c = (cuisine ?? "").ToLowerInvariant();
        return c switch
        {
            _ when Has(c, "pizza", "italian") => ("img/rest/pizza", 2),
            _ when Has(c, "burger", "american") => ("img/rest/burger", 2),
            _ when Has(c, "argentin", "brazil", "german", "steak") => ("img/dishes/steak", 1),
            _ when Has(c, "grill", "arabic", "bbq", "shawarma", "turkish", "lebanese", "levant", "syrian", "iraqi", "yemeni", "omani", "saudi", "emirati", "afghan", "persian", "iranian", "uzbek", "egyptian", "mexican", "greek") => ("img/rest/grill", 2),
            _ when Has(c, "indian", "biryani", "pakistani", "bangla", "nepali", "sri lank", "moroccan", "ethiopian", "somali", "sudan") => ("img/rest/curry", 1),
            _ when Has(c, "french", "croissant") => ("img/dishes/bread", 1),
            _ when Has(c, "sushi", "japanese", "asian", "chinese", "korean", "thai", "vietnam", "indones", "malays", "filipino", "taiwan", "singapor") => ("img/rest/sushi", 2),
            _ when Has(c, "dessert", "sweet", "bakery", "cake", "pastr") => ("img/rest/dessert", 1),
            _ when Has(c, "coffee", "cafe", "café", "juice", "drink") => ("img/rest/coffee", 2),
            _ when Has(c, "healthy", "salad", "vegan", "vegetarian") => ("img/rest/salad", 1),
            _ when Has(c, "seafood", "fish") => ("img/rest/seafood", 1),
            _ when Has(c, "breakfast", "pancake") => ("img/dishes/pancake", 1),
            _ when Has(c, "grocery", "groceries", "mart", "market", "baqala", "hyper") => ("img/rest/grocery", 4),
            _ when Has(c, "pharmacy", "health", "care") => ("img/rest/pharmacy", 2),
            _ when Has(c, "flower", "rose", "bloom") => ("img/rest/flowers", 4),
            _ when Has(c, "shop", "store", "electronic", "gift") => ("img/rest/shop", 3),
            _ => ("img/rest/food", 3)
        };
    }

    /// <summary>Photo for a dish tile, or null to keep the emoji. Understands
    /// the main dish words in the app's languages, because search results and
    /// the chatbot localize names ("Classic Smash · برجر").</summary>
    /// <summary>The same guess, but honouring the store's choice: a shop that turned
    /// stock photos off gets null every time, and the emoji stands in.</summary>
    public static string? ForDish(string? name, bool allow) =>
        allow ? ForDish(name) : null;

    public static string? ForDish(string? name)
    {
        var n = (name ?? "").ToLowerInvariant();
        return n switch
        {
            _ when Has(n, "onion ring") => "img/dishes/rings.jpg",
            _ when Has(n, "fries", "chips", "بطاطس", "فرايز", "картошка") => "img/dishes/fries.jpg",
            _ when Has(n, "pizza", "margherita", "pepperoni", "بيتزا", "پیتزا", "پيتزا", "пицца", "पिज्जा", "披萨", "ピザ") => "img/rest/pizza.jpg",
            _ when Has(n, "burger", "برجر", "برقر", "برغر", "бургер", "बर्गर", "汉堡", "バーガー") => "img/rest/burger.jpg",
            // Rice dishes first — "Chicken Biryani" is a rice plate, not a grill.
            _ when Has(n, "biryani", "rice", "kabsa", "mandi", "majboos", "curry", "masala", "karahi", "dal", "quzi", "plov", "kacchi",
                       "stew", "khoresh", "خورش", "خورشت", "قيمه", "قورمه", "gheimeh", "eggplant", "بادمجان", "polo", "پلو",
                       "برياني", "بریانی", "كبسه", "كبسة", "مندي", "مجبوس", "أرز", "ارز", "كاري",
                       "برنج", "बिरयानी", "चावल", "карри", "咖喱", "カレー") => "img/rest/curry.jpg",
            _ when Has(n, "shawarma", "kebab", "kabab", "kofta", "tikka", "grill", "mashawi", "wings", "broast", "chicken",
                       "شاورما", "كباب", "کباب", "دجاج", "مشاوي", "مشوي", "فروج", "تكا", "شيش",
                       "шаурма", "चिकन", "कबाब", "炸鸡", "チキン") => "img/rest/grill.jpg",
            _ when Has(n, "pasta", "spaghetti", "noodle", "ramen", "penne", "lasagn", "mee ",
                       "معكرونه", "معكرونة", "باستا", "نودلز", "پاستا", "拉面", "面条", "ラーメン") => "img/dishes/pasta.jpg",
            _ when Has(n, "soup", "shorba", "harira", "شوربه", "شوربة", "سوپ", "суп", "汤", "スープ") => "img/dishes/soup.jpg",
            _ when Has(n, "salad", "tabbouleh", "fattoush", "سلطه", "سلطة", "تبوله", "فتوش", "سالاد", "सलाद", "салат", "沙拉", "サラダ") => "img/rest/salad.jpg",
            _ when Has(n, "sushi", "maki", "nigiri", "sashimi", "سوشي", "سوشی", "суши", "寿司", "すし", "刺身") => "img/rest/sushi.jpg",
            _ when Has(n, "sandwich", "club", "sub ", "ساندويش", "ساندويتش", "ساندویچ", "سندويش", "سندويتش", "सैंडविच", "сэндвич", "三明治", "サンド") => "img/dishes/sandwich.jpg",
            _ when Has(n, "steak", "beef", "ribeye", "brisket", "ستيك", "استيك", "стейк", "ステーキ", "牛排") => "img/dishes/steak.jpg",
            _ when Has(n, "cake", "gateau", "cheesecake", "كيك", "کیک", "केक", "торт", "蛋糕", "ケーキ") => "img/dishes/cake.jpg",
            _ when Has(n, "kunafa", "baklava", "halwa", "brownie", "pudding", "dessert", "basbousa",
                       "كنافه", "كنافة", "بقلاوه", "بقلاوة", "حلا", "حلوى", "حلويات", "دسر", "मिठाई", "десерт", "甜点", "デザート") => "img/rest/dessert.jpg",
            _ when Has(n, "ice cream", "gelato", "sundae", "softy", "ايس كريم", "آيس كريم", "بوظه", "مثلجات", "بستنی", "आइसक्रीम", "мороженое", "冰淇淋", "アイス") => "img/dishes/icecream.jpg",
            // ---------- Drinks ----------
            // "chocolate" literally contains "cola", so chocolate is settled first:
            // the hot drink goes with tea, anything else chocolate is a dessert.
            _ when Has(n, "hot chocolate", "hot cocoa", "sahlab", "cocoa",
                       "شوكولاته ساخنه", "شوكولاتة ساخنة", "سحلب", "كاكاو",
                       "какао", "热巧克力", "ホットチョコ") => "img/dishes/tea.jpg",

            // Milk and yoghurt drinks: before both chocolate and water, so a chocolate
            // milkshake stays a drink and laban/ayran keep their own photo.
            _ when Has(n, "milkshake", "milk shake", "laban", "ayran", "buttermilk", "yogurt drink", "yoghurt drink", "lassi",
                       "milk", "labneh drink",
                       "لبن", "حليب", "عيران", "لسي", "ميلك شيك", "شير", "دوغ",
                       "دودھ", "لسّی", "दूध", "लस्सी", "молоко", "айран", "牛奶", "酸奶", "牛乳", "ミルク") => "img/dishes/milk.jpg",

            _ when Has(n, "chocolate", "شوكولا", "شكولا", "شکلات", "چاکلیٹ", "चॉकलेट", "шоколад", "巧克力", "チョコ") => "img/rest/dessert.jpg",

            _ when Has(n, "cola", "pepsi", "soda", "soft drink", "softdrink", "fizzy", "sprite", "fanta", "7up", "seven up",
                       "mountain dew", "mirinda", "energy drink", "red bull", "power horse", "tonic", "root beer", "ginger ale",
                       "كولا", "بيبسي", "ببسي", "مشروب غازي", "مشروبات غازيه", "مشروبات غازية", "غازي", "غازية",
                       "سفن اب", "سبرايت", "فانتا", "مشروب طاقه", "مشروب طاقة", "ريد بول",
                       "نوشابه", "سودا", "کولا",
                       "کولڈ ڈرنک", "سافٹ ڈرنک", "कोला", "सोडा", "कोल्ड ड्रिंक", "एनर्जी ड्रिंक",
                       "кола", "газировка", "энергетик", "可乐", "汽水", "碳酸", "能量饮料", "コーラ", "炭酸", "ソーダ") => "img/dishes/soda.jpg",

            // Juice is checked before water so "watermelon juice" keeps the juice photo.
            _ when Has(n, "juice", "smoothie", "shake", "mojito", "lemonade", "nectar", "cocktail", "iced tea", "ice tea",
                       "عصير", "عصائر", "ليموناضه", "ليموناضة", "موهيتو", "سموذي", "كوكتيل", "شاي مثلج",
                       "آبمیوه", "آب‌میوه", "جوس", "जूस", "शरबत", "сок", "смузи", "лимонад", "果汁", "冰茶", "ジュース", "スムージー") => "img/dishes/juice.jpg",

            _ when Has(n, "water", "mineral water", "sparkling water", "still water", "aqua", "masafi", "tanuf", "zamzam",
                       "ماء", "مياه", "ماي", "مياه معدنيه", "مياه معدنية", "مياه غازيه", "زمزم", "مصافي", "تنوف",
                       "آب", "آب معدنی", "پانی", "منرل واٹر", "पानी", "मिनरल वाटर", "जल",
                       "вода", "минеральная вода", "水", "矿泉水", "ミネラルウォーター", "お水") => "img/dishes/water.jpg",

            _ when Has(n, "coffee", "latte", "cappuccino", "espresso", "americano", "mocha", "macchiato", "frappe", "qahwa",
                       "قهوه", "قهوة", "كوفي", "لاتيه", "لاتية", "كابتشينو", "اسبريسو", "إسبريسو", "موكا", "مكياتو", "فرابيه",
                       "کافی", "کاپوچینو", "कॉफी", "кофе", "капучино", "咖啡", "拿铁", "コーヒー", "ラテ") => "img/rest/coffee.jpg",

            _ when Has(n, "tea", "chai", "karak", "matcha", "hot chocolate", "hot cocoa", "sahlab",
                       "شاي", "شاهي", "كرك", "سحلب", "شوكولاته ساخنه", "شوكولاتة ساخنة",
                       "چای", "چائے", "चाय", "чай", "какао", "奶茶", "红茶", "紅茶", "抹茶") => "img/dishes/tea.jpg",
            _ when Has(n, "garlic bread", "bread", "toast", "croissant", "paratha", "naan", "roti", "manakish",
                       "خبز", "توست", "كرواسون", "مناقيش", "نان", "روتي", "रोटी", "хлеб", "面包", "パン") => "img/dishes/bread.jpg",
            _ when Has(n, "pancake", "waffle", "crepe", "بان كيك", "وافل", "كريب", "پنکیک", "блины", "パンケーキ") => "img/dishes/pancake.jpg",
            _ when Has(n, "fish", "shrimp", "prawn", "seafood", "salmon", "calamari", "masgouf",
                       "سمك", "روبيان", "جمبري", "سلمون", "كاليماري", "ماهی", "मछली", "рыба", "海鲜", "魚") => "img/rest/seafood.jpg",
            _ => null
        };
    }

    private static bool Has(string text, params string[] words)
    {
        foreach (var w in words)
            if (text.Contains(w)) return true;
        return false;
    }
}
