namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// The assistant's multilingual vocabulary: intent trigger phrases, yes/no words,
/// number words and per-language marker words for all 14 app languages (plus
/// romanized Arabic/Hindi/Urdu chat forms). Everything here is matched fuzzily by
/// <see cref="BotNlu"/>, so common misspellings are tolerated automatically —
/// this list only needs the *canonical* spellings and popular dialect variants.
/// </summary>
public static partial class BotLexicon
{
    public enum Intent
    {
        None, Greeting, Help, Thanks, StartOrder, TrackOrder, CancelOrder,
        ShowCart, ClearCart, RemoveItem, Checkout, Reorder, Yes, No
    }

    /// <summary>Phrases that trigger each intent. Multi-word phrases must fully match.</summary>
    public static readonly Dictionary<Intent, string[]> Phrases = new()
    {
        [Intent.Greeting] =
        [
            // en
            "hello", "hi", "hey", "good morning", "good evening", "good afternoon",
            // ar (+ dialect + romanized)
            "مرحبا", "اهلا", "أهلا وسهلا", "السلام عليكم", "سلام", "هلا", "هاي", "صباح الخير", "مساء الخير",
            "marhaba", "ahlan", "salam alaikum", "hala",
            // fa
            "درود", "سلام علیکم",
            // ur
            "السلام علیکم", "آداب", "ہیلو",
            // hi
            "नमस्ते", "नमस्कार", "हैलो", "namaste", "namaskar",
            // tr
            "merhaba", "selam", "günaydın", "iyi akşamlar",
            // fr
            "bonjour", "bonsoir", "salut", "coucou",
            // es
            "hola", "buenos días", "buenas tardes", "buenas noches",
            // de
            "hallo", "guten tag", "guten morgen", "guten abend", "servus", "moin",
            // ru
            "привет", "здравствуйте", "добрый день", "доброе утро", "добрый вечер",
            // it
            "ciao", "buongiorno", "buonasera", "salve",
            // pt
            "olá", "oi", "bom dia", "boa tarde", "boa noite",
            // zh
            "你好", "您好", "早上好", "晚上好", "嗨",
            // ja
            "こんにちは", "こんばんは", "おはよう", "やあ",
        ],
        [Intent.Help] =
        [
            "help", "what can you do", "how does this work", "options", "menu of commands",
            "مساعدة", "ساعدني", "شو تقدر تسوي", "ماذا يمكنك ان تفعل", "كيف استخدمك", "الاوامر",
            "کمک", "راهنما", "چه کاری میتونی بکنی",
            "مدد", "مدد کریں", "آپ کیا کر سکتے ہیں",
            "मदद", "सहायता", "तुम क्या कर सकते हो", "madad", "help karo",
            "yardım", "yardım et", "ne yapabilirsin", "nasıl çalışır",
            "aide", "aidez moi", "que peux tu faire", "comment ça marche",
            "ayuda", "ayúdame", "qué puedes hacer", "cómo funciona",
            "hilfe", "hilf mir", "was kannst du", "wie funktioniert das",
            "помощь", "помоги", "что ты умеешь", "как это работает",
            "aiuto", "aiutami", "cosa sai fare", "come funziona",
            "ajuda", "me ajuda", "o que você pode fazer", "como funciona",
            "帮助", "帮帮我", "你能做什么", "怎么用",
            "助けて", "ヘルプ", "何ができる", "使い方",
        ],
        [Intent.Thanks] =
        [
            "thanks", "thank you", "thx", "ty", "great thanks", "perfect thanks", "awesome",
            "شكرا", "شكرا جزيلا", "مشكور", "يعطيك العافية", "تسلم", "shukran", "mashkoor",
            "ممنون", "متشکرم", "مرسی", "تشکر",
            "شکریہ", "بہت شکریہ",
            "धन्यवाद", "शुक्रिया", "dhanyavad", "shukriya",
            "teşekkürler", "teşekkür ederim", "sağol", "sağ ol",
            "merci", "merci beaucoup",
            "gracias", "muchas gracias",
            "danke", "danke schön", "vielen dank",
            "спасибо", "большое спасибо", "благодарю",
            "grazie", "grazie mille",
            "obrigado", "obrigada", "muito obrigado",
            "谢谢", "多谢", "感谢",
            "ありがとう", "ありがとうございます", "どうも",
        ],
        [Intent.StartOrder] =
        [
            // en
            "i want to order", "i want", "i would like", "id like", "order food", "place an order",
            "im hungry", "i am hungry", "get me", "give me", "can i get", "can i have", "i wanna",
            "buy", "order", "add", "i need", "bring me", "food",
            // ar
            "ابي اطلب", "أريد أن أطلب", "اريد اطلب", "بدي اطلب", "ابغى اطلب", "اطلب", "طلب",
            "ابي", "اريد", "بدي", "ابغى", "اعطني", "جيب لي", "انا جوعان", "جوعان", "جعان",
            "اضف", "زود", "زد", "اطلب اكل", "ودي", "ودي اطلب", "اشتري",
            "abi atlub", "abgha atlub", "biddi", "atlub", "jooaan",
            // fa
            "میخوام سفارش بدم", "می خواهم سفارش دهم", "سفارش بده", "سفارش", "میخوام", "گرسنه ام", "گرسنمه", "اضافه کن",
            // ur
            "میں آرڈر کرنا چاہتا ہوں", "آرڈر کرو", "آرڈر", "مجھے چاہیے", "بھوک لگی ہے", "منگوا دو",
            // hi
            "मुझे ऑर्डर करना है", "ऑर्डर करो", "ऑर्डर", "मुझे चाहिए", "भूख लगी है", "मंगवा दो", "खाना चाहिए",
            "mujhe order karna hai", "order karo", "mujhe chahiye", "bhook lagi hai", "khana chahiye",
            // tr
            "sipariş vermek istiyorum", "sipariş ver", "sipariş", "istiyorum", "acıktım", "açım", "ekle", "yemek söyle",
            // fr
            "je veux commander", "je voudrais", "j'aimerais", "commander", "commande", "j'ai faim", "ajoute", "je veux",
            // es
            "quiero pedir", "quisiera", "me gustaría", "pedir", "pedido", "tengo hambre", "añade", "agrega", "quiero",
            // de
            "ich möchte bestellen", "ich will bestellen", "ich hätte gern", "bestellen", "bestellung", "ich habe hunger", "füge hinzu", "ich möchte",
            // ru
            "я хочу заказать", "хочу заказать", "заказать", "заказ", "я голоден", "я голодна", "добавь", "закажи", "хочу",
            // it
            "voglio ordinare", "vorrei ordinare", "vorrei", "ordinare", "ordina", "ho fame", "aggiungi", "voglio",
            // pt
            "quero pedir", "eu quero", "gostaria de pedir", "pedir", "fazer um pedido", "estou com fome", "adiciona", "quero",
            // zh
            "我要点餐", "我想点", "点餐", "下单", "我要", "我想要", "我饿了", "加一个", "来一份", "订购",
            // ja
            "注文したい", "注文", "頼みたい", "お腹すいた", "お腹が空いた", "追加して", "ほしい",
        ],
        [Intent.TrackOrder] =
        [
            // en
            "where is my order", "wheres my order", "track my order", "track order", "order status",
            "my order status", "follow my order", "how long for my order", "when will my order arrive",
            "where is my food", "wheres my food", "is my order coming", "delivery status", "track",
            // ar
            "وين طلبي", "أين طلبي", "فين طلبي", "تتبع طلبي", "تتبع الطلب", "حالة الطلب", "حالة طلبي",
            "وين الاكل", "متى يوصل طلبي", "متى يوصل الطلب", "طلبي وين وصل", "وصل طلبي", "تابع طلبي",
            "wain talabi", "fen talabi", "track talabi",
            // fa
            "سفارشم کجاست", "سفارش من کجاست", "پیگیری سفارش", "وضعیت سفارش", "کی میرسه سفارشم", "سفارشم کی میرسه",
            // ur
            "میرا آرڈر کہاں ہے", "آرڈر کہاں ہے", "آرڈر ٹریک کرو", "آرڈر کا اسٹیٹس", "آرڈر کب آئے گا",
            // hi
            "मेरा ऑर्डर कहां है", "ऑर्डर कहां है", "ऑर्डर ट्रैक करो", "ऑर्डर का स्टेटस", "ऑर्डर कब आएगा",
            "mera order kahan hai", "order kahan hai", "order track karo", "order kab aayega",
            // tr
            "siparişim nerede", "sipariş nerede", "sipariş takip", "siparişimi takip et", "sipariş durumu", "ne zaman gelir",
            // fr
            "où est ma commande", "ou est ma commande", "suivre ma commande", "suivi de commande",
            "statut de ma commande", "quand arrive ma commande", "où est mon repas",
            // es
            "dónde está mi pedido", "donde esta mi pedido", "rastrear mi pedido", "seguir mi pedido",
            "estado de mi pedido", "cuándo llega mi pedido", "dónde está mi comida",
            // de
            "wo ist meine bestellung", "bestellung verfolgen", "bestellstatus", "wann kommt meine bestellung", "wo bleibt mein essen",
            // ru
            "где мой заказ", "где заказ", "отследить заказ", "отслеживание заказа", "статус заказа", "когда приедет заказ", "где моя еда",
            // it
            "dov'è il mio ordine", "dove il mio ordine", "traccia il mio ordine", "tracciare ordine",
            "stato del mio ordine", "quando arriva il mio ordine", "dov'è il mio cibo",
            // pt
            "onde está meu pedido", "cadê meu pedido", "rastrear meu pedido", "acompanhar pedido",
            "status do meu pedido", "quando chega meu pedido", "onde está minha comida",
            // zh
            "我的订单在哪", "我的订单在哪里", "订单到哪了", "跟踪订单", "订单状态", "什么时候到", "查订单", "外卖到哪了",
            // ja
            "注文はどこ", "注文どこ", "注文を追跡", "注文状況", "いつ届く", "配達状況", "追跡して",
        ],
        [Intent.CancelOrder] =
        [
            "cancel my order", "cancel order", "cancel the order", "i want to cancel", "cancel it", "cancel",
            "الغي طلبي", "ألغ الطلب", "الغاء الطلب", "إلغاء الطلب", "بدي الغي", "ابي الغي الطلب", "كنسل الطلب", "كنسل",
            "لغو سفارش", "سفارشمو لغو کن", "کنسل کن",
            "آرڈر کینسل کرو", "آرڈر منسوخ کرو", "کینسل",
            "ऑर्डर कैंसिल करो", "ऑर्डर रद्द करो", "order cancel karo", "cancel karo",
            "siparişi iptal et", "sipariş iptal", "iptal et", "iptal",
            "annuler ma commande", "annuler la commande", "annule", "annuler",
            "cancelar mi pedido", "cancelar el pedido", "cancela", "anular pedido",
            "bestellung stornieren", "storniere meine bestellung", "stornieren", "abbrechen",
            "отменить заказ", "отмени заказ", "отмена заказа", "отмени",
            "annullare il mio ordine", "annulla l'ordine", "annulla", "cancella ordine",
            "cancelar meu pedido", "cancela o pedido", "cancelar pedido",
            "取消订单", "取消我的订单", "取消",
            "注文をキャンセル", "キャンセルして", "キャンセル",
        ],
        [Intent.ShowCart] =
        [
            "show my cart", "show cart", "my cart", "view cart", "whats in my cart", "my basket", "show basket", "cart", "basket",
            "شوف سلتي", "اعرض السلة", "سلتي", "السلة", "شو في السلة", "وريني السلة", "عربتي",
            "سبد خرید", "سبدم", "سبد منو نشون بده",
            "میری ٹوکری", "کارٹ دکھاؤ", "ٹوکری",
            "मेरी कार्ट", "कार्ट दिखाओ", "टोकरी", "meri cart", "cart dikhao",
            "sepetim", "sepeti göster", "sepetimde ne var", "sepet",
            "mon panier", "voir le panier", "affiche mon panier", "panier",
            "mi carrito", "ver carrito", "mi cesta", "carrito",
            "mein warenkorb", "warenkorb anzeigen", "warenkorb", "korb",
            "моя корзина", "покажи корзину", "что в корзине", "корзина",
            "il mio carrello", "mostra il carrello", "carrello",
            "meu carrinho", "ver carrinho", "mostra o carrinho", "carrinho",
            "我的购物车", "购物车", "看看购物车",
            "カート", "カートを見せて", "買い物かご",
        ],
        [Intent.ClearCart] =
        [
            "empty my cart", "clear my cart", "empty cart", "clear cart", "delete cart", "remove everything",
            "فرغ السلة", "امسح السلة", "احذف السلة", "فضي السلة",
            "سبد را خالی کن", "سبدمو خالی کن",
            "ٹوکری خالی کرو", "کارٹ صاف کرو",
            "कार्ट खाली करो", "कार्ट साफ करो", "cart khali karo",
            "sepeti boşalt", "sepeti temizle",
            "vider mon panier", "vide le panier",
            "vaciar carrito", "vacía el carrito", "limpiar carrito",
            "warenkorb leeren", "leere den warenkorb",
            "очистить корзину", "очисти корзину",
            "svuota il carrello", "svuotare carrello",
            "esvaziar carrinho", "limpar carrinho",
            "清空购物车",
            "カートを空にして",
        ],
        [Intent.RemoveItem] =
        [
            "remove", "remove the", "take out", "take off", "delete the", "without the", "minus one",
            "شيل", "احذف", "امسح", "بدون", "نقص", "خفف", "الغي من السله",
            "حذف کن", "برش دار", "کمش کن", "بدون",
            "ہٹا دو", "نکال دو", "کم کرو",
            "हटा दो", "निकाल दो", "कम करो", "hata do", "nikal do",
            "çıkar", "kaldır", "sil", "azalt", "çıkart",
            "enlève", "retire", "supprime", "sans le", "sans la",
            "quita", "elimina", "saca", "sin el", "sin la",
            "entferne", "nimm raus", "lösche", "ohne das", "weniger",
            "убери", "удали", "убрать", "без", "поменьше",
            "togli", "rimuovi", "elimina", "senza il", "senza la",
            "tira", "remove o", "remove a", "tirar", "sem o", "sem a",
            "去掉", "删掉", "删除", "不要了", "减少",
            "抜いて", "外して", "削除して", "減らして", "なしで",
        ],
        [Intent.Checkout] =
        [
            "checkout", "check out", "place the order", "place my order", "confirm order", "complete order",
            "finish order", "pay", "proceed to payment", "send my order", "submit order",
            "اتمام الطلب", "اكمل الطلب", "أكمل الطلب", "كمل الطلب", "اتمم الطلب", "ارسل الطلب",
            "اكد الطلب", "أكد الطلب", "الدفع", "ادفع", "خلص الطلب", "انهاء الطلب", "تشيك اوت",
            "پرداخت", "نهایی کردن سفارش", "ثبت سفارش", "تکمیل سفارش",
            "آرڈر مکمل کرو", "ادائیگی", "آرڈر بھیجو", "چیک آؤٹ",
            "चेकआउट", "ऑर्डर पूरा करो", "भुगतान", "ऑर्डर भेजो", "order complete karo", "payment karo",
            "siparişi tamamla", "ödeme", "ödeme yap", "siparişi onayla", "siparişi gönder",
            "finaliser la commande", "valider la commande", "payer", "paiement",
            "finalizar pedido", "completar pedido", "confirmar pedido", "pagar", "pago", "tramitar pedido",
            "zur kasse", "bestellung abschließen", "bestellung bestätigen", "bezahlen", "kasse",
            "оформить заказ", "оформи заказ", "подтвердить заказ", "оплатить", "оплата", "к оплате",
            "concludi l'ordine", "completa l'ordine", "conferma l'ordine", "pagare", "pagamento", "alla cassa",
            "finalizar o pedido", "concluir pedido", "confirmar o pedido", "pagar", "pagamento", "fechar pedido",
            "结账", "去结算", "结算", "提交订单", "付款", "支付",
            "会計", "チェックアウト", "注文を確定", "支払い", "支払う",
        ],
        [Intent.Reorder] =
        [
            "order again", "reorder", "repeat my order", "repeat last order", "same as last time", "my usual", "the usual",
            "اطلب مرة ثانية", "اعد الطلب", "أعد طلبي", "كرر الطلب", "كرر طلبي الاخير", "نفس الطلب السابق", "نفس المرة الماضية", "طلبي المعتاد",
            "دوباره سفارش بده", "سفارش قبلی رو تکرار کن", "همون همیشگی",
            "دوبارہ آرڈر کرو", "پچھلا آرڈر دہراؤ", "وہی آرڈر",
            "फिर से ऑर्डर करो", "पिछला ऑर्डर दोहराओ", "वही ऑर्डर", "same order phir se", "dobara order karo",
            "tekrar sipariş ver", "son siparişi tekrarla", "aynısından",
            "commander à nouveau", "recommander", "répète ma commande", "comme la dernière fois",
            "pedir de nuevo", "repetir pedido", "repite mi pedido", "lo mismo de la última vez",
            "nochmal bestellen", "erneut bestellen", "wiederhole meine bestellung", "das gleiche wie letztes mal",
            "заказать снова", "повторить заказ", "повтори мой заказ", "как в прошлый раз",
            "ordina di nuovo", "riordina", "ripeti il mio ordine", "come l'ultima volta",
            "pedir de novo", "repetir pedido", "repete meu pedido", "o mesmo da última vez",
            "再来一单", "再点一次", "重复上次订单", "老样子",
            "もう一度注文", "再注文", "前回と同じ", "いつもの",
        ],
        [Intent.Yes] =
        [
            "yes", "yeah", "yep", "yup", "sure", "ok", "okay", "confirm", "correct", "right", "go ahead", "do it", "of course", "definitely", "absolutely",
            "نعم", "ايه", "اي", "ايوه", "ايوا", "اجل", "تمام", "اوك", "اوكي", "موافق", "اكيد", "طبعا", "صح", "زين", "ماشي", "توكل", "na3am", "aywa", "tamam", "akeed",
            "بله", "آره", "اره", "باشه", "حتما", "درسته", "اوکی",
            "ہاں", "جی", "جی ہاں", "ٹھیک ہے", "بالکل", "ضرور",
            "हां", "हाँ", "जी", "जी हां", "ठीक है", "बिल्कुल", "जरूर", "haan", "ha", "ji", "theek hai", "bilkul", "zaroor",
            "evet", "tamam", "olur", "tabii", "kesinlikle", "onayla", "aynen",
            "oui", "ouais", "d'accord", "daccord", "bien sûr", "certainement", "confirme",
            "sí", "si", "claro", "vale", "de acuerdo", "por supuesto", "confirmo", "adelante", "dale",
            "ja", "jawohl", "klar", "sicher", "natürlich", "einverstanden", "bestätigen", "mach das", "genau",
            "да", "ага", "конечно", "хорошо", "ладно", "давай", "подтверждаю", "точно", "верно",
            "sì", "certo", "va bene", "d'accordo", "certamente", "confermo", "procedi",
            "sim", "claro", "com certeza", "confirmo", "pode ser", "beleza", "isso",
            "是", "是的", "对", "好", "好的", "可以", "行", "确认", "没问题", "当然",
            "はい", "うん", "ええ", "いいよ", "いいですよ", "オッケー", "もちろん", "確認", "お願いします", "そうです",
        ],
        [Intent.No] =
        [
            "no", "nope", "nah", "not now", "dont", "do not", "never mind", "nevermind", "stop", "forget it", "not really", "negative",
            "لا", "لأ", "كلا", "مو", "مب", "ما ابي", "ما اريد", "لا شكرا", "خلاص لا", "وقف", "توقف", "la", "la2", "ma abi",
            "نه", "خیر", "نخیر", "نمیخوام", "بیخیال",
            "نہیں", "جی نہیں", "نہ", "رہنے دو",
            "नहीं", "ना", "मत", "रहने दो", "nahi", "nahin", "na", "mat", "rehne do",
            "hayır", "yok", "olmaz", "istemiyorum", "vazgeç", "boşver", "dur",
            "non", "pas maintenant", "laisse tomber", "arrête", "annule ça", "je ne veux pas",
            "no", "no gracias", "ahora no", "olvídalo", "detente", "no quiero",
            "nein", "nee", "nicht", "jetzt nicht", "vergiss es", "stopp", "lass es", "will nicht",
            "нет", "не надо", "не сейчас", "забудь", "стоп", "отмена", "не хочу",
            "no", "non ora", "lascia stare", "fermati", "non voglio", "annulla questo",
            "não", "nao", "agora não", "esquece", "deixa pra lá", "não quero",
            "不", "不是", "不要", "不用", "别", "算了", "停", "不对",
            "いいえ", "いや", "やめて", "やめとく", "結構です", "だめ", "違う", "ストップ",
        ],
    };

