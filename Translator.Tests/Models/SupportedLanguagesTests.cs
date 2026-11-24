using BTCPayTranslator.Models;
using Xunit;

namespace BTCPayTranslator.Tests.Models;

public class SupportedLanguagesTests
{
    [Fact]
    public void GetLanguageInfo_ReturnsLanguage_WhenCodeExists()
    {
        var result = SupportedLanguages.GetLanguageInfo("es-ES");

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
    public void GetLanguageInfo_IsCaseInsensitive(string input)
    {
        var result = SupportedLanguages.GetLanguageInfo(input);

        Assert.NotNull(result);
        Assert.Equal("fr-FR", result.Code);
    }

    [Fact]
    public void GetLanguageInfo_ReturnsNull_ForBareLanguageSubtag()
    {
        Assert.Null(SupportedLanguages.GetLanguageInfo("fr"));
    }

    [Fact]
    public void SuggestCodes_ReturnsCodesSharingTheLanguageSubtag()
    {
        Assert.Equal(new[] { "fr-FR" }, SupportedLanguages.SuggestCodes("fr"));

        var chinese = SupportedLanguages.SuggestCodes("zh").ToList();
        Assert.Contains("zh-CN", chinese);
        Assert.Contains("zh-TW", chinese);

        Assert.Empty(SupportedLanguages.SuggestCodes("xx"));
    }

    [Fact]
    public void Languages_AreKeyedByTheirOwnCode()
    {
        Assert.All(SupportedLanguages.Languages,
            kvp => Assert.Equal(kvp.Value.Code, kvp.Key));
    }
}