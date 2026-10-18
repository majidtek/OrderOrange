using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Admin → Disk space (Majed 2026-10-13: "add hard disk space manager in admin panel").
///
/// <para>Shows every fixed drive and what the platform's own folders hold, and offers a fixed
/// set of cleanups. Nothing here takes a path from the browser: each category, its folders
/// and its cleanup rule are defined below, so an admin can only ever delete the leftovers a
/// rule allows (old deploy copies, build temp, old logs, old temp files). Databases and the
/// database backups are shown for information only.</para>
/// </summary>
public sealed class DiskManager(IMongoDatabase mongo, BackupRunner backups, ILogger<DiskManager> log)
{
    /// <summary>How a category may be cleaned.</summary>
    public enum Rule { None, KeepNewestPerGroup, DeleteAll, OlderThanDays }

    public sealed record Category(string Key, string[] Paths, Rule Rule, int DefaultValue, string FilePattern = "*", bool SkipClaude = false, string? KeepFile = null);

    // The service account's own temp folder (the API runs as Administrator). Claude's working
    // folders inside it are always skipped.
    private static readonly string Temp = @"C:\Users\Administrator\AppData\Local\Temp";

    /// <summary>The whole catalogue. Paths that do not exist on this server are skipped.</summary>
    public static readonly Category[] Categories =
    [
        new("deployBackups", [@"D:\OrderOrange\_backup"], Rule.KeepNewestPerGroup, 2),
        new("buildTemp", [@"D:\OrderOrange\_adminhosttmp", @"D:\OrderOrange\_admintmp", @"D:\OrderOrange\_apitmp",
                          @"D:\OrderOrange\_clienttmp", @"D:\OrderOrange\_clientwebtmp", @"D:\OrderOrange\_hosttmp",
                          @"D:\OrderOrange\staging\_pubtmp"], Rule.DeleteAll, 0),
        new("iisLogs", [@"C:\inetpub\logs\LogFiles"], Rule.OlderThanDays, 14, "*.log"),
        new("apiLogs", [@"D:\OrderOrange\publish-api\logs", @"D:\OrderOrange\staging\publish-api\logs"], Rule.OlderThanDays, 14),
        // "mongod.log.*" also matches the LIVE "mongod.log" on Windows (".*" matches no extension), so it is named and kept.
        new("mongoLogs", [@"C:\Program Files\MongoDB\Server\8.3\log"], Rule.OlderThanDays, 14, "mongod.log.*", KeepFile: "mongod.log"),
        new("tempFiles", [Temp], Rule.OlderThanDays, 7, "*", SkipClaude: true),
        new("dbBackups", [], Rule.None, 0),   // the admin's backup folder, filled in at scan time
        new("databases", [@"D:\database", @"C:\OrderOrangeStaging\db", @"C:\Program Files\MongoDB\Server\8.3\data"], Rule.None, 0),
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DiskScanDto? _last;
    public bool Scanning { get; private set; }

    private IMongoCollection<DiskLogDoc> Log => mongo.GetCollection<DiskLogDoc>("diskCleanups");

    public sealed class DiskLogDoc
    {
        [BsonId] public MongoDB.Bson.ObjectId Id { get; set; }
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime At { get; set; }
        public string By { get; set; } = "";
        public string Category { get; set; } = "";
        public int Value { get; set; }
        public int Files { get; set; }
        public long Bytes { get; set; }
        public string Note { get; set; } = "";
    }

    public static List<DriveDto> Drives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveDto(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize, d.TotalSize - d.AvailableFreeSpace, d.AvailableFreeSpace))
            .ToList();

    public DiskScanDto? Last => _last;

    public async Task<List<DiskCleanupLogDto>> HistoryAsync() =>
        (await Log.Find(FilterDefinition<DiskLogDoc>.Empty).SortByDescending(d => d.At).Limit(15).ToListAsync())
        .Select(d => new DiskCleanupLogDto(d.At, d.By, d.Category, d.Value, d.Files, d.Bytes, d.Note)).ToList();

    /// <summary>Measures every category (and what its default cleanup would free). Slow on big folders, so it runs in the background.</summary>
    public async Task<bool> ScanAsync()
    {
        if (!await _gate.WaitAsync(0)) return false;
        Scanning = true;
        try
        {
            var backupFolder = (await backups.GetAsync()).Folder;
            var result = await Task.Run(() => Categories.Select(c => Measure(c, backupFolder)).ToList());
            _last = new DiskScanDto(DateTime.Now, result);
        }
        catch (Exception ex) { log.LogError(ex, "Disk scan"); }
        finally { Scanning = false; _gate.Release(); }
        return true;
    }

    private static string[] PathsOf(Category c, string backupFolder) =>
        c.Key == "dbBackups" ? (string.IsNullOrWhiteSpace(backupFolder) ? [] : [backupFolder]) : c.Paths;

    private static DiskCategoryDto Measure(Category c, string backupFolder)
    {
        var paths = PathsOf(c, backupFolder).Where(Directory.Exists).ToArray();
        long bytes = 0, freeable = 0; var files = 0;
        var top = new List<DiskItemDto>();
        foreach (var root in paths)
        {
            foreach (var f in SafeFiles(root, "*", c.SkipClaude)) { bytes += f.Length; files++; }
            foreach (var child in SafeEntries(root, c.SkipClaude))
                top.Add(new DiskItemDto(child.Name, SizeOf(child, c.SkipClaude), child.LastWriteTime));
        }
        freeable = Plan(c, paths, c.DefaultValue).Sum(f => f.Length);
        return new DiskCategoryDto(c.Key, paths.ToList(), c.Rule.ToString(), c.DefaultValue, bytes, files, freeable,
            top.OrderByDescending(t => t.Bytes).Take(12).ToList());
    }

