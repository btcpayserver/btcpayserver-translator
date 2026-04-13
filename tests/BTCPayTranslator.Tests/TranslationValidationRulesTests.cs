using BTCPayTranslator.Services;

namespace BTCPayTranslator.Tests;

public class TranslationValidationRulesTests
{
    // ──────────────────────────────────────────────────────────────────────
    //  IsSuspiciousMetaResponse
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Please provide the English text")]
    [InlineData("please provide english text")]
    [InlineData("I am ready to translate")]
    [InlineData("I'm ready to translate")]
    [InlineData("Ready to translate English to French")]
    [InlineData("Ready to translate English to Brazilian Portuguese (pt-BR)")]
    [InlineData("Translate English text to Spanish")]
    [InlineData("Waiting for the English text")]
    [InlineData("Please provide the text you'd like me to translate")]
    [InlineData("Please provide the text you want me to translate")]
    [InlineData("Please provide the text to translate")]
    [InlineData("I understand the instructions")]
    [InlineData("I understand")]
    [InlineData("I don't see any text")]
    [InlineData("You haven't provided any text")]
    [InlineData("I am a professional translator for BTCPay Server")]
    [InlineData("As an AI, I can help with translations")]
    public void IsSuspiciousMetaResponse_Detects_MetaPatterns(string text)
    {
        Assert.True(TranslationValidationRules.IsSuspiciousMetaResponse(text));
    }

    [Theory]
    [InlineData("Paramètres")]
    [InlineData("Créer une facture")]
    [InlineData("Connexion au nœud Lightning réussie.")]
    [InlineData("Save")]
    [InlineData("Cancel")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSuspiciousMetaResponse_Passes_NormalTranslations(string text)
    {
        Assert.False(TranslationValidationRules.IsSuspiciousMetaResponse(text));
    }

    [Fact]
    public void IsSuspiciousMetaResponse_Null_ReturnsFalse()
    {
        Assert.False(TranslationValidationRules.IsSuspiciousMetaResponse(null!));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  HasMatchingPlaceholders
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello {0}", "Bonjour {0}")]
    [InlineData("{0} items for {1}", "{0} articles pour {1}")]
    [InlineData("{OrderId} confirmed", "{OrderId} confirmé")]
    [InlineData("No placeholders here", "Pas de paramètres ici")]
    [InlineData("", "")]
    [InlineData("Plain text", "Texte simple")]
    public void HasMatchingPlaceholders_Matching_ReturnsTrue(string source, string translation)
    {
        Assert.True(TranslationValidationRules.HasMatchingPlaceholders(source, translation));
    }

    [Theory]
    [InlineData("{0} items", "articles")]                       // placeholder dropped
    [InlineData("{0} for {1}", "{0} pour")]                     // one placeholder dropped
    [InlineData("No placeholder", "Pas de {0} paramètre")]     // placeholder introduced
    [InlineData("{OrderId}", "{InvoiceId}")]                    // different placeholder name
    [InlineData("{0} {0}", "{0}")]                              // duplicate count mismatch
    public void HasMatchingPlaceholders_Mismatching_ReturnsFalse(string source, string translation)
    {
        Assert.False(TranslationValidationRules.HasMatchingPlaceholders(source, translation));
    }

    [Fact]
    public void HasMatchingPlaceholders_MultipleSamePlaceholder_MatchesCounts()
    {
        // Source has {0} twice, translation must also have it twice
        Assert.True(TranslationValidationRules.HasMatchingPlaceholders(
            "{0} and {0} again", "{0} et {0} encore"));

        Assert.False(TranslationValidationRules.HasMatchingPlaceholders(
            "{0} and {0} again", "{0} et encore"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  IsLikelySentenceFallback
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("This is a complete English sentence that was not translated")]
    [InlineData("The invoice has been paid successfully and confirmed")]
    [InlineData("Allow anyone to create invoice")]
    public void IsLikelySentenceFallback_DetectsUntranslatedSentences(string text)
    {
        // source == translation for sentence-like strings → flagged
        Assert.True(TranslationValidationRules.IsLikelySentenceFallback(text, text));
    }

    [Theory]
    [InlineData("Save", "Save")]                                         // too short (< 20 chars)
    [InlineData("API", "API")]                                           // too short, all uppercase
    [InlineData("BTC", "BTC")]                                           // technical token, too short
    [InlineData("Cancel", "Annuler")]                                    // different → not a fallback
    [InlineData("Hello world test", "Hello world test")]                 // < 20 chars
    [InlineData("NO LOWER CASE LETTERS IN THIS", "NO LOWER CASE LETTERS IN THIS")] // no lowercase
    public void IsLikelySentenceFallback_IgnoresShortAndTechnicalStrings(string source, string translation)
    {
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback(source, translation));
    }

    [Fact]
    public void IsLikelySentenceFallback_DetectsSentenceWithPlaceholder()
    {
        // Placeholder-bearing English sentences should still be analyzed and flagged.
        var text = "The payment {OrderId} was confirmed successfully";
        Assert.True(TranslationValidationRules.IsLikelySentenceFallback(text, text));
    }

    [Fact]
    public void IsLikelySentenceFallback_IgnoresMarkupFragmentsWithPlaceholder()
    {
        // Markup-heavy snippets with a short remaining phrase should not be treated as sentence fallbacks.
        var text = "<span class=\"currency\">{0}</span> on-chain";
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback(text, text));
    }

    [Fact]
    public void IsLikelySentenceFallback_IgnoresTechnicalOnlyStrings()
    {
        // A string composed entirely of allowed technical tokens + uppercase should not be flagged
        var text = "BTC LNURL BOLT11 GRPC SSL";
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback(text, text));
    }

    [Fact]
    public void IsLikelySentenceFallback_DifferentSourceAndTranslation_ReturnsFalse()
    {
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback(
            "Allow anyone to create invoice",
            "Autoriser tout le monde à créer des factures"));
    }

