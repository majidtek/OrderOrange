using OrderOrange.Shared;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// The path a store lives at when the app writes a link: <c>/p/{id}</c> for every
/// vertical — one short neutral form, so a soap shop's address never says "restaurant"
/// and a printed link stays as small as a link can be. The longer routes
/// (/restaurant/{id}, /store/{id}) still RESOLVE — every link ever printed keeps
/// working — this only decides what gets written from now on.
/// </summary>
public static class StorePath
{
    public static string For(StoreType type, int id) => For(id);
    public static string For(int id) => $"/p/{id}";
}
