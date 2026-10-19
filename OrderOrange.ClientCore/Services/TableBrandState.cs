namespace OrderOrange.ClientCore.Services;

/// <summary>
/// The store a table-QR guest is standing in. While set, the customer app's bar wears
/// THAT store's logo and name instead of OrderOrange's — the guest scanned a card on a
/// table; the brand they are inside is the restaurant's, and the platform steps back.
/// </summary>
public sealed class TableBrandState
{
    public string? Name { get; private set; }
    public string? Logo { get; private set; }
    public string? Emoji { get; private set; }
    public bool IsSet => !string.IsNullOrEmpty(Name);
    /// <summary>The store whose own QR started this visit — its guests may order without signing in.</summary>
    public int? QrStoreId { get; private set; }

    public event Action? Changed;

    public void Set(string name, string? logo, string? emoji, int? qrStoreId = null)
    {
        Name = name;
        if (qrStoreId is not null) QrStoreId = qrStoreId;
        Logo = logo;
        Emoji = emoji;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Name is null) return;
        Name = Logo = Emoji = null;
        QrStoreId = null;
        Changed?.Invoke();
    }
}
