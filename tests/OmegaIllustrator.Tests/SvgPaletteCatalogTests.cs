using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class SvgPaletteCatalogTests
{
    [Fact]
    public void CreateColorMap_uses_semantic_roles_and_leaves_unmapped_source_colors_untouched()
    {
        const string json = """
            [
              { "id":"palette-1", "background":{"bg1":"#D7CDF5","bg2":"#AA9BF0","bg3":"#7864D7"}, "objectOnly":{"accent1":"#F9ECB0","accent2":"#F9A3AA","accent3":"#FFE3E1"}, "neutral":{"outline":"#181817","white":"#FFFFFF"} },
              { "id":"palette-2", "background":{"bg1":"#FDD7B9","bg2":"#FAC896","bg3":"#D27532"}, "objectOnly":{"accent1":"#F9ECB0","accent2":"#80DADA","accent3":"#BEEBEB"}, "neutral":{"outline":"#181817","white":"#FFFFFF"} }
            ]
            """;

        var catalog = SvgPaletteCatalog.FromJson(json);
        var map = catalog.CreateColorMap("palette-2");

        Assert.Equal("#FDD7B9", catalog.BackgroundColor("palette-2"));
        Assert.Equal("#D27532", map["#7864D7"]);
        Assert.Equal("#80DADA", map["#F9A3AA"]);
        Assert.DoesNotContain("#F9ECB0", map.Keys);
        Assert.DoesNotContain("#171409", map.Keys);
    }

    [Fact]
    public void CreateBackgroundObjectColorMap_uses_visible_palette_accents_instead_of_canvas_colors()
    {
        const string json = """
            [
              { "id":"palette-1", "background":{"bg1":"#D7CDF5","bg2":"#AA9BF0","bg3":"#7864D7"}, "objectOnly":{"accent1":"#F9ECB0","accent2":"#F9A3AA","accent3":"#FFE3E1"}, "neutral":{"outline":"#181817","white":"#FFFFFF"} },
              { "id":"palette-3", "background":{"bg1":"#BEEBEB","bg2":"#80DADA","bg3":"#5ABEBE"}, "objectOnly":{"accent1":"#FFD4F2","accent2":"#6D7BFC","accent3":"#B6C6FE"}, "neutral":{"outline":"#181817","white":"#FFFFFF"} }
            ]
            """;

        var map = SvgPaletteCatalog.FromJson(json).CreateBackgroundObjectColorMap("palette-3");

        Assert.Equal("#B6C6FE", map["#D7CDF5"]);
        Assert.Equal("#FFD4F2", map["#AA9BF0"]);
        Assert.Equal("#6D7BFC", map["#7864D7"]);
        Assert.DoesNotContain("#BEEBEB", map.Values);
    }
}