    /// <summary>
    /// Polite fillers and glue words stripped from a message before using the
    /// remainder as a food-search query ("please give me 2 pizzas" → "pizzas").
    /// </summary>
    /// <summary>
    /// How people ask whether you sell something: "do you have pizza", "عندكم بيتزا",
    /// "پیتزا دارید", "pizza var mı". The frame carries no meaning for a menu search —
    /// the dish is what is left once it is taken off — so it is removed before the
    /// query goes out. Longest first: "do you have" must win over "you have".
    /// </summary>
    public static readonly string[] AskForms =
    [
        // English
        "do you have any", "do you guys have", "do u have any", "do you have", "do u have",
        "do you sell", "do you serve", "do you make", "do you do", "have you got any",
        "have you got", "you got any", "got any", "is there any", "are there any",
        "do you got", "you have any", "you have", "u have", "can i get", "can i have",
        "could i get", "i would like", "i'd like", "id like", "looking for", "i want",
        "i need", "is there", "any chance you have",
        // Arabic
        "هل عندكم", "هل عندك", "هل يوجد", "هل في", "في عندكم", "فيه عندكم", "عندكم",
        "عندك", "يوجد", "تبيعون", "بتبيعوا", "ابغى", "ابي", "عايز", "عاوز", "بدي",
        // Persian
        "ایا دارید", "آیا دارید", "شما دارید", "دارید", "دارین", "داری", "موجود هست",
        "موجوده", "هست", "میخوام", "می‌خوام", "میخواهم",
        // Urdu
        "کیا آپ کے پاس", "آپ کے پاس", "کیا ہے", "ملے گا", "چاہیے",
        // Hindi
        "क्या आपके पास", "आपके पास", "है क्या", "मिलेगा", "चाहिए",
        // Turkish
        "sizde var mı", "var mı", "var mi", "satıyor musunuz", "istiyorum",
        // German
        "habt ihr", "haben sie", "gibt es", "ich möchte", "ich hätte gern",
        // Spanish
        "tienen", "tienes", "hay", "quiero", "me gustaría",
        // French
        "est ce que vous avez", "avez vous", "vous avez", "il y a", "je voudrais", "je veux",
        // Italian
        "avete", "ci sono", "c'è", "vorrei", "voglio",
        // Portuguese
        "vocês têm", "voces tem", "vocês tem", "tem", "há", "quero", "queria",
        // Russian
        "у вас есть", "есть ли", "есть", "хочу", "хотел бы",
        // Japanese and Chinese: no spaces, matched as substrings
        "ありますか", "はありますか", "ください", "有没有", "你们有", "有吗", "我要",
    ];

