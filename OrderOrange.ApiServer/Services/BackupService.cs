using System.IO.Compression;
using Microsoft.Data.SqlClient;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The admin's backup settings and the last run's outcome — one document in Mongo
/// <c>platformSettings</c> (id "backup"), so production and staging each keep their own.
/// </summary>
public sealed class BackupSettingsDoc
{
    [BsonId] public string Id { get; set; } = "backup";
    public bool Enabled { get; set; }
    public string Folder { get; set; } = "";
    public int IntervalHours { get; set; } = 1;
    public int KeepHours { get; set; } = 24;

    public DateTime? LastRunAt { get; set; }
    public DateTime? LastFullAt { get; set; }
    public bool LastOk { get; set; }
    public string LastMessage { get; set; } = "";
    public string LastSqlFile { get; set; } = "";
    public long LastSqlBytes { get; set; }
    public string LastMongoFolder { get; set; } = "";
    public long LastMongoBytes { get; set; }
    /// <summary>The run's single zip (SQL .bak + Mongo dump) and its size on disk.</summary>
    public string LastZipFile { get; set; } = "";
    public long LastZipBytes { get; set; }
    public double LastSeconds { get; set; }
}

/// <summary>
/// Hourly backups of the SQL database and the Mongo database to a folder the admin picks
/// (Majed 2026-10-09: "in admin settings add path to get backup for sql and mongodb, each
/// 1 hour"; 2026-10-10: "make them in zip … both of them in one zip").
///
/// <para>Every run writes ONE zip, <c>&lt;db&gt;_&lt;yyyyMMdd-HHmm&gt;_full.zip</c> or
/// <c>…_diff.zip</c>, holding <c>sql/&lt;name&gt;.bak</c>, <c>mongo/&lt;mongoDb&gt;/…</c> and a
/// RESTORE.txt. Zipping matters: LocalDB cannot compress its own backups.</para>
///
/// <para>SQL: the production database is ~9 GB, so a FULL backup every hour would fill the
/// disk in a day. Instead one full backup a day and a DIFFERENTIAL every other run — a
/// differential holds only what changed since that full, so it stays small. Restoring is
/// "the full zip's .bak, then the newest diff zip's .bak".</para>
///
/// <para>Mongo: no mongodump on this server, so the driver streams every collection as raw
/// BSON into <c>&lt;collection&gt;.bson</c> with its indexes in
/// <c>&lt;collection&gt;.metadata.json</c> — the layout <c>mongorestore --dir</c> reads.</para>
///
/// <para>Old zips are removed after KeepHours; a full is kept while any kept differential
/// still depends on it. A run is skipped when the drive has under 3 GB free.</para>
/// </summary>
public sealed class BackupRunner(IConfiguration config, IMongoDatabase mongo, ILogger<BackupRunner> log)
{
    private const long MinFreeBytes = 3L * 1024 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMongoCollection<BackupSettingsDoc> Settings => mongo.GetCollection<BackupSettingsDoc>("platformSettings");

    public bool Running { get; private set; }
    public string Step { get; private set; } = "";

    public async Task<BackupSettingsDoc> GetAsync() =>
        await Settings.Find(d => d.Id == "backup").FirstOrDefaultAsync() ?? new BackupSettingsDoc();

    public async Task SaveSettingsAsync(bool enabled, string folder, int intervalHours, int keepHours)
    {
        var update = Builders<BackupSettingsDoc>.Update
            .Set(d => d.Enabled, enabled)
            .Set(d => d.Folder, folder)
            .Set(d => d.IntervalHours, intervalHours)
            .Set(d => d.KeepHours, keepHours);
        await Settings.UpdateOneAsync(d => d.Id == "backup", update, new UpdateOptions { IsUpsert = true });
    }

