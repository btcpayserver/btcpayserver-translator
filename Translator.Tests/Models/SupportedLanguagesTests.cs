using BTCPayTranslator.Models;
using Xunit;

namespace BTCPayTranslator.Tests.Models;

public class SupportedLanguagesTests
{
    [Fact]
    public void GetLanguageInfo_ReturnsLanguage_WhenCodeExists()
    {
        var result = SupportedLanguages.GetLanguageInfo("es");

        Assert.NotNull(result);
        Assert.Equal("Spanish", result.Name);
        Assert.Equal("es-ES", result.Code);
    }

    [Fact]
    public void GetLanguageInfo_ReturnsNull_WhenCodeDoesNotExist()
    {
        var result = SupportedLanguages.GetLanguageInfo("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public void GetAllLanguages_ReturnsConsistentAndValidLanguageCatalog()
    {
        var all = SupportedLanguages.GetAllLanguages().ToList();

        Assert.NotEmpty(all);
        
        Assert.Equal(SupportedLanguages.Languages.Count, all.Count);
        
        Assert.Contains(all, l => l.Code == "es-ES" && l.Name == "Spanish");
        Assert.Contains(all, l => l.Code == "fr-FR" && l.Name == "French");
        Assert.Contains(all, l => l.Code == "ar" && l.IsRightToLeft);
        
        Assert.All(all, l => Assert.False(string.IsNullOrWhiteSpace(l.Code)));
        Assert.All(all, l => Assert.False(string.IsNullOrWhiteSpace(l.Name)));
        Assert.All(all, l => Assert.False(string.IsNullOrWhiteSpace(l.NativeName)));
        
        Assert.Equal(all.Count, all.Select(l => l.Code).Distinct().Count());
        Assert.Equal(all.Count, all.Select(l => l.Name).Distinct().Count());
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("FR-fr")]
    [InlineData("fr")]
    public void GetLanguageInfo_AcceptsCanonicalCodeAndAlias_CaseInsensitive(string input)
    {
        var result = SupportedLanguages.GetLanguageInfo(input);

        Assert.NotNull(result);
        Assert.Equal("fr-FR", result.Code);
    }

    [Fact]
    public void GetLanguageInfo_PtAliasResolvesToBrazilianPortuguese()
    {
        Assert.Equal("pt-BR", SupportedLanguages.GetLanguageInfo("pt")!.Code);
    }

    [Fact]
    public void Languages_AreKeyedByTheirOwnCode()
    {
        Assert.All(SupportedLanguages.Languages,
            kvp => Assert.Equal(kvp.Value.Code, kvp.Key));
    }

    [Fact]
    public void EveryAlias_PointsToAnExistingLanguage_AndNeverShadowsACode()
    {
        foreach (var (alias, target) in SupportedLanguages.CompatibilityAliases)
        {
            Assert.True(SupportedLanguages.Languages.ContainsKey(target), $"{alias} -> {target} is not in the catalogue");
            Assert.False(SupportedLanguages.Languages.ContainsKey(alias), $"{alias} is both an alias and a canonical code");
        }
    }
}