    /// <summary>Runs one category's cleanup with the admin's value (keep N / older than N days).</summary>
    public async Task<(int Files, long Bytes, string? Error)> CleanAsync(string key, int value, string by)
    {
        var c = Categories.FirstOrDefault(x => x.Key == key);
        if (c is null || c.Rule == Rule.None) return (0, 0, "This folder cannot be cleaned here.");
        if (c.Rule == Rule.KeepNewestPerGroup && value < 1) return (0, 0, "Keep at least one copy of each.");
        if (c.Rule == Rule.OlderThanDays && value < 1) return (0, 0, "Use at least 1 day.");
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(5))) return (0, 0, "A scan or cleanup is already running.");
        var deleted = 0; long freed = 0; var failed = 0;
        try
        {
            var paths = c.Paths.Where(Directory.Exists).ToArray();
            await Task.Run(() =>
            {
                if (c.Rule == Rule.KeepNewestPerGroup)
                {
                    foreach (var entry in PlanEntries(paths, value))
                    {
                        var size = SizeOf(entry, false);
                        try
                        {
                            if (entry is DirectoryInfo d) d.Delete(recursive: true); else entry.Delete();
                            deleted++; freed += size;
                        }
                        catch { failed++; }
                    }
                }
                else
                {
                    foreach (var f in Plan(c, paths, value))
                    {
                        try { var size = f.Length; f.Delete(); deleted++; freed += size; } catch { failed++; }
                    }
                    if (c.Rule == Rule.DeleteAll)
                        foreach (var root in paths)
                            foreach (var d in new DirectoryInfo(root).GetDirectories())
                                try { d.Delete(recursive: true); } catch { /* still in use */ }
                }
            });
            await Log.InsertOneAsync(new DiskLogDoc
            {
                At = DateTime.Now, By = by, Category = key, Value = value, Files = deleted, Bytes = freed,
                Note = failed > 0 ? $"{failed} item(s) were in use and kept" : "",
            });
        }
        finally { _gate.Release(); }
        _ = ScanAsync();   // refresh the numbers
        return (deleted, freed, null);
    }

    /// <summary>The files a cleanup with this value would delete (for file-based rules).</summary>
    private static List<FileInfo> Plan(Category c, string[] paths, int value)
    {
        switch (c.Rule)
        {
            case Rule.DeleteAll:
                return paths.SelectMany(p => SafeFiles(p, "*", false)).ToList();
            case Rule.OlderThanDays:
                var cutoff = DateTime.Now.AddDays(-Math.Max(1, value));
                return paths.SelectMany(p => SafeFiles(p, c.FilePattern, c.SkipClaude))
                    .Where(f => f.LastWriteTime < cutoff)
                    .Where(f => c.KeepFile is null || !f.Name.Equals(c.KeepFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            case Rule.KeepNewestPerGroup:
                return PlanEntries(paths, Math.Max(1, value)).SelectMany(e => e is DirectoryInfo d ? SafeFiles(d.FullName, "*", false) : [(FileInfo)e]).ToList();
            default:
                return [];
        }
    }

    // "OrderOrangePartner-live-20261011-1355" → "OrderOrangePartner"; "api-dlls-20261010-1056" → "api-dlls".
    private static readonly Regex Stamp = new(@"[-_]?(live[-_])?\d{8}([-_]\d{4,6})?.*$", RegexOptions.Compiled);

    /// <summary>Deploy copies beyond the newest <paramref name="keep"/> of each site.</summary>
    private static List<FileSystemInfo> PlanEntries(string[] paths, int keep) =>
        paths.SelectMany(p => new DirectoryInfo(p).GetFileSystemInfos())
            .GroupBy(e => Stamp.Replace(e.Name, "") is { Length: > 0 } g ? g : e.Name)
            .SelectMany(g => g.OrderByDescending(e => e.LastWriteTime).Skip(Math.Max(1, keep)))
            .ToList();

    private static IEnumerable<FileInfo> SafeFiles(string root, string pattern, bool skipClaude)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (skipClaude && dir.Contains(@"\claude", StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo[] here = [];
            try
            {
                var di = new DirectoryInfo(dir);
                here = di.GetFiles(pattern);
                foreach (var sub in di.GetDirectories())
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub.FullName);
            }
            catch { /* no access */ }
            foreach (var f in here) yield return f;
        }
    }

    private static IEnumerable<FileSystemInfo> SafeEntries(string root, bool skipClaude)
    {
        try
        {
            return new DirectoryInfo(root).GetFileSystemInfos()
                .Where(e => !(skipClaude && e.Name.Equals("claude", StringComparison.OrdinalIgnoreCase))
                            && !(skipClaude && e is DirectoryInfo d && Directory.Exists(Path.Combine(d.FullName, "claude"))));
        }
        catch { return []; }
    }

    private static long SizeOf(FileSystemInfo e, bool skipClaude) =>
        e is FileInfo f ? f.Length : SafeFiles(e.FullName, "*", skipClaude).Sum(x => x.Length);
}