    /// <summary>Runs one backup now unless one is already running. Returns false when busy.</summary>
    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return false;
        Running = true;
        var started = DateTime.Now;
        var s = await GetAsync();
        var result = new BackupSettingsDoc { LastRunAt = started, LastFullAt = s.LastFullAt };
        string? workBak = null, partZip = null;
        try
        {
            if (string.IsNullOrWhiteSpace(s.Folder) || !Path.IsPathFullyQualified(s.Folder))
                throw new InvalidOperationException("No backup folder is set.");
            var folder = Directory.CreateDirectory(s.Folder).FullName;
            var work = Directory.CreateDirectory(Path.Combine(folder, ".work")).FullName;
            foreach (var stale in Directory.GetFiles(work)) TryDelete(stale);                    // a crashed run's leftovers
            foreach (var stale in Directory.GetFiles(folder, "*.zip.part")) TryDelete(stale);

            Prune(folder, s.KeepHours);   // room first
            var free = new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace;
            if (free < MinFreeBytes)
                throw new InvalidOperationException($"Only {free / 1024d / 1024 / 1024:0.0} GB free on the backup drive — skipped (needs 3 GB).");

            var stamp = started.ToString("yyyyMMdd-HHmm");
            var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("No SQL connection string.");
            var dbName = new SqlConnectionStringBuilder(cs).InitialCatalog;
            var mongoName = mongo.DatabaseNamespace.DatabaseName;
            var lastFull = Directory.GetFiles(folder, $"{dbName}_*_full.zip").Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTime).FirstOrDefault();
            var full = lastFull is null || (started - lastFull.LastWriteTime).TotalHours >= 23.5 || s.IntervalHours >= 24;
            var kind = full ? "full" : "diff";
            var baseName = $"{dbName}_{stamp}_{kind}";

