using System.Net.Http.Json;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using SkiaSharp;

namespace OrderOrange.Tests;

/// <summary>Small product photos for the POS tiles, and the full photo still there for the big view.</summary>
public class PhotoThumbTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PhotoThumbTests(ApiFactory factory) => _factory = factory;

    /// <summary>A real JPEG of the given size, as a data URI, like a partner's upload.</summary>
    private static string Photo(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return "data:image/jpeg;base64," + Convert.ToBase64String(data.Span);
    }

    private static (int Width, int Height) SizeOf(string dataUri)
    {
        var bytes = Convert.FromBase64String(dataUri[(dataUri.IndexOf(',') + 1)..]);
        using var bitmap = SKBitmap.Decode(bytes);
        return (bitmap.Width, bitmap.Height);
    }

    [Fact]
    public void Make_ShrinksToAWebPThatFitsTheTile_KeepingTheShape()
    {
        var big = Photo(1600, 1200, SKColors.OrangeRed);
        var thumb = PhotoThumbs.Make(big);

        Assert.NotNull(thumb);
        Assert.StartsWith("data:image/webp;base64,", thumb);
        Assert.True(thumb!.Length < big.Length / 5, $"thumb {thumb.Length} vs photo {big.Length}");
        Assert.Equal((240, 180), SizeOf(thumb));
    }

    [Fact]
    public void Make_RefusesWhatIsNotAPicture()
    {
        Assert.Null(PhotoThumbs.Make(null));
        Assert.Null(PhotoThumbs.Make("https://example.com/a.jpg"));
        Assert.Null(PhotoThumbs.Make("data:image/png;base64,not-base64!"));
        Assert.Null(PhotoThumbs.Make("data:image/png;base64," + Convert.ToBase64String([1, 2, 3, 4])));
    }

    [Fact]
    public async Task SmallPhotos_ComeAPageAtATime_OnlyForTheStoresOwnProducts()
    {
        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com"); // Burger Bros
        var menu = await sara.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu?light=true");
        async Task<int> AddAsync(string name, string? photo) => (await (await sara.PostAsJsonAsync("api/menu/items",
                new SaveMenuItemRequest(menu![0].Id, name, "", 1.000m, "🍔", false, true, photo)))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<MenuItemDto>())!.Id;
        var red = await AddAsync("Batch Red", Photo(800, 800, SKColors.Red));
        var blue = await AddAsync("Batch Blue", Photo(800, 400, SKColors.Blue));
        var none = await AddAsync("Batch Plain", null);

        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com"); // Bella Napoli
        var other = (await marco.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu?light=true"))!
            .SelectMany(c => c.Items).First().Id;

        var got = await sara.GetFromJsonAsync<Dictionary<int, string?>>($"api/menu/photos/small?ids={red},{blue},{none},{other}");
        Assert.Equal((240, 240), SizeOf(got![red]!));
        Assert.Equal((240, 120), SizeOf(got[blue]!));
        Assert.Null(got[none]);
        Assert.False(got.ContainsKey(other));   // another store's product is simply not there
    }

    [Fact]
    public async Task SmallPhoto_IsMadeOnce_RemadeWhenThePhotoChanges_AndTheFullPhotoStaysAvailable()
    {
        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com"); // Burger Bros
        var menu = await sara.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu?light=true");
        var first = Photo(1200, 900, SKColors.SeaGreen);
        var item = await (await sara.PostAsJsonAsync("api/menu/items",
            new SaveMenuItemRequest(menu![0].Id, "Thumb Burger", "", 1.000m, "🍔", false, true, first)))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<MenuItemDto>();

        var small = (await sara.GetFromJsonAsync<DishMainPhotoDto>($"api/menu/items/{item!.Id}/photo?small=true"))!.Photo!;
        Assert.StartsWith("data:image/webp;base64,", small);
        Assert.Equal((240, 180), SizeOf(small));
        var again = (await sara.GetFromJsonAsync<DishMainPhotoDto>($"api/menu/items/{item.Id}/photo?small=true"))!.Photo;
        Assert.Equal(small, again);

        // The big view still gets the photo as uploaded.
        var full = (await sara.GetFromJsonAsync<DishMainPhotoDto>($"api/menu/items/{item.Id}/photo"))!.Photo;
        Assert.Equal(first, full);

        // A new photo of another shape: the old thumbnail must not survive it.
        var second = Photo(600, 1200, SKColors.SteelBlue);
        (await sara.PutAsJsonAsync($"api/menu/items/{item.Id}",
            new SaveMenuItemRequest(menu[0].Id, "Thumb Burger", "", 1.000m, "🍔", false, true, second)))
            .EnsureSuccessStatusCode();
        var remade = (await sara.GetFromJsonAsync<DishMainPhotoDto>($"api/menu/items/{item.Id}/photo?small=true"))!.Photo!;
        Assert.Equal((120, 240), SizeOf(remade));
    }
}
