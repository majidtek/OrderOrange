using System.Collections.Concurrent;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Tells a shop's tills the moment something about its orders changed, so a sale rung up on
/// the web is on paper in about a second instead of after the next poll (Majed 2026-10-18:
/// "can we make more fast"). One version counter per store: every save that touches the
/// store's orders or open tables bumps it (see <see cref="Data.AppDbContext.SaveChangesAsync"/>);
/// a till long-polls <c>GET api/orders/restaurant/wait?since=N</c>, which returns as soon as
/// the counter passes N or after the timeout. In-memory on purpose — a restart just makes
/// every till refresh once.
/// </summary>
public sealed class StoreChangeNotifier
{
    public static StoreChangeNotifier Instance { get; } = new();

    private sealed class Entry
    {
        public long Version;
        public TaskCompletionSource<bool> Signal = NewSignal();
    }

    private readonly ConcurrentDictionary<int, Entry> _stores = new();

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Entry For(int restaurantId) => _stores.GetOrAdd(restaurantId, _ => new Entry());

    public long Version(int restaurantId) => Interlocked.Read(ref For(restaurantId).Version);

    /// <summary>Something changed for this store: bump the version and wake every waiting till.</summary>
    public void Signal(int restaurantId)
    {
        if (restaurantId <= 0) return;
        var e = For(restaurantId);
        Interlocked.Increment(ref e.Version);
        var old = Interlocked.Exchange(ref e.Signal, NewSignal());
        old.TrySetResult(true);
    }

    /// <summary>
    /// Returns as soon as the store's version is newer than <paramref name="since"/>, or after
    /// <paramref name="timeout"/> with the current (unchanged) version. Cancelled when the client
    /// goes away.
    /// </summary>
    public async Task<long> WaitAsync(int restaurantId, long since, TimeSpan timeout, CancellationToken ct)
    {
        var e = For(restaurantId);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var current = Interlocked.Read(ref e.Version);
            if (current > since) return current;
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return current;
            var signal = e.Signal.Task;
            var done = await Task.WhenAny(signal, Task.Delay(left, ct));
            if (done != signal) return Interlocked.Read(ref e.Version);
        }
    }
}