            // --- SQL: back up to a work file, it goes into the zip below ---
            Step = "sql";
            workBak = Path.Combine(work, baseName + ".bak");
            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 3600;
                cmd.CommandText = $"BACKUP DATABASE [{dbName.Replace("]", "]]")}] TO DISK = @file WITH INIT, CHECKSUM{(full ? "" : ", DIFFERENTIAL")}, NAME = @name";
                cmd.Parameters.AddWithValue("@file", workBak);
                cmd.Parameters.AddWithValue("@name", $"OrderOrange {(full ? "full" : "differential")} {stamp}");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            result.LastSqlBytes = new FileInfo(workBak).Length;

            // --- One zip: the .bak, every Mongo collection, and how to restore ---
            Step = "zip";
            partZip = Path.Combine(folder, baseName + ".zip.part");
            long mongoBytes = 0;
            var collections = 0;
            await using (var zfs = new FileStream(partZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true))
            using (var zip = new ZipArchive(zfs, ZipArchiveMode.Create))
            {
                var bakEntry = zip.CreateEntry($"sql/{baseName}.bak", CompressionLevel.Fastest);
                await using (var es = bakEntry.Open())
                await using (var bs = new FileStream(workBak, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                    await bs.CopyToAsync(es, 1 << 20, ct);

                Step = "mongo";
                var names = await (await mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
                foreach (var name in names.Where(n => !n.StartsWith("system.")).OrderBy(n => n))
                {
                    var coll = mongo.GetCollection<RawBsonDocument>(name);
                    var dataEntry = zip.CreateEntry($"mongo/{mongoName}/{name}.bson", CompressionLevel.Fastest);
                    await using (var es = dataEntry.Open())
                    {
                        using var cursor = await coll.Find(FilterDefinition<RawBsonDocument>.Empty,
                            new FindOptions { BatchSize = 500 }).ToCursorAsync(ct);
                        while (await cursor.MoveNextAsync(ct))
                            foreach (var doc in cursor.Current)
                            {
                                var bytes = doc.ToBson();
                                mongoBytes += bytes.Length;
                                await es.WriteAsync(bytes, ct);
                            }
                    }
                    var indexes = await (await coll.Indexes.ListAsync(ct)).ToListAsync(ct);
                    var meta = new BsonDocument
                    {
                        { "options", new BsonDocument() },
                        { "indexes", new BsonArray(indexes) },
                        { "collectionName", name },
                    };
                    var metaEntry = zip.CreateEntry($"mongo/{mongoName}/{name}.metadata.json", CompressionLevel.Fastest);
                    await using (var ms = metaEntry.Open())
                    await using (var w = new StreamWriter(ms))
                        await w.WriteAsync(meta.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson }));
                    collections++;
                }

                var readme = zip.CreateEntry("RESTORE.txt", CompressionLevel.Fastest);
                await using (var rs = readme.Open())
                await using (var w = new StreamWriter(rs))
                    await w.WriteAsync(
$"""
OrderOrange backup {stamp} ({(full ? "FULL" : "DIFFERENTIAL")})
SQL database:   {dbName}   ->  sql/{baseName}.bak
Mongo database: {mongoName}  ->  mongo/{mongoName}/  ({collections} collections)

Restore SQL
  {(full ? "This is a FULL backup:" : "This is a DIFFERENTIAL: first restore the newest *_full.zip made BEFORE it, then this one.")}
  RESTORE DATABASE [{dbName}] FROM DISK = '<full .bak>' WITH NORECOVERY, REPLACE;
  RESTORE DATABASE [{dbName}] FROM DISK = '<this diff .bak>' WITH RECOVERY;
  (a full on its own: ... FROM DISK = '<full .bak>' WITH RECOVERY, REPLACE;)

Restore MongoDB (MongoDB Database Tools)
  mongorestore --drop --dir "<unzipped folder>\mongo"
""");
            }
            File.Move(partZip, Path.Combine(folder, baseName + ".zip"));
            var zipPath = Path.Combine(folder, baseName + ".zip");
            partZip = null;

            result.LastZipFile = zipPath;
            result.LastZipBytes = new FileInfo(zipPath).Length;
            result.LastMongoBytes = mongoBytes;
            if (full) result.LastFullAt = started;
            result.LastOk = true;
            result.LastMessage = $"{(full ? "Full" : "Differential")} SQL backup + Mongo dump of {collections} collections, zipped.";
        }
        catch (Exception ex)
        {
            result.LastOk = false;
            result.LastMessage = ex.Message;
            log.LogError(ex, "Backup failed");
        }
        finally
        {
            if (workBak is not null) TryDelete(workBak);
            if (partZip is not null) TryDelete(partZip);
            result.LastSeconds = (DateTime.Now - started).TotalSeconds;
            var update = Builders<BackupSettingsDoc>.Update
                .Set(d => d.LastRunAt, result.LastRunAt)
                .Set(d => d.LastFullAt, result.LastFullAt)
                .Set(d => d.LastOk, result.LastOk)
                .Set(d => d.LastMessage, result.LastMessage)
                .Set(d => d.LastZipFile, result.LastZipFile)
                .Set(d => d.LastZipBytes, result.LastZipBytes)
                .Set(d => d.LastSqlBytes, result.LastSqlBytes)
                .Set(d => d.LastMongoBytes, result.LastMongoBytes)
                .Set(d => d.LastSeconds, result.LastSeconds);
            try { await Settings.UpdateOneAsync(d => d.Id == "backup", update, new UpdateOptions { IsUpsert = true }); }
            catch (Exception ex) { log.LogError(ex, "Could not record the backup result"); }
            Running = false;
            Step = "";
            _gate.Release();
        }
        return true;
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { log.LogWarning(ex, "Could not delete {Path}", path); }
    }

    /// <summary>Deletes zips older than keepHours, keeping every full a kept differential needs.</summary>
    private void Prune(string folder, int keepHours)
    {
        var cutoff = DateTime.Now.AddHours(-Math.Max(1, keepHours));
        try
        {
            var zips = Directory.GetFiles(folder, "*.zip").Select(p => new FileInfo(p)).OrderBy(fi => fi.LastWriteTime).ToList();
            foreach (var z in zips.Where(z => z.Name.EndsWith("_diff.zip") && z.LastWriteTime < cutoff)) z.Delete();
            var fulls = zips.Where(z => z.Name.EndsWith("_full.zip")).ToList();
            for (var i = 0; i < fulls.Count - 1; i++)
                // A full is only safe to drop once the NEXT full is itself older than the cutoff:
                // then no kept differential can be based on this one.
                if (fulls[i + 1].LastWriteTime < cutoff) fulls[i].Delete();
        }
        catch (Exception ex) { log.LogWarning(ex, "Backup pruning"); }
    }
    /// <summary>What the admin page shows about the folder.</summary>
    public static (int Files, long Bytes, long Free) FolderStats(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return (0, 0, -1);
        var all = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        long free = -1;
        try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; } catch { }
        return (all.Count, all.Sum(f => f.Length), free);
    }
}

/// <summary>Wakes every minute and starts a backup when the admin's interval has passed.</summary>
public sealed class BackupScheduler(BackupRunner runner, ILogger<BackupScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (LocalDB cold start) before the first check.
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var s = await runner.GetAsync();
                var due = s.Enabled && !string.IsNullOrWhiteSpace(s.Folder) && !runner.Running &&
                          (s.LastRunAt is null || DateTime.Now - s.LastRunAt.Value >= TimeSpan.FromHours(Math.Max(1, s.IntervalHours)) - TimeSpan.FromMinutes(1));
                if (due) await runner.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Backup scheduler"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch { return; }
        }
    }
}
