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
    public static readonly Dictionary<string, LanguageInfo> Languages = new()
    {
        ["hi"] = new LanguageInfo("hi", "Hindi", "हिंदी"),
        ["es"] = new LanguageInfo("es-ES", "Spanish", "Español"),
        ["fr"] = new LanguageInfo("fr-FR", "French", "Français"),
        ["de"] = new LanguageInfo("de-DE", "German", "Deutsch"),
        ["it"] = new LanguageInfo("it-IT", "Italian", "Italiano"),
        ["pt"] = new LanguageInfo("pt-BR", "Portuguese (Brazil)", "Português (Brasil)"),
        ["ru"] = new LanguageInfo("ru-RU", "Russian", "Русский"),
        ["ja"] = new LanguageInfo("ja-JP", "Japanese", "日本語"),
        ["ko"] = new LanguageInfo("ko", "Korean", "한국어"),
        ["zh-cn"] = new LanguageInfo("zh-SG", "Chinese (Simplified)", "简体中文"),
        ["zh-tw"] = new  LanguageInfo( "zh-TW", "Chinese (Traditional)", "繁體中文"),
        ["ar"] = new LanguageInfo("ar", "Arabic", "العربية", true),
        ["he"] = new LanguageInfo("he", "Hebrew", "עברית", true),
        ["fa"] = new LanguageInfo("fa", "Persian", "فارسی", true),
        ["tr"] = new LanguageInfo("tr", "Turkish", "Türkçe"),
        ["nl"] = new LanguageInfo("nl-NL", "Dutch", "Nederlands"),
        ["sv"] = new LanguageInfo("sv", "Swedish", "Svenska"),
        ["no"] = new LanguageInfo("no", "Norwegian", "Norsk"),
        ["da"] = new LanguageInfo("da-DK", "Danish", "Dansk"),
        ["fi"] = new LanguageInfo("fi-FI", "Finnish", "Suomi"),
        ["pl"] = new LanguageInfo("pl", "Polish", "Polski"),
        ["cs"] = new LanguageInfo("cs-CZ", "Czech", "Čeština"),
        ["sk"] = new LanguageInfo("sk-SK", "Slovak", "Slovenčina"),
        ["hu"] = new LanguageInfo("hu-HU", "Hungarian", "Magyar"),
        ["ro"] = new LanguageInfo("ro", "Romanian", "Română"),
        ["bg"] = new LanguageInfo("bg-BG", "Bulgarian", "Български"),
        ["hr"] = new LanguageInfo("hr-HR", "Croatian", "Hrvatski"),
        ["sr"] = new LanguageInfo("sr", "Serbian", "Српски"),
        ["sl"] = new LanguageInfo("sl-SI", "Slovenian", "Slovenščina"),
        ["et"] = new LanguageInfo("et", "Estonian", "Eesti"),
        ["lv"] = new LanguageInfo("lv", "Latvian", "Latviešu"),
        ["lt"] = new LanguageInfo("lt", "Lithuanian", "Lietuvių"),
        ["uk"] = new LanguageInfo("uk-UA", "Ukrainian", "Українська"),
        ["be"] = new LanguageInfo("be", "Belarusian", "Беларуская"),
        ["el"] = new LanguageInfo("el-GR", "Greek", "Ελληνικά"),
        ["th"] = new LanguageInfo("th-TH", "Thai", "ไทย"),
        ["vi"] = new LanguageInfo("vi-VN", "Vietnamese", "Tiếng Việt"),
        ["id"] = new LanguageInfo("id", "Indonesian", "Bahasa Indonesia"),
        ["ms"] = new LanguageInfo("ms", "Malay", "Bahasa Melayu"),
        ["tl"] = new LanguageInfo("tl", "Filipino", "Filipino"),
        ["bn"] = new LanguageInfo("bn", "Bengali", "বাংলা"),
        ["ta"] = new LanguageInfo("ta", "Tamil", "தமிழ்"),
        ["te"] = new LanguageInfo("te", "Telugu", "తెలుగు"),
        ["ml"] = new LanguageInfo("ml", "Malayalam", "മലയാളം"),
        ["kn"] = new LanguageInfo("kn", "Kannada", "ಕನ್ನಡ"),
        ["gu"] = new LanguageInfo("gu", "Gujarati", "ગુજરાતી"),
        ["mr"] = new LanguageInfo("mr", "Marathi", "मराठी"),
        ["pa"] = new LanguageInfo("pa", "Punjabi", "ਪੰਜਾਬੀ"),
        ["or"] = new LanguageInfo("or", "Odia", "ଓଡ଼ିଆ"),
        ["as"] = new LanguageInfo("as", "Assamese", "অসমীয়া"),
        ["ur"] = new LanguageInfo("ur", "Urdu", "اردو", true),
        ["ne"] = new LanguageInfo("np-NP", "Nepali", "नेपाली"),
        ["si"] = new LanguageInfo("si", "Sinhala", "සිංහල"),
        ["my"] = new LanguageInfo("my", "Myanmar", "မြန်မာ"),
        ["km"] = new LanguageInfo("km", "Khmer", "ខ្មែរ"),
        ["lo"] = new LanguageInfo("lo", "Lao", "ລາວ"),
        ["ka"] = new LanguageInfo("ka", "Georgian", "ქართული"),
        ["hy"] = new LanguageInfo("hy", "Armenian", "Հայերեն"),
        ["az"] = new LanguageInfo("az", "Azerbaijani", "Azərbaycan"),
        ["kk"] = new LanguageInfo("kk-KZ", "Kazakh", "Қазақша"),
        ["ky"] = new LanguageInfo("ky", "Kyrgyz", "Кыргызча"),
        ["uz"] = new LanguageInfo("uz", "Uzbek", "O'zbek"),
        ["tg"] = new LanguageInfo("tg", "Tajik", "Тоҷикӣ"),
        ["mn"] = new LanguageInfo("mn", "Mongolian", "Монгол"),
        ["am"] = new LanguageInfo("am-ET", "Amharic", "አማርኛ"),
        ["sw"] = new LanguageInfo("sw", "Swahili", "Kiswahili"),
        ["zu"] = new LanguageInfo("zu", "Zulu", "isiZulu"),
        ["af"] = new LanguageInfo("af", "Afrikaans", "Afrikaans"),
        ["is"] = new LanguageInfo("is-IS", "Icelandic", "Íslenska"),
        ["fo"] = new LanguageInfo("fo", "Faroese", "Føroyskt"),
        ["mt"] = new LanguageInfo("mt", "Maltese", "Malti"),
        ["cy"] = new LanguageInfo("cy", "Welsh", "Cymraeg"),
        ["ga"] = new LanguageInfo("ga", "Irish", "Gaeilge"),
        ["gd"] = new LanguageInfo("gd", "Scottish Gaelic", "Gàidhlig"),
        ["eu"] = new LanguageInfo("eu", "Basque", "Euskera"),
        ["ca"] = new LanguageInfo("ca-ES", "Catalan", "Català"),
        ["gl"] = new LanguageInfo("gl", "Galician", "Galego"),
        ["ast"] = new LanguageInfo("ast", "Asturian", "Asturianu"),
        ["br"] = new LanguageInfo("br", "Breton", "Brezhoneg"),
        ["co"] = new LanguageInfo("co", "Corsican", "Corsu"),
        ["sc"] = new LanguageInfo("sc", "Sardinian", "Sardu"),
        ["lb"] = new LanguageInfo("lb", "Luxembourgish", "Lëtzebuergesch"),
        ["rm"] = new LanguageInfo("rm", "Romansh", "Rumantsch"),
        ["fur"] = new LanguageInfo("fur", "Friulian", "Furlan"),
        ["vec"] = new LanguageInfo("vec", "Venetian", "Vèneto"),
        ["nap"] = new LanguageInfo("nap", "Neapolitan", "Napulitano"),
        ["scn"] = new LanguageInfo("scn", "Sicilian", "Sicilianu"),
        ["lmo"] = new LanguageInfo("lmo", "Lombard", "Lumbaart"),
        ["pms"] = new LanguageInfo("pms", "Piedmontese", "Piemontèis"),
        ["lij"] = new LanguageInfo("lij", "Ligurian", "Ligure"),
        ["eml"] = new LanguageInfo("eml", "Emilian-Romagnol", "Emiliàn"),
        ["bs"] = new LanguageInfo("bs-BA", "Bosnian", "Bosanski"),
        ["mk"] = new LanguageInfo("mk", "Macedonian", "Македонски"),
        ["sq"] = new LanguageInfo("sq", "Albanian", "Shqip"),
        ["cnr"] = new LanguageInfo("cnr", "Montenegrin", "Crnogorski")
    };

    public static LanguageInfo? GetLanguageInfo(string code)
    {
        return Languages.TryGetValue(code, out var info) ? info : null;
    }

    public static IEnumerable<LanguageInfo> GetAllLanguages()
    {
        return Languages.Values;
    }
}
