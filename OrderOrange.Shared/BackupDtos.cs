namespace OrderOrange.Shared;

/// <summary>Admin backup settings (Settings page): where, how often, how long to keep.</summary>
public record BackupSettingsRequest(bool Enabled, string Folder, int IntervalHours, int KeepHours);

/// <summary>Settings plus what the last run did and what the folder holds.</summary>
public record BackupStatusDto(
    bool Enabled, string Folder, int IntervalHours, int KeepHours,
    bool Running, string Step,
    DateTime? LastRunAt, DateTime? LastFullAt, bool LastOk, string LastMessage,
    string LastZipFile, long LastZipBytes, long LastSqlBytes, long LastMongoBytes, double LastSeconds,
    DateTime? NextRunAt, int FolderFiles, long FolderBytes, long FreeBytes,
    string SqlDatabase, string MongoDatabase);

/// <summary>One backup zip in the folder: name, size, when, full or diff.</summary>
public record BackupFileDto(string Name, long Bytes, DateTime CreatedAt, string Kind);

/// <summary>A short-lived download link (relative to the API base).</summary>
public record BackupLinkDto(string Url);
