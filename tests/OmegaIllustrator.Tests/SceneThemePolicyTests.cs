using OmegaIllustrator.Core.Suggestions;

namespace OmegaIllustrator.Tests;

public sealed class SceneThemePolicyTests
{
    [Fact]
    public void Financial_project_in_a_quiet_setting_keeps_the_financial_subject_primary()
    {
        var terms = new[] { "продуктивной", "работа", "финансовым", "проектом", "тихой", "обстановке" };

        Assert.True(SceneThemePolicy.IsFinancialIntent(terms));
        Assert.Equal("folder", SceneThemePolicy.PreferredFinancialFocus(terms, ["folder", "wallet", "coin", "pen", "lamp"]));
        Assert.False(SceneThemePolicy.AllowsInFinancialScene("headphones", terms));
        Assert.False(SceneThemePolicy.AllowsInFinancialScene("flowers", terms));
        Assert.False(SceneThemePolicy.AllowsInFinancialScene("book", terms));
        Assert.All(new[] { "folder", "wallet", "coin", "pen", "lamp" }, concept =>
            Assert.True(SceneThemePolicy.AllowsInFinancialScene(concept, terms)));
    }

    [Fact]
    public void Explicitly_requested_support_object_can_still_join_a_financial_scene()
    {
        var terms = new[] { "финансовый", "проект", "наушники" };

        Assert.True(SceneThemePolicy.AllowsInFinancialScene("headphones", terms));
    }

    [Fact]
    public void Financial_learning_scene_can_connect_a_wallet_to_its_book()
    {
        Assert.True(SceneThemePolicy.AllowsRelationship("wallet", "book", "beside"));
    }

}
