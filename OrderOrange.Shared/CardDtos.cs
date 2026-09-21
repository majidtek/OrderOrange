namespace OrderOrange.Shared;

/// <summary>A saved payment card — TEST mode. Only the last four digits are stored.</summary>
public record CardDto(int Id, string Brand, string HolderName, string Last4, int ExpMonth, int ExpYear);

/// <summary>The full number is used once to derive brand + last4, then discarded.</summary>
public record SaveCardRequest(string HolderName, string Number, int ExpMonth, int ExpYear, string Cvv);
