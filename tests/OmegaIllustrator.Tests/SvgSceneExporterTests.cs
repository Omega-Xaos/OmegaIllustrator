using System.Xml.Linq;
using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class SvgSceneExporterTests
{
    [Theory]
    [InlineData("palette-1", "#D7CDF5")]
    [InlineData("palette-2", "#FDD7B9")]
    [InlineData("palette-3", "#BEEBEB")]
    public void Export_embeds_vector_and_applies_palette(string palette, string expectedFill)
    {
        var path = CreateAsset();
        try
        {
            var xml = SvgSceneExporter.Export(1000, 1000, "#FFFFFF", palette,
                [new SceneSvgItem(path, 500, 500, 300, 200, 15)]);
            var document = XDocument.Parse(xml);
            XNamespace svg = "http://www.w3.org/2000/svg";

            Assert.Equal("svg", document.Root?.Name.LocalName);
            Assert.NotEmpty(document.Descendants(svg + "path"));
            Assert.Contains(document.Descendants(svg + "path"), element => (string?)element.Attribute("fill") == expectedFill);
            Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName is "image" or "foreignObject");
            Assert.DoesNotContain("/Assets/", xml, StringComparison.Ordinal);
            Assert.Contains("rotate(15)", xml, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Palette_keeps_ink_and_white_unchanged()
    {
        var map = SvgSceneExporter.PaletteMap("palette-3");
        Assert.DoesNotContain("#181817", map.Keys);
        Assert.DoesNotContain("#FFFFFF", map.Keys);
    }

    [Fact]
    public void Export_keeps_palette_colored_support_objects_opaque()
    {
        var path = CreateAsset();
        try
        {
            var xml = SvgSceneExporter.Export(100, 100, "#fff", "palette-1",
                [new SceneSvgItem(path, 50, 50, 30, 20, 0)]);
            var document = XDocument.Parse(xml);
            XNamespace svg = "http://www.w3.org/2000/svg";

            Assert.Null(document.Descendants(svg + "g").Single(element => ((string?)element.Attribute("id"))?.StartsWith("object-", StringComparison.Ordinal) == true).Attribute("opacity"));
            Assert.NotEmpty(document.Descendants(svg + "path"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_applies_the_palette_map_for_each_background_object()
    {
        var path = CreateAsset();
        try
        {
            var xml = SvgSceneExporter.Export(100, 100, "#fff", new Dictionary<string, string>(),
                [new SceneSvgItem(path, 50, 50, 30, 20, 0, Role: "background", PaletteMap: new Dictionary<string, string> { ["#D7CDF5"] = "#B6C6FE" })]);
            var document = XDocument.Parse(xml);
            XNamespace svg = "http://www.w3.org/2000/svg";

            Assert.Contains(document.Descendants(svg + "path"), element => (string?)element.Attribute("fill") == "#B6C6FE");
            Assert.Contains(document.Descendants(svg + "g"), element => (string?)element.Attribute("data-role") == "background");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_rejects_invalid_viewbox()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 0 10\"><path d=\"M0 0\"/></svg>");
        try { Assert.Throws<InvalidDataException>(() => SvgSceneExporter.Export(100, 100, "#fff", "palette-1", [new SceneSvgItem(path, 50, 50, 10, 10, 0)])); }
        finally { File.Delete(path); }
    }

    private static string CreateAsset()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><g transform=\"translate(1 1)\"><path d=\"M0 0 L10 10\" fill=\"#D7CDF5\"/><path d=\"M1 1 L2 2\" fill=\"#181817\"/></g></svg>");
        return path;
    }
}
