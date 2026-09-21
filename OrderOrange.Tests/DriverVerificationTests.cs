using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class DriverVerificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DriverVerificationTests(ApiFactory factory) => _factory = factory;

    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg=";

    [Fact]
    public async Task FullFlow_SubmitRejectResubmitApprove_GatesGoingOnline()
    {
        // A fresh rider applies and the admin activates the account.
        var anonymous = _factory.CreateClient();
        (await anonymous.PostAsJsonAsync("api/auth/register-driver",
            new RegisterDriverRequest("Verify Test Rider", "verify.rider@test.com", "99887766", "Pas_123", VehicleType.Motorbike, Gate: FormToken.Create())))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        var users = await admin.GetFromJsonAsync<List<UserDto>>("api/admin/users?search=verify.rider@test.com");
        var riderId = Assert.Single(users!).Id;
        (await admin.PostAsync($"api/admin/users/{riderId}/toggle-active", null)).EnsureSuccessStatusCode();

        // Unverified: can't go online, state says NotSubmitted.
        var rider = _factory.CreateClient();
        await rider.SignInAsync("verify.rider@test.com");
        var state = await rider.GetFromJsonAsync<DriverStateDto>("api/drivers/state");
        Assert.Equal(DriverVerificationStatus.NotSubmitted, state!.Verification);
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.PostAsync("api/drivers/toggle-online", null)).StatusCode);

        // All four photos are mandatory.
        var partial = await rider.PostAsJsonAsync("api/drivers/verification",
            new SubmitDriverDocsRequest(Png, Png, Png, ""));
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        // Full submission lands in the admin's Pending tab.
        (await rider.PostAsJsonAsync("api/drivers/verification", new SubmitDriverDocsRequest(Png, Png, Png, Png)))
            .EnsureSuccessStatusCode();
        var pending = await admin.GetFromJsonAsync<List<AdminDriverDocsDto>>("api/admin/driver-verifications?status=Pending");
        var submission = Assert.Single(pending!, d => d.UserId == riderId);
        Assert.Equal(Png, submission.IdCardFront);

        // Reject needs a reason; the reason reaches the rider, still can't go online.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"api/admin/driver-verifications/{riderId}/reject", new RejectDriverDocsRequest(" "))).StatusCode);
        (await admin.PostAsJsonAsync($"api/admin/driver-verifications/{riderId}/reject", new RejectDriverDocsRequest("ID photo is blurry")))
            .EnsureSuccessStatusCode();
        var verification = await rider.GetFromJsonAsync<DriverVerificationDto>("api/drivers/verification");
        Assert.Equal(DriverVerificationStatus.Rejected, verification!.Status);
        Assert.Equal("ID photo is blurry", verification.Reason);
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.PostAsync("api/drivers/toggle-online", null)).StatusCode);

        // Resubmit → Pending again, reason cleared → approve → online works.
        (await rider.PostAsJsonAsync("api/drivers/verification", new SubmitDriverDocsRequest(Png, Png, Png, Png)))
            .EnsureSuccessStatusCode();
        verification = await rider.GetFromJsonAsync<DriverVerificationDto>("api/drivers/verification");
        Assert.Equal(DriverVerificationStatus.Pending, verification!.Status);
        Assert.Null(verification.Reason);

        (await admin.PostAsync($"api/admin/driver-verifications/{riderId}/approve", null)).EnsureSuccessStatusCode();
        (await rider.PostAsync("api/drivers/toggle-online", null)).EnsureSuccessStatusCode();
        state = await rider.GetFromJsonAsync<DriverStateDto>("api/drivers/state");
        Assert.True(state!.IsOnline);
        Assert.Equal(DriverVerificationStatus.Approved, state.Verification);
    }
}
