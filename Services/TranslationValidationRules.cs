using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BTCPayTranslator.Services;

internal static class TranslationValidationRules
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex =
        new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex TokenRegex =
        new(@"[A-Za-z0-9+./_-]+", RegexOptions.Compiled);

    private static readonly Regex ShortEnglishLabelRegex =
        new(@"^[A-Za-z][A-Za-z0-9'() ./-]*$", RegexOptions.Compiled);

    private static readonly Regex[] SuspiciousMetaPatterns =
    {
        // English
        new(@"\bplease provide (the )?english text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bwaiting for the english text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi\s*(?:am|'m) ready to translate\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bready to translate english(?:\s+to\s+[a-z\s\-()]+)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\btranslate english text to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bplease provide the text (?:you(?:'d)? like me to translate|you want me to translate|to translate)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi understand(?:\s+the\s+instructions)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bi don't see any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\byou haven't provided any text\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bprofessional translator for btcpay server\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bas an ai\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    // Localized meta-response patterns: phrases in non-English languages that indicate
    // the LLM replied with "waiting for text" / "ready to translate" instead of translating.
    private static readonly Regex[] LocalizedMetaPatterns =
    {
        // German
        new(@"geben Sie den zu \u00fcbersetzenden", RegexOptions.Compiled),  // "provide the text to translate"
        new(@"Bereit f\u00fcr die \u00dcbersetzung", RegexOptions.Compiled), // "Ready for translation"
        new(@"ich kann .*\u00fcbersetzen", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:\u00fcbersetze|\u00fcbersetzen) .*englisch", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Dutch
        new(@"ik ben (?:een )?(?:professionele )?vertaler", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:geef|geef me) .*engelse tekst", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"klaar om te vertalen", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // French
        new(@"(?:attends|fournir|fourni(?:r|ssez)) le texte \u00e0 traduire", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"ne (?:peux|vois) pas traduire sans texte", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"je peux traduire", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:traduis|traduire) .*anglais", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"je suis (?:un )?traducteur", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Italian
        new(@"fornisci il testo da tradurre", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:pronto|attendo|serve).*tradurre", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"non vedo il testo da tradurre", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"posso tradurre dall'?inglese", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"traduci dall'?inglese in italiano", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"sono un traduttore", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"posso aiutare a tradurre", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Portuguese
        new(@"forne\u00e7a o texto em ingl\u00eas", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"gostaria que eu traduzisse", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"posso traduzir do ingl\u00eas", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"sou (?:um )?tradutor", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Spanish
        new(@"proporcione el texto en ingl\u00e9s", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"necesita ser traducido", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"puedo traducir del ingl\u00e9s", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"traduce del ingl\u00e9s", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"soy (?:un )?traductor", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Thai
        new(@"\u0e01\u0e23\u0e38\u0e13\u0e32\u0e43\u0e2b\u0e49\u0e02\u0e49\u0e2d\u0e04\u0e27\u0e32\u0e21", RegexOptions.Compiled), // "กรุณาให้ข้อความ"
        new(@"\u0e1e\u0e23\u0e49\u0e2d\u0e21\u0e41\u0e1b\u0e25", RegexOptions.Compiled),                                         // "พร้อมแปล"
        new(@"\u0e02\u0e49\u0e2d\u0e04\u0e27\u0e32\u0e21\u0e17\u0e35\u0e48\u0e15\u0e49\u0e2d\u0e07\u0e01\u0e32\u0e23\u0e41\u0e1b\u0e25", RegexOptions.Compiled), // "ข้อความที่ต้องการแปล"
        // Japanese
        new(@"\u7ffb\u8a33\u3059\u308b.*\u30c6\u30ad\u30b9\u30c8\u3092\u63d0\u4f9b", RegexOptions.Compiled), // "翻訳する...テキストを提供"
        // Korean
        new(@"\ubc88\uc5ed\ud560 \uc6d0\ubb38\uc774 \uc81c\uacf5", RegexOptions.Compiled), // "번역할 원문이 제공"
        new(@"\uc601\uc5b4 \ud14d\uc2a4\ud2b8\ub97c \uc81c\uacf5", RegexOptions.Compiled), // "영어 텍스트를 제공"
        // Indonesian
        new(@"berikan teks yang perlu diterjemahkan", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"menunggu teks bahasa Inggris", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Serbian
        new(@"dajte mi tekst za prevod", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Russian
        new(@"\u0442\u0435\u043a\u0441\u0442 \u0434\u043b\u044f \u043f\u0435\u0440\u0435\u0432\u043e\u0434\u0430", RegexOptions.Compiled), // "текст для перевода"
    };

    // Allowlist of short labels that can legitimately appear unchanged in many locales.
    private static readonly HashSet<string> ShortKeyAllowlist = new(StringComparer.Ordinal)
    {
        "Reset",
        "No", "Start", "Source", "Done", "Save", "Send", "Image",
        "API", "URL", "URI", "JSON", "CSV", "PSBT", "BTC", "LNURL", "Tor",
    };

    // Focused hotspot keys that have repeatedly been contaminated with English fallback values.
    private static readonly HashSet<string> ShortKeyHotspotKeys = new(StringComparer.Ordinal)
    {
        "Change Role",
        "Confirm",
        "Continue",
        "Edit",
        "Edit plan",
        "here",
        "Inputs",
        "Invalid role",
        "Modify",
        "New role",
        "Next",
        "Redeliver",
        "Regenerate",
        "Retry",
        "Text",
        "Translations",
        "Update Role",
        "Yes",
        "RESET",
        "Role updated",
        "Role created",
        "Copy Code",
        "More details...",
        "More information...",
    };

    private static readonly HashSet<string> TechnicalAllowTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "apis",
        "btc",
        "lnurl",
        "lnurlp",
        "auth",
        "node",
        "grpc",
        "ssl",
        "cipher",
        "suite",
        "suites",
        "bolt11",
        "bolt12",
        "bip21",
        "json",
        "csv",
        "http",
        "https",
        "url",
        "uri",
        "oauth",
        "webhook",
        "webhooks",
        "docker",
        "github",
        "btcpay",
        "bitcoin",
        "lightning",
        "nostr",
        "nfc",
        "tor",
        "psbt"
    };

    public static bool IsSuspiciousMetaResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return SuspiciousMetaPatterns.Any(pattern => pattern.IsMatch(text))
            || LocalizedMetaPatterns.Any(pattern => pattern.IsMatch(text));
    }

    /// <summary>
    /// Detects short, common UI keys (Confirm, Continue, Yes, etc.) that were
    /// left as English instead of being translated.
    /// </summary>
    public static bool IsShortKeyEnglishFallback(string key, string value)
    {
        if (!string.Equals(key, value, StringComparison.Ordinal))
            return false;

        return IsShortKeyFallbackHotspot(key);
    }

    public static bool IsShortKeyFallbackHotspot(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (ShortKeyAllowlist.Contains(key))
            return false;

        if (PlaceholderRegex.IsMatch(key))
            return false;

        var trimmed = key.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 20)
            return false;

        if (!ShortEnglishLabelRegex.IsMatch(trimmed))
            return false;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is < 1 or > 2)
            return false;

        return ShortKeyHotspotKeys.Contains(trimmed);
    }

    public static string ResolveSentenceFallback(string source)
    {
        return source;
    }

    public static bool HasMatchingPlaceholders(string source, string translation)
    {
        var sourceTokens = ExtractTokenCounts(source);
        var translationTokens = ExtractTokenCounts(translation);

        if (sourceTokens.Count != translationTokens.Count)
            return false;

        foreach (var token in sourceTokens)
        {
            if (!translationTokens.TryGetValue(token.Key, out var count) || count != token.Value)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsLikelySentenceFallback(string source, string translation)
    {
        if (!string.Equals(source, translation, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(source) || source.Length < 20)
            return false;

        var sourceForAnalysis = HtmlTagRegex.Replace(source, " ");
        sourceForAnalysis = PlaceholderRegex.Replace(sourceForAnalysis, " ");
        sourceForAnalysis = WhitespaceRegex.Replace(sourceForAnalysis, " ").Trim();

        if (string.IsNullOrWhiteSpace(sourceForAnalysis) || sourceForAnalysis.Length < 20)
            return false;

        var words = sourceForAnalysis.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 4)
            return false;

        if (!sourceForAnalysis.Any(char.IsLower))
            return false;

        var tokens = TokenRegex.Matches(sourceForAnalysis).Select(match => match.Value).ToList();
        if (tokens.Count == 0)
            return false;

        foreach (var token in tokens)
        {
            if (TechnicalAllowTokens.Contains(token))
                continue;

            if (token.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || ch == '_' || ch == '-'))
                continue;

            return true;
        }

        return false;
    }

    private static Dictionary<string, int> ExtractTokenCounts(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            if (!counts.TryAdd(match.Value, 1))
            {
                counts[match.Value]++;
            }
        }

        return counts;
    }
}