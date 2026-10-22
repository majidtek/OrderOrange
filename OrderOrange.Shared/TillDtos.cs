namespace OrderOrange.Shared;

/// <summary>The current Windows till (LocalHandler) release, as published by <c>api/till/version</c>.</summary>
public record TillVersionDto(string Version, string File, string? Notes = null, string? Sha256 = null,
    /// <summary>The one-file installer (OrderOrangeTill-Setup-x.y.z.exe) beside the update zip, when one is published.</summary>
    string? Setup = null);