    public static readonly string[] Fillers =
    [
        "please", "pls", "plz", "a", "an", "the", "some", "one", "of", "for", "me", "to", "and", "with",
        "من فضلك", "لو سمحت", "الله يخليك", "رجاء", "و", "يا",
        "لطفا", "خواهشا",
        "براہ کرم", "پلیز", "مہربانی",
        "कृपया", "प्लीज", "जरा", "please", "zara",
        "lütfen", "bir", "tane", "ve",
        "s'il vous plaît", "s'il te plaît", "svp", "stp", "un", "une", "des", "du", "de", "la", "le", "les", "et",
        "por favor", "porfa", "un", "una", "unos", "unas", "de", "el", "la", "los", "las", "y",
        "bitte", "ein", "eine", "einen", "und", "mit", "von", "der", "die", "das",
        "пожалуйста", "пожалуйста", "один", "одну", "и", "с",
        "per favore", "per piacere", "un", "una", "uno", "e", "di", "il", "lo", "la",
        "por favor", "um", "uma", "e", "de", "o", "a", "os", "as",
        "请", "麻烦", "帮我", "给我",
        "お願いします", "ください", "を", "が", "の", "と", "で",
    ];

    /// <summary>Number words → value, across all supported languages (folded at init by BotNlu).</summary>
    public static readonly Dictionary<string, int> NumberWords = new()
    {
        // en
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["couple"] = 2, ["dozen"] = 12,
        // ar (Gulf variants included; folded forms — ة→ه handled by folding)
        ["واحد"] = 1, ["واحدة"] = 1, ["وحده"] = 1, ["اثنين"] = 2, ["اثنان"] = 2, ["اتنين"] = 2, ["ثنتين"] = 2,
        ["ثلاثة"] = 3, ["ثلاث"] = 3, ["تلاتة"] = 3, ["اربعة"] = 4, ["أربعة"] = 4, ["اربع"] = 4,
        ["خمسة"] = 5, ["خمس"] = 5, ["ستة"] = 6, ["ست"] = 6, ["سبعة"] = 7, ["سبع"] = 7,
        ["ثمانية"] = 8, ["ثمان"] = 8, ["تسعة"] = 9, ["تسع"] = 9, ["عشرة"] = 10, ["عشر"] = 10,
        // fa
        ["یک"] = 1, ["يك"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["پنج"] = 5,
        ["شش"] = 6, ["هفت"] = 7, ["هشت"] = 8, ["نه"] = 9, ["ده"] = 10, ["دوتا"] = 2, ["یدونه"] = 1,
        // ur
        ["ایک"] = 1, ["تین"] = 3, ["چار"] = 4, ["پانچ"] = 5, ["چھ"] = 6,
        ["سات"] = 7, ["آٹھ"] = 8, ["نو"] = 9, ["دس"] = 10,
        // hi (Devanagari + roman)
        ["एक"] = 1, ["दो"] = 2, ["तीन"] = 3, ["चार"] = 4, ["पांच"] = 5, ["पाँच"] = 5,
        ["छह"] = 6, ["सात"] = 7, ["आठ"] = 8, ["नौ"] = 9, ["दस"] = 10,
        ["ek"] = 1, ["do"] = 2, ["teen"] = 3, ["char"] = 4, ["panch"] = 5, ["paanch"] = 5,
        ["chhe"] = 6, ["saat"] = 7, ["aath"] = 8, ["nau"] = 9, ["das"] = 10,
        // tr
        ["bir"] = 1, ["iki"] = 2, ["üç"] = 3, ["uc"] = 3, ["dört"] = 4, ["dort"] = 4, ["beş"] = 5, ["bes"] = 5,
        ["altı"] = 6, ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10,
        // fr
        ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5,
        ["six"] = 6, ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10,
        // es
        ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
        ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10,
        // de
        ["ein"] = 1, ["eins"] = 1, ["eine"] = 1, ["einen"] = 1, ["zwei"] = 2, ["drei"] = 3, ["vier"] = 4,
        ["fünf"] = 5, ["funf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9, ["zehn"] = 10,
        // ru
        ["один"] = 1, ["одну"] = 1, ["одна"] = 1, ["два"] = 2, ["две"] = 2, ["три"] = 3, ["четыре"] = 4,
        ["пять"] = 5, ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10,
        ["пару"] = 2, ["пара"] = 2,
        // it
        ["due"] = 2, ["tre"] = 3, ["quattro"] = 4, ["cinque"] = 5,
        ["sei"] = 6, ["sette"] = 7, ["otto"] = 8, ["nove"] = 9, ["dieci"] = 10,
        // pt
        ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["três"] = 3, ["tres"] = 3,
        ["quatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["sete"] = 7, ["oito"] = 8, ["nove"] = 9, ["dez"] = 10,
    };

    /// <summary>CJK numerals (matched per-character inside unsegmented text).</summary>
    public static readonly Dictionary<char, int> CjkNumbers = new()
    {
        ['一'] = 1, ['二'] = 2, ['两'] = 2, ['俩'] = 2, ['三'] = 3, ['四'] = 4, ['五'] = 5,
        ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9, ['十'] = 10,
    };

    /// <summary>
    /// Hand-curated phrase additions from evaluation-corpus miss analysis —
    /// same precedence tier as <see cref="Phrases"/> (always beats generated).
    /// </summary>
    public static readonly Dictionary<Intent, string[]> PatchPhrases = new()
    {
        [Intent.StartOrder] =
        [
            "آرڈر لگا دو", "آرڈر لگا دیں", "آرڈر کر دو", "آرڈر کر دیں", "چاہیے",
            "برام ثبت کن", "ثبت کن برام", "سفارش بدی", "برام سفارش",
            "pídeme", "pide un", "pide una", "pede um", "pede uma", "me pede", "me ve", "me vê",
            "podrias pedir", "me podrias pedir", "pedirme",
            "commande moi", "commandes moi", "passer une commande",
            "hätte gerne", "ich hätte gerne",
            "भेज दो", "भेजो", "भिजवा दो", "मंगवाओ", "खाना है", "मुझे खाना है",
            "想吃", "好饿", "来俩", "来两个", "来一个", "帮我点", "帮我定", "给我来",
            "たべたい", "くいたい", "ちゅうもんで", "ちゅうもんしたい", "お願いしたいのですが", "頼みたいのですが",
            "можно мне", "закажи мне", "хачу", "alayım", "einmal",
            "هوس کردم", "آشپزی ندارم", "چی داری",
            "söylemek istiyorum", "menü söyle",
        ],
        [Intent.TrackOrder] =
        [
            "انقبل الطلب", "الطلب انقبل", "وين وصل", "اخبار طلبي", "ايش اخبار الطلب", "قبلوا الطلب",
            "order kahan hy", "order kahan he", "kb ayega", "kab ayega", "kb tk ayega",
            "قبول کر لیا", "قبول کیا", "قبول ہوا", "آرڈر کہاں پہنچا", "کب پہنچے گا",
            "accepté ma commande", "commande acceptée", "combien de temps",
            "ya acepto mi pedido", "aceptaron mi pedido", "pedido aceptado", "cuanto falta",
            "кто везет", "сколько ждать", "сколько еще ждать", "долго ждать",
            "غذام کو", "غذا کو", "منتظرم",
            "कितनी देर", "कब आएगा", "कौन डिलीवर", "नहीं आया", "अभी तक नहीं आया", "कहाँ है",
            "几点能送到", "什么时候能到", "几点送到", "接单了没", "接单了吗", "商家接单",
            "受け付けてくれましたか", "受け付けた", "受け付けましたか", "だれが配達",
            "on the way", "on its way", "accepted my order", "restaurant accepted",
            "onde ta", "demorando", "cade a comida",
            "چه مرحله", "وضعیتش", "قرار بود برسه",
            "teslimat süresi", "کس اسٹیج",
            "متا يوصل", "متا بيوصل",
            "एक्सेप्ट हुआ", "स्वीकार हुआ",
        ],
        [Intent.CancelOrder] =
        [
            "منسوخ", "منسوخ کرنا", "منسوخ کر دیں", "آرڈر منسوخ کرنا", "آرڈر منسوخ کرنا ہے",
            "کینسل کر دیں", "کینسل کر دو", "آرڈر کینسل کر دیں", "کنسل کردو", "آرڈر کنسل کردو",
            "とりけして", "とりけしたい", "きゃんせる", "きゃんせるしたい",
            "退掉", "取消这单", "这单取消",
        ],
        [Intent.ShowCart] =
        [
            "what i added", "added so far", "what did i add",
            "ya quoi dans mon panier", "quoi dans mon panier", "quoi dans le panier", "c'est quoi mon panier",
            "कुल बिल", "बिल कितना", "कुल कितना",
            "中身を見せて", "中身見せて",
            "quanto ta dando", "qnt ta dando", "quanto deu", "quanto ficou",
            "cuanto llevo", "cuánto llevo",
            "مجموع الطلب", "المجموع", "كم المجموع", "مجموع",
        ],
        [Intent.ClearCart] =
        [
            "delete everything", "borra todo", "apaga tudo", "supprime tout", "vide tout",
            "alles löschen", "alles leeren", "удали все", "очисти все", "cancella tutto",
            "svuota tutto", "hepsini sil", "her şeyi kaldır", "hepsini kaldır", "sepeti sıfırla",
            "سب کچھ ہٹاؤ", "سب کچھ ہٹانا", "ٹوکری صاف", "سب ہٹا دو",
            "सब हटा दो", "सब कुछ हटाओ",
            "احذف جميع", "احذف الكل", "احذف كل شي",
            "پاک کن", "همه رو پاک کن",
            "全删", "都删了", "全部删掉", "都不要了", "全部清空",
            "ぜんぶ消して", "まるごとクリア", "からっぽにして",
            "herseyi kaldir", "herşeyi kaldır", "herseyi sil", "baştan başla", "bastan basla",
            "कार्ट खाली कर दो", "खाली कर दो",
            "supprime tout le panier",
            "همرو حذف کن", "همه رو حذف کن",
            "cart clear", "sara saman nikal",
        ],
        [Intent.RemoveItem] =
        [
            "remove from my cart", "delete from my cart", "take out of my cart",
            "nimm raus", "nur noch eine", "statt zwei",
            "quítame el", "quitame el", "saca el",
            "tire o", "pode tirar",
            "enlève le", "retire le",
            "выкинь", "выбрось", "убери из",
            "invece di", "togline", "metti solo",
            "sayısını düşür", "düşür",
            "شيل من", "طلع من", "طلع",
            "بردار", "از سبد بردار", "حذفش کن",
            "ریمو کر دیں", "نکال دیں", "کم کر کے",
            "निकालो", "रिमूव",
            "减一份", "减一个", "去掉一个", "拿掉",
            "はずして", "ぬいて", "へらして",
            "احذفلي", "احذف لي", "raus damit",
            "supprime du panier", "supprime panier", "retire du panier", "enleve du panier",
            "tirar do carrinho", "tira do carrinho",
            "aus dem warenkorb",
        ],
        [Intent.Checkout] =
        [
            "ارسل الاوردر", "ارسل الطلب", "اعتمد الطلب", "كمل عملية الشراء", "خلصت", "ادفع واخلص",
            "khalast", "ersil el order", "yalla ersil",
            "آرڈر مکمل کریں", "آرڈر مکمل کرو", "ادائیگی کرنا", "مکمل کریں",
            "ödemeyi", "ödeme yapalım", "ödeyelim",
            "gib die bestellung auf",
            "注文かくてい", "かくてい", "以上で", "いじょうで",
            "买单", "结账吧",
            "فائنل کر دو", "آرڈر فائنل", "پیمنٹ", "آرڈر پلیس کر دو", "بس اتنا ہی",
            "میں ادائیگی کرنا چاہتا ہوں", "ادائیگی کرنا چاہتا ہوں",
            "प्लेस कर दो", "ऑर्डर प्लेस",
            "ödemeyi yapıp", "ödeme yapıp",
            "همینارو ثبت", "把单下了", "下单吧", "选好了",
        ],
        [Intent.Reorder] =
        [
            "نفس اللي طلبته", "اللي طلبته امس",
            "same thing", "the same thing",
            "lo de ayer", "lo mismo de ayer",
            "la meme commande", "même commande",
            "o mesmo pedido", "msm pedido", "faz o msm",
            "genau das selbe", "das selbe wie gestern",
            "то же самое", "как вчера",
            "come l'altra volta", "rifammi l'ordine", "stesso ordine", "ordine di ieri",
            "refaire ma commande", "refais ma commande", "refaire la commande",
            "دوباره سفارش",
            "फिर से लगा",
            "おなじの", "前のやつ",
            "一样的", "跟上次一样",
        ],
        [Intent.Greeting] = ["eaee", "дарова"],
        [Intent.Thanks] = ["3q", "帮大忙", "merci pour tout", "gentil"],
        [Intent.Help] =
        [
            "توضیح بده", "چجوری کار",
            "اشرحلي", "اشرح لي", "شو تقدر", "وش الخدمات", "ما هي الخدمات", "كيف استخدمه", "علموني وش",
            "what commands", "which commands", "walk me through",
            "हेल्प चाहिए", "हेल्प करो", "कैसे चलाते", "ऐप कैसे", "क्या काम कर सकता", "पूरी लिस्ट बता",
            "کون کون سے کام", "کیا کام کروا",
            "neler yapabiliyorsun", "anlatır mısın", "anlatsana", "nasıl kullanılır", "hangi komutlar",
            "oq vc consegue fazer", "o que vc faz", "oq voce faz", "como uso",
            "どうやってつかうの", "つかいかた",
            "你都能干啥", "能干啥", "干啥的", "咋用",
        ],
        [Intent.Yes] = ["yess", "可以滴", "可以的", "好滴", "好哒", "嗯嗯", "おけ", "それでおけ", "perfetto", "si perfetto"],
        [Intent.No] =
        [
            "noo", "nein danke", "non merci", "нет спасибо", "não obrigado", "nao obrigado",
            "hayır teşekkürler", "نه ممنون", "不行", "别这样",
        ],
    };

    /// <summary>
    /// Hand-curated keyword additions/overrides — beat generated keywords on
    /// intent conflicts. "Order" words make good WEAK signals here even though
    /// they are too generic to be phrases.
    /// </summary>
    public static readonly Dictionary<Intent, string[]> PatchKeywords = new()
    {
        [Intent.StartOrder] =
        [
            "order", "buy", "wanna", "quiero", "ordenar", "commande", "commander", "bestellen",
            "ordinare", "ordinami", "pedir", "pedido", "sipariş", "заказать", "закажи", "хочу",
            "ارڈر", "اطلب", "سفارش", "ऑर्डर", "alayım",
            // want-verbs: safe weak signals — action phrases always outrank them
            "want", "veux", "quero", "queria", "voglio", "vorrei", "möchte", "mochte",
            "istiyorum", "میخوام", "ابغى", "ابغي", "ابي", "بدي", "ودي", "اريد", "عايز", "بغيت",
            "چاہیے", "chahiye", "kiero",
        ],
        [Intent.TrackOrder] = ["مستني", "منتظر", "livreur", "ждать", "aspetto", "پہنچا", "پیک", "پیکش", "میاره", "teslimat", "स्टैटस", "स्टेटस", "مرحله", "एक्सेप्ट",
            // Naming the driver/courier is almost always "where is my order?".
            "repartidor", "fahrer", "entregador", "fattorino", "курьер", "waynah"],
        [Intent.ShowCart] = ["مجموع", "السلة", "بالسلة", "سلتي"],
        [Intent.RemoveItem] = ["raus", "levane", "levami", "levalo", "toglimi", "quitame", "quítame", "sacame", "sácame", "levane"],
    };

    /// <summary>
    /// Specific machine-generated entries that proved toxic in evaluation
    /// (they hijack other intents' sentences) — dropped at merge time, for
    /// both phrases and keywords.
    /// </summary>
    public static readonly string[] BannedGeneratedPhrases =
    [
        "non lo voglio più",   // it: "I don't want it anymore" — RemoveItem/No, not CancelOrder
        "danke danke",
        "c'est tout",          // fr: swallows "merci pour tout"
        "grande",              // it: ordinary adjective, not thanks
        "mil",                 // es: "mil gracias" fragment — collides with "mi"
        "chef",                // it: "grazie chef" fragment
        "minus",               // en: collides with "mins"
        "commands",            // en: one edit away from fr "commande"
        "durum", "durumda",    // tr: "status" — also the dürüm wrap
        "بلدی",                // fa: collides with بدی "you give"
        "اكي",                 // ar-chat "oki" — one swap from Urdu ایک "one"
        "wars",                // de "war's" fragment — one edit from "was"
        "la ayuda",            // es thanks fragment — beats a plain "ayuda" помощь request
        "dalle",               // it — one edit from es "dale"
        "allez",               // fr fragment — one edit from de "alles"
        "order it",            // en — swallows "cancel my order, it says..."
        "c'est", "cest",       // fr fragments
        "غيرت رأيي",           // "changed my mind" — remove/clear/cancel all use it
        "changed my mind", "ho cambiato idea", "cambiato idea", "mudei de ideia", "mudei ideia",
        "cambié de opinión", "j'ai changé d'avis",
        "ma",                  // fr/it function word leaked in as a No answer
        "meno",                // it "less" — one edit from "menu"
        "sera",                // it "evening" fragment
        "ऑर्डर लगा दो",        // hi "place an order" — start-vs-checkout ambiguous
        "आर्डर लगा दो",
        "ثبت کن",              // fa "register it" — start-vs-checkout ambiguous
        "quoi dans panier",    // fr fragment — hijacks clear-the-cart sentences
        "آرڈر کرنا ہے",        // ur: "want to order" — swallows cancel/checkout sentences
        "میں آرڈر کرنا چاہتا ہوں",
        "order kar do",        // hi-roman: claimed by several intents at once
        // Mined CJK fragments that are really just "from the cart" or a drink NAME —
        // they contain 购物车 (cart), so as RemoveItem they fire on every show/clear
        // sentence, and 珍珠奶茶 (bubble tea) is a product, not a command.
        "从购物车里", "从购物车删", "了帮我删掉", "删掉其中一", "掉其中一份", "珍珠奶茶",
        // Everyday words the generated set tagged as standalone Reorder signals. They
        // fire on greetings ("hello AGAIN my FAVORITE robot", "better than last time",
        // "nicht so viel los wie letztes Mal") and quietly re-add a past order. The real
        // intent still rides on "reorder", "usual", "repeat" and "order again".
        "again", "same", "favorite", "favourite",
        "wie letztes mal", "wie letztes", "otra vez", "la otra vez", "ultima vez", "la ultima vez",
    ];

    /// <summary>
    /// High-signal marker words per Latin-script language, used only for
    /// language *detection* (never for intent). Exact folded matches only.
    /// </summary>
    public static readonly Dictionary<string, string[]> LatinMarkers = new()
    {
        ["en"] = ["i", "the", "my", "want", "order", "where", "is", "please", "yes", "hello", "thanks", "food", "hungry", "and", "can", "you"],
        ["tr"] = ["sipariş", "siparis", "istiyorum", "nerede", "merhaba", "evet", "hayır", "hayir", "lütfen", "lutfen", "sepet", "teşekkürler", "tesekkurler", "acıktım", "ödeme", "ve", "bir"],
        ["fr"] = ["je", "veux", "voudrais", "commande", "commander", "où", "ou", "est", "ma", "oui", "bonjour", "merci", "panier", "faim", "livraison", "s'il", "vous"],
        ["es"] = ["quiero", "pedido", "pedir", "dónde", "donde", "está", "esta", "mi", "sí", "hola", "gracias", "carrito", "hambre", "por", "favor", "cuándo"],
        ["de"] = ["ich", "möchte", "mochte", "bestellen", "bestellung", "wo", "ist", "meine", "ja", "hallo", "danke", "warenkorb", "hunger", "bitte", "und", "essen"],
        ["it"] = ["voglio", "vorrei", "ordine", "ordinare", "dove", "dov'è", "il", "mio", "sì", "ciao", "grazie", "carrello", "fame", "per", "favore", "quando"],
        ["pt"] = ["quero", "pedido", "pedir", "onde", "está", "esta", "meu", "sim", "olá", "ola", "obrigado", "obrigada", "carrinho", "fome", "cadê", "cade", "você", "por", "favor"],
        // Romanized Hindi/Urdu chat ("Hinglish") — reply stays in the UI language,
        // but these stop the detector from mistaking such messages for English.
        ["hi"] = ["mujhe", "chahiye", "karo", "kahan", "hai", "mera", "bhook", "khana", "haan", "nahi", "shukriya", "dikhao"],
    };

    /// <summary>Distinctive characters that immediately pin a Latin-script language.</summary>
    public static readonly (char Ch, string Locale)[] LatinHintChars =
    [
        ('ş', "tr"), ('ğ', "tr"), ('ı', "tr"), ('İ', "tr"),
        ('ñ', "es"), ('¿', "es"), ('¡', "es"),
        ('ß', "de"),
        ('ã', "pt"), ('õ', "pt"),
        ('œ', "fr"),
    ];

    /// <summary>
    /// Counter/measure words that belong to the QUANTITY, not to the product name.
    /// "دو عدد پیتزا" is two pizzas — without this, "عدد" travelled into the search query
    /// and the spell-corrector turned "دو عدد" into "دوغ عدس" (yoghurt drink + lentils).
    /// </summary>
    public static readonly string[] CounterWords =
    [
        "عدد", "حبة", "حبه", "حبات", "قطعة", "قطعه", "قطع", "علبة", "علبه", "كوب", "أكواب",
        "تا", "دونه", "دانه", "عددی", "دست",
        "piece", "pieces", "pcs", "pc", "unit", "units", "order", "orders", "portion", "portions",
        "adet", "tane", "porsiyon",
        "штук", "шт", "штуки", "порция", "порции",
        "पीस", "नग", "प्लेट", "عدد",
        "unidad", "unidades", "pieza", "piezas", "stück", "pezzi", "pezzo", "peça", "peças",
    ];

    /// <summary>
    /// Words that join separate items in one spoken order ("2 pizzas AND a coke").
    /// Deliberately excludes "with"-type particles: "tea with milk" and "برجر مع جبن" are
    /// ONE product with a topping, and splitting there would invent an order the customer
    /// never placed.
    /// </summary>
    public static readonly string[] ItemConnectors =
    [
        "and", "plus", "also", "then",
        "و", "وايضا", "وأيضا", "كمان", "بعد",
        "همچنین", "بعلاوه",
        "اور",
        "ve", "bir de", "ayrıca",
        "et", "ainsi",
        "y", "e", "además", "tambien", "también",
        "und", "sowie", "dazu",
        "ed", "inoltre",
        "и", "плюс", "ещё", "еще", "также",
        "और", "तथा",
        "と", "そして", "それと", "あと",
        "和", "还有", "加",
    ];

    /// <summary>
    /// Particles that introduce a TOPPING or option rather than another product. Once one
    /// of these appears, everything after it belongs to the dish already named — "a burger
    /// with cheese and tomato" is one burger, not a burger plus tomatoes.
    /// </summary>
    public static readonly string[] ModifierParticles =
    [
        "with", "without", "extra",
        "مع", "بدون", "بلا",
        "با", "بدون",
        "ساتھ", "بغیر",
        "ile", "ekstra",
        "avec", "sans",
        "con", "sin",
        "mit", "ohne",
        "senza",
        "com", "sem",
        "с", "без",
        "साथ", "बिना",
    ];

    /// <summary>
    /// Dish names that contain a connector. Splitting these invents an order the customer
    /// never placed ("fish and chips" is one plate, not fish plus chips).
    /// </summary>
    public static readonly string[] CompoundDishNames =
    [
        "fish and chips", "mac and cheese", "macaroni and cheese", "surf and turf",
        "bangers and mash", "sweet and sour", "chicken and waffles", "peaches and cream",
        "rice and beans", "beans and toast", "salt and pepper", "cheese and tomato",
        "ham and cheese", "bacon and egg", "milk and honey", "corn on the cob",
        "لحم ودجاج", "رز ودجاج",
    ];

    /// <summary>
    /// Everyday Arabic words that begin with the letter waw. The waw prefix normally means
    /// "and" and is written stuck to the next word ("وبيبسي" = "and a Pepsi"), so it gets
    /// peeled off — but never for these.
    /// </summary>
    public static readonly string[] WawWords =
    [
        "وجبة", "وجبه", "وجبات", "وسط", "ورق", "وقت", "وصل", "وفر", "ولد", "وردة", "ورده",
        "وسادة", "وساده", "ورقة", "ورقه", "وعاء", "وزن", "وطن", "ولاية", "ولايه", "واحد",
        "وايد", "وين", "ويش", "وش", "والله", "وسخ", "ودي", "وصفة", "وصفه",
    ];

    /// <summary>
    /// Words for the money itself. A number next to one of these is a BUDGET, not a
    /// quantity — "I have 5 rials" asks what 5 rials buys, it does not order 5 of anything.
    /// </summary>
    public static readonly string[] CurrencyWords =
    [
        "omr", "rial", "rials", "riyal", "riyals", "baisa", "baisas",
        "ريال", "ريالات", "ريالا", "بيسة", "بيسه",
        "ریال", "ریالات",
        "روپے", "روپیہ",
        "money", "budget", "فلوس", "مصاري", "پول", "بجٹ",
        "para", "bütçe", "argent", "dinero", "geld", "soldi", "dinheiro", "деньги", "पैसे",
        "钱", "预算", "お金", "予算",
    ];

    /// <summary>
    /// "Either/or" words. Inside one slot of a budget question they offer alternatives:
    /// "which pizza can I get with a pepsi OR water".
    /// </summary>
    public static readonly string[] OrConnectors =
    [
        "or", "either",
        "او", "أو", "ولا",
        "یا", "یاا",
        "ya", "veya", "yada", "ya da",
        "ou", "o", "oder", "oppure", "ou então",
        "или", "либо",
        "या",
        "或", "或者", "还是",
        "または", "か",
    ];

    /// <summary>
    /// The words a budget question is made of — "what CAN I BUY with 5 rials". They are not
    /// products, and searching them returns nonsense: "چی می تونم بخرم" was matching bottled
    /// water. Only ever applied to a message that already stated an amount.
    /// </summary>
    public static readonly string[] BudgetQuestionWords =
    [
        "what", "which", "can", "could", "should", "buy", "get", "afford", "have", "want",
        "me", "my", "for", "the", "some", "something", "anything", "eat", "order", "much",
        "چی", "چه", "چیا", "چیزی", "بخرم", "بخورم", "بگیرم", "تونم", "توانم", "میتونم",
        "بتونم", "خوام", "میخورم", "کدوم", "کدام", "چند", "برام", "دارم", "بدی", "میشه",
        // Bare Persian particles: the verb prefix "می", the article "یه", the object
        // marker "را/رو". They survive noise-stripping but are never food.
        "می", "یه", "را", "رو", "هم", "بشه", "باشه",
        "وش", "ايش", "إيش", "شنو", "شو", "اقدر", "أقدر", "اشتري", "أشتري", "اخذ", "آخذ",
        "ماذا", "ايه", "إيه", "ممكن", "عندي", "معي", "اجيب", "أجيب", "ابغى", "ابي", "اكل",
        "کیا", "سکتا", "سکتی", "خرید", "لے", "ملے", "کھا",
        "ne", "neler", "alabilirim", "alırım", "yiyebilirim", "ile", "için",
        "que", "puedo", "comprar", "llevar", "con", "por",
        "quoi", "puis", "acheter", "prendre", "avec", "pour",
        "was", "kann", "kaufen", "bekomme", "für", "mit",
        "cosa", "posso", "comprare", "prendere",
        "que", "posso", "comprar", "levar",
        "что", "могу", "купить", "взять", "на",
        "क्या", "खरीद", "सकता", "सकती", "मिलेगा", "में",
        "什么", "可以", "买", "能买", "买什么",
        "何", "買える", "買えます", "で",
    ];

    /// <summary>
    /// A spread of everyday catalog words used to answer "what can I get for X" when the
    /// customer names no product. Deliberately varied so the suggestions are not all drinks.
    /// </summary>
    public static readonly string[] BudgetSampleTerms =
    [
        "burger", "pizza", "shawarma", "chicken", "rice", "sandwich",
        "salad", "juice", "coffee", "dessert", "fries", "soup",
    ];

    // ─────────────── Talking about what was said a moment ago ───────────────

    /// <summary>
    /// "The first one", "the last one" — a customer points at the list already on screen
    /// instead of typing its number. -1 means the last one.
    /// </summary>
    public static readonly (string Word, int Index)[] OrdinalWords =
    [
        ("first", 1), ("1st", 1), ("second", 2), ("2nd", 2), ("third", 3), ("3rd", 3),
        ("fourth", 4), ("4th", 4), ("fifth", 5), ("5th", 5), ("last", -1), ("final", -1),
        ("الأول", 1), ("الاول", 1), ("اول", 1), ("الثاني", 2), ("ثاني", 2), ("الثالث", 3),
        ("ثالث", 3), ("الرابع", 4), ("الخامس", 5), ("الأخير", -1), ("الاخير", -1), ("اخير", -1),
        ("اولی", 1), ("اولین", 1), ("دومی", 2), ("دومین", 2), ("سومی", 3), ("سومین", 3),
        ("چهارمی", 4), ("پنجمی", 5), ("آخری", -1), ("اخری", -1), ("آخرین", -1),
        ("پہلا", 1), ("پہلی", 1), ("دوسرا", 2), ("دوسری", 2), ("تیسرا", 3), ("آخری", -1),
        ("birinci", 1), ("ikinci", 2), ("üçüncü", 3), ("sonuncu", -1),
        ("premier", 1), ("première", 1), ("deuxième", 2), ("troisième", 3), ("dernier", -1),
        ("primero", 1), ("primera", 1), ("segundo", 2), ("tercero", 3), ("último", -1),
        ("erste", 1), ("zweite", 2), ("dritte", 3), ("letzte", -1),
        ("primo", 1), ("secondo", 2), ("terzo", 3), ("ultimo", -1),
        ("primeiro", 1), ("segundo", 2), ("terceiro", 3), ("último", -1),
        ("первый", 1), ("второй", 2), ("третий", 3), ("последний", -1),
        ("पहला", 1), ("दूसरा", 2), ("तीसरा", 3), ("आखिरी", -1),
        ("第一", 1), ("第二", 2), ("第三", 3), ("最后", -1),
        ("一番目", 1), ("二番目", 2), ("最後", -1),
    ];

    /// <summary>"The cheapest one" / "the most expensive" — pick by price from the last list.</summary>
    public static readonly string[] CheapestWords =
    [
        "cheapest", "cheaper", "lowest", "budget",
        "الأرخص", "الارخص", "ارخص", "أرخص", "الرخيص",
        "ارزانترین", "ارزان‌ترین", "ارزونترین", "ارزون‌ترین", "ارزانتر",
        "سستا", "سستی", "سب سے سستا",
        "ucuz", "enucuz", "en ucuz",
        "moins cher", "plus abordable",
        "más barato", "mas barato", "barato",
        "billigste", "günstigste",
        "più economico", "economico",
        "mais barato",
        "дешевле", "самый дешёвый", "дешёвый",
        "सबसे सस्ता", "सस्ता",
        "最便宜", "便宜",
        "一番安い", "安い",
    ];

    public static readonly string[] PriciestWords =
    [
        "expensive", "priciest", "best", "biggest", "largest",
        "الأغلى", "الاغلى", "اغلى", "أغلى", "الأكبر", "الاكبر",
        "گرانترین", "گران‌ترین", "بزرگترین", "بزرگ‌ترین",
        "مہنگا", "سب سے مہنگا", "بڑا",
        "pahalı", "en pahalı", "en büyük",
        "plus cher", "plus grand",
        "más caro", "mas caro", "más grande",
        "teuerste", "größte",
        "più costoso", "più grande",
        "mais caro", "maior",
        "дороже", "самый дорогой", "большой",
        "सबसे महंगा", "महंगा", "सबसे बड़ा",
        "最贵", "最大",
        "一番高い", "大きい",
    ];

    /// <summary>
    /// "Make it 3", "change it to two" — the customer is correcting the quantity of what
    /// was just put in the basket, not ordering three more of something.
    /// </summary>
    public static readonly string[] ChangeQtyMarkers =
    [
        "make it", "change it", "change to", "instead", "actually", "no make it", "set it",
        "خلها", "خليها", "اجعلها", "غيرها", "بدلها", "خله", "خليه", "عدلها",
        "بکنش", "بکن", "کنش", "عوضش", "عوض کن", "بذارش", "بزارش",
        "کر دو", "بنا دو", "کر دیں",
        "yap", "olsun",
        "mets", "plutôt",
        "ponlo", "cámbialo", "mejor",
        "mach", "ändere",
        "cambia", "fallo",
        "muda", "troca",
        "сделай", "измени", "поменяй",
        "कर दो", "बदल दो",
        "改成", "换成",
        "変更", "にして",
    ];

    /// <summary>
    /// Words that point at the thing just mentioned. "Remove it" only makes sense with the
    /// last thing added, so the bot must recognise the pronoun rather than search for it.
    /// </summary>
    public static readonly string[] ItPronouns =
    [
        "it", "that", "this", "them", "those", "one",
        "هذا", "هذه", "ذاك", "ذلك", "اياه", "إياه", "هذي", "هالشي",
        "این", "اون", "آن", "همین", "همون", "اونو", "اینو",
        "یہ", "وہ", "اسے", "اس",
        "bunu", "şunu", "onu",
        "ça", "cela", "celui",
        "eso", "esto", "ese",
        "das", "es", "dies",
        "questo", "quello",
        "isso", "isto",
        "это", "то", "его",
        "यह", "वो", "इसे",
        "这个", "那个",
        "これ", "それ",
    ];

    /// <summary>Urdu-specific letters (Arabic script, but never plain Arabic).</summary>
    public static readonly HashSet<char> UrduChars = ['ٹ', 'ڈ', 'ڑ', 'ں', 'ھ', 'ے', 'ۃ', 'ہ'];

    /// <summary>
    /// Persian letters that plain Arabic never uses (Urdu shares them, so Urdu's
    /// own distinctive letters are checked first).
    /// </summary>
    public static readonly HashSet<char> PersianChars = ['پ', 'چ', 'ژ', 'گ', 'ک', 'ی'];

    /// <summary>
    /// Letters Persian and Urdu never use — plain Arabic only. Deliberately short:
    /// أ إ ؤ ئ all appear in Persian loanwords, so they prove nothing.
    /// </summary>
    public static readonly HashSet<char> ArabicOnlyChars = ['ة', 'ى'];

    /// <summary>
    /// Everyday words that tell Persian, Arabic and Urdu apart. The three share a script and
    /// a customer often writes a whole sentence with no letter that separates them
    /// ("سفارش", "آب", "سلام") — vocabulary is then the only evidence there is. Only words
    /// a customer of ONE of the three would actually type belong here; anything the
    /// languages share (لطفا, سلام, تمام) would cast a false vote and is left out.
    /// </summary>
    public static readonly (string Locale, string[] Words)[] ScriptMarkers =
    [
        ("fa",
        [
            "سفارش", "سفارشم", "کجاست", "کجاس", "کجاست؟", "چقدر", "چند", "میخوام", "می‌خوام",
            "میخواهم", "خوام", "بخرم", "دارم", "دارید", "داری", "هست", "نیست", "هستم", "باشه",
            "بله", "آره", "نه", "خیر", "ممنون", "مرسی", "متشکرم", "تشکر", "نوشابه", "غذا",
            "پیک", "دوتا", "سه‌تا", "یه", "یکی", "برام", "برای", "هنوز", "نرسید", "نرسیده",
            "میرسه", "می‌رسه", "بیار", "بیارید", "بفرست", "دیگه", "خیلی", "زود", "دیر",
            "سبد", "خرید", "کدوم", "کدام", "چیه", "چیست", "همین", "بدید", "بده", "نمیخوام",
            "چطور", "چرا", "الآن", "عجله", "گشنمه", "گرسنمه", "میخورم", "بخورم",
            "آدرس", "شده", "شد", "عوض", "معدنی", "دوباره", "بشه", "حتما", "فقط",
            "سفارشم", "سفارشمو", "لغو", "شود", "بفرست", "بفرستید", "صورتحساب", "راننده",
            "منتظرم", "خام", "ماست", "منزل", "طبقه", "بدهد", "خورشت", "ثبت", "خونه",
            "دستت", "نداشت", "نبود", "نشد", "نرسده", "نرسیده", "دوغ", "برنج", "پرس",
            "بذارید", "کنین", "کنید", "میکشه", "چنده", "خالیه", "نیومده", "بخیر",
        ]),
        ("ar",
        [
            "ابغى", "أبغى", "ابغا", "ابي", "أبي", "عايز", "عاوز", "بدي", "اريد", "أريد",
            "وين", "فين", "أين", "كم", "طلبي", "الطلب", "طلبيتي", "سلة", "السلة", "سلتي",
            "توصيل", "مطعم", "المطعم", "شكرا", "شكرًا", "نعم", "ايوه", "أيوه", "فضلك",
            "سمحت", "الحساب", "ادفع", "الغي", "ألغي", "مرحبا", "هلا", "اللي", "شنو", "وش",
            "هذا", "هذه", "ذلك", "متى", "ليش", "ليه", "عشان", "خلاص", "ماشي", "جوعان",
            "بسرعة", "وصل", "وصلني", "ودي", "احتاج", "أحتاج",
        ]),
        ("ur",
        [
            "مجھے", "چاہیے", "چاہئے", "کہاں", "شکریہ", "براہ", "مہربانی", "آرڈر", "کھانا",
            "کتنا", "کتنے", "کتنی", "میرا", "میری", "ہاں", "نہیں", "کریں", "دیں", "ابھی",
            "جلدی", "بھیج", "بھیجیں", "پہنچا", "ٹھیک", "اچھا", "بھوک", "کھانے",
            // Urdu written WITHOUT its own letters is indistinguishable from Persian by
            // script alone, so these everyday words carry the whole burden.
            "پارسل", "کرو", "کردو", "پلیز", "پلز", "واپس", "فرمائش", "فرمایش", "کدر",
            "بلکل", "بالکل", "کوپن", "زبردست", "بریانی", "سالن", "آدمی", "اضافی", "مرچ",
            "گوشت", "بوتل", "کیچپ", "سموسا", "آفر", "رعایت", "شکایت", "کنفرم", "پتا",
            "دوبارا", "کوشش", "سست", "سائز", "فیملی", "لارج", "زنگر", "چکن", "فرائیز",
            "ادائیگی", "ادایگی", "مزیدار", "نکلا", "نکلی", "گیا", "گئی", "جی", "اور",
            "کیا", "روٹی", "نہاری", "کریم", "رول", "لگ", "لگا", "دیر",
        ]),
    ];
}