    [Fact]
    public void IsLikelySentenceFallback_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback("", ""));
        Assert.False(TranslationValidationRules.IsLikelySentenceFallback("   ", "   "));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Edge-case / integration-style guardrail tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void MetaResponse_CaseInsensitive()
    {
        Assert.True(TranslationValidationRules.IsSuspiciousMetaResponse(
            "I AM READY TO TRANSLATE"));
        Assert.True(TranslationValidationRules.IsSuspiciousMetaResponse(
            "PLEASE PROVIDE THE ENGLISH TEXT"));
    }

    [Fact]
    public void MetaResponse_EmbeddedInLongerString()
    {
        // Even embedded inside a larger string, the pattern should match
        Assert.True(TranslationValidationRules.IsSuspiciousMetaResponse(
            "Sure! I am ready to translate your text. Please send it."));
    }

    [Fact]
    public void Placeholders_HtmlEntitiesPreserved()
    {
        // Ensure HTML tags don't interfere (no false positives from angle brackets)
        Assert.True(TranslationValidationRules.HasMatchingPlaceholders(
            "<strong>{0}</strong> items",
            "<strong>{0}</strong> articles"));
    }

    [Fact]
    public void Placeholders_ComplexMixedContent()
    {
        var source = "Available placeholders: <code>{StoreName} {ItemDescription} {OrderId}</code>";
        var translation = "Paramètres disponibles : <code>{StoreName} {ItemDescription} {OrderId}</code>";
        Assert.True(TranslationValidationRules.HasMatchingPlaceholders(source, translation));
    }

    [Fact]
    public void Placeholders_ComplexMixedContent_Mismatch()
    {
        var source = "Available placeholders: <code>{StoreName} {ItemDescription} {OrderId}</code>";
        var translation = "Paramètres disponibles : <code>{StoreName} {OrderId}</code>";
        Assert.False(TranslationValidationRules.HasMatchingPlaceholders(source, translation));
    }

    [Fact]
    public void SentenceFallback_RealWorldContaminatedEntry()
    {
        // Simulates a real contamination case: a full English sentence that slipped through untranslated
        var text = "Check releases on GitHub and notify when new BTCPay Server version is available";
        Assert.True(TranslationValidationRules.IsLikelySentenceFallback(text, text));
    }
}
