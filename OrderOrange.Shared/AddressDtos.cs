namespace OrderOrange.Shared;

public record AddressDto(int Id, string Label, string Area, string Street, string Building, string? Notes, double? Lat, double? Lng);
public record SaveAddressRequest(string Label, string Area, string Street, string Building, string? Notes, double? Lat, double? Lng);
