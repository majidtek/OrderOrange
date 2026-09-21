using OrderOrange.ClientCore.Services;

namespace OrderOrange.Tests;

/// <summary>
/// The five million generated stores are localized by taking their English names and
/// blurbs apart, so the two things that must never break are: a real partner's own
/// name is left exactly as typed, and English itself is a no-op.
/// </summary>
public class StoreTextTests
{
    private static readonly string[] Locales =
        ["en", "ar", "fa", "ur", "hi", "tr", "fr", "es", "de", "ru", "it", "pt", "zh", "ja"];

    [Fact]
    public void English_IsUnchanged()
    {
        const string name = "Golden Samosa House - Bawshar";
        const string desc = "The home of samosa lovers in Bawshar, Muscat.";

        Assert.Equal(name, StoreText.Name(name, "en"));
        Assert.Equal(desc, StoreText.Description(desc, "en"));
        Assert.Equal("Bawshar, Muscat", StoreText.Area("Bawshar, Muscat", "en"));
    }

    [Theory]
    [InlineData("Bella Napoli")]
    [InlineData("Zayn's Kaffee Klatsch")]
    [InlineData("مطعم الوليد")]
    public void UnknownNames_AreLeftAlone(string name)
    {
        foreach (var locale in Locales)
            Assert.Equal(name, StoreText.Name(name, locale));
    }

    [Fact]
    public void HandWrittenDescription_IsLeftAlone()
    {
        const string desc = "Levantine mezze, kibbeh and charcoal grills the Beirut way.";
        foreach (var locale in Locales)
            Assert.Equal(desc, StoreText.Description(desc, locale));
    }

    /// <summary>Locales that don't use the Latin alphabet, so an Omani place name is respelt.</summary>
    private static readonly string[] NonLatin = ["ar", "fa", "ur", "hi", "ru", "zh", "ja"];

    [Fact]
    public void GeneratedName_IsTranslatedInEveryLanguage()
    {
        const string name = "Golden Samosa House - Bawshar";
        foreach (var locale in Locales.Where(l => l != "en"))
        {
            var got = StoreText.Name(name, locale);
            Assert.NotEqual(name, got);
            Assert.DoesNotContain("sw.", got, StringComparison.Ordinal);   // no leaked keys
            Assert.DoesNotContain("sp.", got, StringComparison.Ordinal);

            // A Latin-script language legitimately keeps "Samosa" and "Bawshar" as written
            // (Spanish for samosa is samosa); only a different script must respell them.
            if (NonLatin.Contains(locale))
            {
                Assert.DoesNotContain("Samosa", got, StringComparison.Ordinal);
                Assert.DoesNotContain("Bawshar", got, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("The home of samosa lovers in Bawshar, Muscat.")]
    [InlineData("A modern taste of harees from the heart of Sur.")]
    [InlineData("Authentic thareed made fresh daily in Salalah.")]
    [InlineData("Family recipes and the best biryani in Al Batinah.")]
    [InlineData("pizza done right — fast delivery across Muscat.")]
    [InlineData("Groceries, fresh produce and daily essentials delivered fast.")]
    [InlineData("Medicines, vitamins, baby care and beauty — delivered to your door.")]
    public void EveryGeneratedDescription_IsTranslatedInEveryLanguage(string desc)
    {
        foreach (var locale in Locales.Where(l => l != "en"))
        {
            var got = StoreText.Description(desc, locale);
            Assert.NotEqual(desc, got);
            Assert.DoesNotContain("{0}", got, StringComparison.Ordinal);   // every slot filled
            Assert.DoesNotContain("{1}", got, StringComparison.Ordinal);
            Assert.DoesNotContain("{2}", got, StringComparison.Ordinal);
            Assert.DoesNotContain("sd.", got, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Area_SplitsTownFromRegion()
    {
        Assert.Equal("بوشر، مسقط", StoreText.Area("Bawshar, Muscat", "ar"));
        Assert.Equal("博舍尔，马斯喀特", StoreText.Area("Bawshar, Muscat", "zh"));
        // A bare town still resolves.
        Assert.Equal("مسقط", StoreText.Area("Muscat", "ar"));
        // An area we don't know is passed through.
        Assert.Equal("Somewhere Else", StoreText.Area("Somewhere Else", "ar"));
    }

    /// <summary>
    /// A real shop names itself. Rebuilding its English name from the generated-store
    /// vocabulary produced «زعفران Catering» and «Amiran کافه و رستوران» on the customer
    /// home, while the owner's own Persian name sat unread in the database.
    /// </summary>
    [Fact]
    public void OwnersOwnName_BeatsTheGeneratedVocabulary()
    {
        var saffron = new Dictionary<string, string>
        {
            ["en"] = "Saffron Catering",
            ["fa"] = "کترینگ ایرانی زعفران",
            ["ar"] = "زعفران للتموين الإيراني",
        };

        Assert.Equal("کترینگ ایرانی زعفران", StoreText.Name("Saffron Catering", saffron, "fa"));
        Assert.Equal("زعفران للتموين الإيراني", StoreText.Name("Saffron Catering", saffron, "ar"));
        Assert.Equal("Saffron Catering", StoreText.Name("Saffron Catering", saffron, "en"));

        // A language the owner did not fill falls back to the old behaviour rather
        // than showing an empty name.
        Assert.Equal(StoreText.Name("Saffron Catering", "tr"),
                     StoreText.Name("Saffron Catering", saffron, "tr"));
    }

    [Fact]
    public void NoOwnerNames_LeavesGeneratedStoresAlone()
    {
        const string generated = "Golden Samosa House - Bawshar";
        foreach (var locale in Locales)
        {
            Assert.Equal(StoreText.Name(generated, locale), StoreText.Name(generated, null, locale));
            Assert.Equal(StoreText.Name(generated, locale), StoreText.Name(generated, [], locale));
        }
    }

    [Fact]
    public void BlankOwnerName_DoesNotWinOverTheFallback()
    {
        var blank = new Dictionary<string, string> { ["fa"] = "   " };
        Assert.Equal(StoreText.Name("Saffron Catering", "fa"),
                     StoreText.Name("Saffron Catering", blank, "fa"));
    }

    [Fact]
    public void BlankInput_IsSafe()
    {
        foreach (var locale in Locales)
        {
            Assert.Equal("", StoreText.Name(null, locale));
            Assert.Equal("", StoreText.Description(null, locale));
            Assert.Equal("", StoreText.Area("   ", locale));
        }
    }
}
