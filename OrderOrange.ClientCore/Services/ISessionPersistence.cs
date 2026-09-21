using OrderOrange.Shared;

namespace OrderOrange.ClientCore.Services;

/// <summary>Where a signed-in session survives between page loads, if anywhere.</summary>
public interface ISessionPersistence
{
    Task<LoginResponse?> LoadAsync();
    Task SaveAsync(LoginResponse session);
    Task ClearAsync();
}

/// <summary>Default: nothing persists (used outside a browser host).</summary>
public sealed class NoSessionPersistence : ISessionPersistence
{
    public Task<LoginResponse?> LoadAsync() => Task.FromResult<LoginResponse?>(null);
    public Task SaveAsync(LoginResponse session) => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
}
