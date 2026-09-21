using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

foreach (var arg in args)
{
    var parts = arg.Split('|');
    var input = parts[0];
    var output = parts[1];
    using var image = Image.Load(input);
    image.Save(output, new PngEncoder());
    Console.WriteLine($"{input} -> {output} ({image.Width}x{image.Height})");
}
