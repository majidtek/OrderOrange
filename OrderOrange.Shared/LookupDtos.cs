namespace OrderOrange.Shared;

public record CuisineDto(int Id, string Name, string Emoji);
public record SaveCuisineRequest(string Name, string Emoji);
