namespace OrderOrange.Shared;

/// <summary>One fixed drive: size, used and free bytes.</summary>
public record DriveDto(string Name, string Label, long Total, long Used, long Free);

/// <summary>A big item inside a category folder.</summary>
public record DiskItemDto(string Name, long Bytes, DateTime Modified);

/// <summary>What one category holds and what its default cleanup would free.</summary>
/// <param name="Rule">None | KeepNewestPerGroup | DeleteAll | OlderThanDays.</param>
/// <param name="DefaultValue">Keep N copies / older than N days.</param>
public record DiskCategoryDto(string Key, List<string> Paths, string Rule, int DefaultValue,
    long Bytes, int Files, long Freeable, List<DiskItemDto> Top);

public record DiskScanDto(DateTime At, List<DiskCategoryDto> Categories);

public record DiskCleanupLogDto(DateTime At, string By, string Category, int Value, int Files, long Bytes, string Note);

/// <summary>The disk page: drives now, the last folder scan, recent cleanups.</summary>
public record DiskStatusDto(List<DriveDto> Drives, DiskScanDto? Scan, bool Scanning, List<DiskCleanupLogDto> History);

public record DiskCleanRequest(string Category, int Value);

public record DiskCleanResultDto(int Files, long Bytes);