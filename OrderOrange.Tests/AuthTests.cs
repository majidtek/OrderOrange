using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsAdministratorRole()
    {
        var client = _factory.CreateClient();
        var login = await client.SignInAsync("admin@majidfood.com");

        Assert.Equal(UserRole.Administrator, login.Role);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejected()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("admin@majidfood.com", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AsOwner_CarriesTheRestaurant()
    {
        var client = _factory.CreateClient();
        var login = await client.SignInAsync("marco@majidfood.com");

        Assert.Equal(UserRole.RestaurantOwner, login.Role);
        Assert.NotNull(login.RestaurantId);
        Assert.Equal("Bella Napoli", login.RestaurantName);
    }

    [Fact]
    public async Task Register_CreatesACustomerThatCanSignIn()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Test Person", "test.person@example.com", "+968 9999 0000", "Secret_1", FormToken.Create()));
        response.EnsureSuccessStatusCode();

        var login = await client.SignInAsync("test.person@example.com", "Secret_1");
        Assert.Equal(UserRole.Customer, login.Role);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_IsRejected()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Imposter", "admin@majidfood.com", "1", "Secret_1", FormToken.Create()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("api/orders/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
