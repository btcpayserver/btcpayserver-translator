using System;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayTranslator.Models;

public record LanguageInfo(
    string Code,
    string Name,
    string NativeName,
    bool IsRightToLeft = false
);

public static class SupportedLanguages
{
    private static readonly LanguageInfo[] Catalogue =
    [
        new LanguageInfo("hi", "Hindi", "हिंदी"),
        new LanguageInfo("es-ES", "Spanish", "Español"),
        new LanguageInfo("fr-FR", "French", "Français"),
        new LanguageInfo("de-DE", "German", "Deutsch"),
        new LanguageInfo("it-IT", "Italian", "Italiano"),
        new LanguageInfo("pt-BR", "Portuguese (Brazil)", "Português (Brasil)"),
        new LanguageInfo("pt-PT", "Portuguese (Portugal)", "Português (Portugal)"),
        new LanguageInfo("ru-RU", "Russian", "Русский"),
        new LanguageInfo("ja-JP", "Japanese", "日本語"),
        new LanguageInfo("ko", "Korean", "한국어"),
        new LanguageInfo("zh-CN", "Chinese (Simplified)", "简体中文"),
        new LanguageInfo("zh-TW", "Chinese (Traditional)", "繁體中文"),
        new LanguageInfo("ar", "Arabic", "العربية", true),
        new LanguageInfo("he", "Hebrew", "עברית", true),
        new LanguageInfo("fa", "Persian", "فارسی", true),
        new LanguageInfo("tr", "Turkish", "Türkçe"),
        new LanguageInfo("nl-NL", "Dutch", "Nederlands"),
        new LanguageInfo("sv", "Swedish", "Svenska"),
        new LanguageInfo("no", "Norwegian", "Norsk"),
        new LanguageInfo("da-DK", "Danish", "Dansk"),
        new LanguageInfo("fi-FI", "Finnish", "Suomi"),
        new LanguageInfo("pl", "Polish", "Polski"),
        new LanguageInfo("cs-CZ", "Czech", "Čeština"),
        new LanguageInfo("sk-SK", "Slovak", "Slovenčina"),
        new LanguageInfo("hu-HU", "Hungarian", "Magyar"),
        new LanguageInfo("ro", "Romanian", "Română"),
        new LanguageInfo("bg-BG", "Bulgarian", "Български"),
        new LanguageInfo("hr-HR", "Croatian", "Hrvatski"),
        new LanguageInfo("sr", "Serbian", "Српски"),
        new LanguageInfo("sl-SI", "Slovenian", "Slovenščina"),
        new LanguageInfo("et", "Estonian", "Eesti"),
        new LanguageInfo("lv", "Latvian", "Latviešu"),
        new LanguageInfo("lt", "Lithuanian", "Lietuvių"),
        new LanguageInfo("uk-UA", "Ukrainian", "Українська"),
        new LanguageInfo("be", "Belarusian", "Беларуская"),
        new LanguageInfo("el-GR", "Greek", "Ελληνικά"),
        new LanguageInfo("th-TH", "Thai", "ไทย"),
        new LanguageInfo("vi-VN", "Vietnamese", "Tiếng Việt"),
        new LanguageInfo("id", "Indonesian", "Bahasa Indonesia"),
        new LanguageInfo("ms", "Malay", "Bahasa Melayu"),
        new LanguageInfo("tl", "Filipino", "Filipino"),
        new LanguageInfo("bn", "Bengali", "বাংলা"),
        new LanguageInfo("ta", "Tamil", "தமிழ்"),
        new LanguageInfo("te", "Telugu", "తెలుగు"),
        new LanguageInfo("ml", "Malayalam", "മലയാളം"),
        new LanguageInfo("kn", "Kannada", "ಕನ್ನಡ"),
        new LanguageInfo("gu", "Gujarati", "ગુજરાતી"),
        new LanguageInfo("mr", "Marathi", "मराठी"),
        new LanguageInfo("pa", "Punjabi", "ਪੰਜਾਬੀ"),
        new LanguageInfo("or", "Odia", "ଓଡ଼ିଆ"),
        new LanguageInfo("as", "Assamese", "অসমীয়া"),
        new LanguageInfo("ur", "Urdu", "اردو", true),
        new LanguageInfo("ne-NP", "Nepali", "नेपाली"),
        new LanguageInfo("si", "Sinhala", "සිංහල"),
        new LanguageInfo("my", "Myanmar", "မြန်မာ"),
        new LanguageInfo("km", "Khmer", "ខ្មែរ"),
        new LanguageInfo("lo", "Lao", "ລາວ"),
        new LanguageInfo("ka", "Georgian", "ქართული"),
        new LanguageInfo("hy", "Armenian", "Հայերեն"),
        new LanguageInfo("az", "Azerbaijani", "Azərbaycan"),
        new LanguageInfo("kk-KZ", "Kazakh", "Қазақша"),
        new LanguageInfo("ky", "Kyrgyz", "Кыргызча"),
        new LanguageInfo("uz", "Uzbek", "O'zbek"),
        new LanguageInfo("tg", "Tajik", "Тоҷикӣ"),
        new LanguageInfo("mn", "Mongolian", "Монгол"),
        new LanguageInfo("am-ET", "Amharic", "አማርኛ"),
        new LanguageInfo("sw", "Swahili", "Kiswahili"),
        new LanguageInfo("zu", "Zulu", "isiZulu"),
        new LanguageInfo("af", "Afrikaans", "Afrikaans"),
        new LanguageInfo("is-IS", "Icelandic", "Íslenska"),
        new LanguageInfo("fo", "Faroese", "Føroyskt"),
        new LanguageInfo("mt", "Maltese", "Malti"),
        new LanguageInfo("cy", "Welsh", "Cymraeg"),
        new LanguageInfo("ga", "Irish", "Gaeilge"),
        new LanguageInfo("gd", "Scottish Gaelic", "Gàidhlig"),
        new LanguageInfo("eu", "Basque", "Euskera"),
        new LanguageInfo("ca-ES", "Catalan", "Català"),
        new LanguageInfo("gl", "Galician", "Galego"),
        new LanguageInfo("ast", "Asturian", "Asturianu"),
        new LanguageInfo("br", "Breton", "Brezhoneg"),
        new LanguageInfo("co", "Corsican", "Corsu"),
        new LanguageInfo("sc", "Sardinian", "Sardu"),
        new LanguageInfo("lb", "Luxembourgish", "Lëtzebuergesch"),
        new LanguageInfo("rm", "Romansh", "Rumantsch"),
        new LanguageInfo("fur", "Friulian", "Furlan"),
        new LanguageInfo("vec", "Venetian", "Vèneto"),
        new LanguageInfo("nap", "Neapolitan", "Napulitano"),
        new LanguageInfo("scn", "Sicilian", "Sicilianu"),
        new LanguageInfo("lmo", "Lombard", "Lumbaart"),
        new LanguageInfo("pms", "Piedmontese", "Piemontèis"),
        new LanguageInfo("lij", "Ligurian", "Ligure"),
        new LanguageInfo("eml", "Emilian-Romagnol", "Emiliàn"),
        new LanguageInfo("bs-BA", "Bosnian", "Bosanski"),
        new LanguageInfo("mk", "Macedonian", "Македонски"),
        new LanguageInfo("sq", "Albanian", "Shqip"),
        new LanguageInfo("cnr", "Montenegrin", "Crnogorski")
    ];

    public static readonly IReadOnlyDictionary<string, LanguageInfo> Languages = Catalogue.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

    public static LanguageInfo? GetLanguageInfo(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Languages.TryGetValue(code, out var info) ? info : null;
    }

    public static IEnumerable<LanguageInfo> GetAllLanguages() => Catalogue;

    /// Codes in the catalogue that share the input's language subtag,
    /// e.g. "fr" -> fr-FR, "zh" -> zh-CN, zh-TW. Used for error messages only.
    public static IEnumerable<string> SuggestCodes(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var language = input.Split('-')[0];
        return Catalogue
            .Where(l => l.Code.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Code);
    }

    public static LanguageInfo? GetLanguageInfoByName(string name) =>
        Catalogue.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
