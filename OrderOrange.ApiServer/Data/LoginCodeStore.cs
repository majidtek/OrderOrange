using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// A one-time sign-in code, keyed by the address it was sent to.
///
/// The code itself is NOT stored — only a hash of it. Anyone who can read the database
/// would otherwise be able to sign in as every user who happened to be logging in, and
/// a login code is a password for the next five minutes.
/// </summary>
public sealed class LoginCodeDoc
{
    /// <summary>The email address, lower-cased. One live code per address, so it is the key.</summary>
    [BsonId] public string Email { get; set; } = "";

    public string CodeHash { get; set; } = "";
    public DateTime SentAt { get; set; }

    /// <summary>Mongo deletes the document itself once this passes — no cleanup job.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Wrong guesses so far. Enough of them burn the code.</summary>
    public int Attempts { get; set; }
}

/// <summary>
/// Storage and rules for email sign-in codes.
///
/// A six-digit code is only 1,000,000 possibilities, which is nothing to a machine. What
/// makes it safe is that it dies quickly, dies on a handful of wrong guesses, and cannot
/// be requested over and over to widen the window. All three live here rather than in the
/// controller, so no future endpoint can forget one of them.
/// </summary>
public sealed class LoginCodeStore
{
    private readonly IMongoCollection<LoginCodeDoc> _codes;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    public LoginCodeStore(IMongoDatabase database)
    {
        _codes = database.GetCollection<LoginCodeDoc>("loginCodes");

        // A TTL index: Mongo removes expired codes on its own. Without it, a used-up or
        // abandoned code would sit in the database indefinitely waiting to be guessed.
        _codes.Indexes.CreateOne(new CreateIndexModel<LoginCodeDoc>(
            Builders<LoginCodeDoc>.IndexKeys.Ascending(c => c.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
    }

    /// <summary>Six digits, from the cryptographic generator — never Random.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>
    /// Salted per address so two people with the same code do not share a hash, and so a
    /// stolen hash cannot be looked up in a table of all million possibilities.
    /// </summary>
    private static string Hash(string email, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"orderorange-otp-v1|{email}|{code}")));

    /// <summary>How long until this address may ask for another code; zero when it may now.</summary>
    public async Task<TimeSpan> RetryAfterAsync(string email)
    {
        var existing = await _codes.Find(c => c.Email == email).FirstOrDefaultAsync();
        if (existing is null) return TimeSpan.Zero;
        var wait = existing.SentAt + ResendInterval - DateTime.UtcNow;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>Stores a freshly issued code, replacing any earlier one for this address.</summary>
    public async Task IssueAsync(string email, string code)
    {
        var now = DateTime.UtcNow;
        await _codes.ReplaceOneAsync(
            c => c.Email == email,
            new LoginCodeDoc
            {
                Email = email,
                CodeHash = Hash(email, code),
                SentAt = now,
                ExpiresAt = now + Lifetime,
                Attempts = 0,
            },
            new ReplaceOptions { IsUpsert = true });
    }

    public enum Result { Ok, NoCode, Expired, TooManyAttempts, Wrong }

    /// <summary>
    /// Checks a code and consumes it. A correct code is deleted immediately so it cannot
    /// be replayed; a wrong one costs an attempt, and running out burns the code entirely
    /// rather than letting someone work through the range.
    /// </summary>
    public async Task<Result> VerifyAsync(string email, string code)
    {
        var doc = await _codes.Find(c => c.Email == email).FirstOrDefaultAsync();
        if (doc is null) return Result.NoCode;

        if (doc.ExpiresAt <= DateTime.UtcNow)
        {
            await _codes.DeleteOneAsync(c => c.Email == email);
            return Result.Expired;
        }
        if (doc.Attempts >= MaxAttempts)
        {
            await _codes.DeleteOneAsync(c => c.Email == email);
            return Result.TooManyAttempts;
        }

        // Fixed-time comparison: a normal string compare leaks how much of the code was
        // right through how long it took, which is enough to guess it digit by digit.
        var expected = Encoding.UTF8.GetBytes(doc.CodeHash);
        var actual = Encoding.UTF8.GetBytes(Hash(email, code ?? ""));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            await _codes.UpdateOneAsync(c => c.Email == email,
                Builders<LoginCodeDoc>.Update.Inc(c => c.Attempts, 1));
            return Result.Wrong;
        }

        await _codes.DeleteOneAsync(c => c.Email == email);
        return Result.Ok;
    }
}
