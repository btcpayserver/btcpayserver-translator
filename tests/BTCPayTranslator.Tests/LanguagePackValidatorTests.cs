using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace BTCPayTranslator.Tests;

/// <summary>
/// Minimal IConfiguration backed by a dictionary. Avoids a dependency on
/// the Microsoft.Extensions.Configuration.Memory NuGet package.
/// </summary>
internal sealed class DictionaryConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _data;

    public DictionaryConfiguration(Dictionary<string, string?> data) => _data = data;

    public string? this[string key]
    {
        get => _data.GetValueOrDefault(key);
        set => _data[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];
    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
    public IConfigurationSection GetSection(string key) =>
        throw new NotSupportedException("Not needed for tests");
}

public class LanguagePackValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LanguagePackValidator _validator;

    public LanguagePackValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"validator_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var config = new DictionaryConfiguration(new Dictionary<string, string?>
        {
            ["Translation:OutputDirectory"] = _tempDir
        });

        _validator = new LanguagePackValidator(
            config,
            NullLogger<LanguagePackValidator>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Missing directory handling
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_MissingDirectory_ReturnsIssueWithMessage()
    {
        var config = new DictionaryConfiguration(new Dictionary<string, string?>
        {
            ["Translation:OutputDirectory"] = Path.Combine(_tempDir, "missing_subdir_" + Guid.NewGuid().ToString("N"))
        });

        var validator = new LanguagePackValidator(
            config,
            NullLogger<LanguagePackValidator>.Instance);

        var result = await validator.ValidateAsync(fix: false);

        Assert.Equal(0, result.FilesScanned);
        Assert.Single(result.Issues);
        Assert.Contains("does not exist", result.Issues[0].Reason);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Empty directory
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_EmptyDirectory_ReturnsZeroFilesZeroIssues()
    {
        var result = await _validator.ValidateAsync(fix: false);

        Assert.Equal(0, result.FilesScanned);
        Assert.Equal(0, result.EntriesScanned);
        Assert.Empty(result.Issues);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Invalid JSON reporting
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReportsErrorAndContinues()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "bad.json"),
            "{ NOT VALID JSON }");

        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "good.json"),
            """{ "Save": "Speichern" }""");

        var result = await _validator.ValidateAsync(fix: false);

        Assert.Equal(2, result.FilesScanned);
        Assert.Equal(1, result.EntriesScanned);

        var jsonIssue = Assert.Single(result.Issues);
        Assert.Equal("bad.json", jsonIssue.FileName);
        Assert.Contains("Invalid JSON", jsonIssue.Reason);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Meta-response detection in pack files
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_DetectsMetaResponse()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "french.json"),
            """
            {
              "Save": "Please provide the English text",
              "Cancel": "Annuler"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(2, result.EntriesScanned);

        var metaIssue = Assert.Single(result.Issues);
        Assert.Equal("Save", metaIssue.Key);
        Assert.Contains("meta-response", metaIssue.Reason);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Placeholder mismatch detection
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_DetectsPlaceholderMismatch()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "spanish.json"),
            """
            {
              "Hello {0}": "Hola",
              "Goodbye {0}": "Adiós {0}"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        var mismatchIssue = Assert.Single(result.Issues);
        Assert.Equal("Hello {0}", mismatchIssue.Key);
        Assert.Contains("Placeholder", mismatchIssue.Reason);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Sentence-like fallback detection
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_DetectsSentenceLikeFallback()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "korean.json"),
            """
            {
              "Allow anyone to create invoice": "Allow anyone to create invoice",
              "Save": "저장"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        var fallbackIssue = Assert.Single(result.Issues);
        Assert.Equal("Allow anyone to create invoice", fallbackIssue.Key);
        Assert.Contains("fallback", fallbackIssue.Reason);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  --fix mode: rewrite + clean second validation pass
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FixMode_RewritesSuspiciousEntries()
    {
        var filePath = Path.Combine(_tempDir, "german.json");
        await File.WriteAllTextAsync(filePath,
            """
            {
              "Save": "I am ready to translate",
              "Cancel": "Abbrechen"
            }
            """);

        var firstPass = await _validator.ValidateAsync(fix: true);

        Assert.Single(firstPass.Issues);
        Assert.Equal("Save", firstPass.Issues[0].Key);

        // Verify the file was rewritten: the suspicious value should now be the key
        var rewrittenContent = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"Save\": \"Save\"", rewrittenContent);
        Assert.Contains("\"Cancel\": \"Abbrechen\"", rewrittenContent);
    }

    [Fact]
    public async Task ValidateAsync_FixMode_SecondPassReturnsClearForFixedEntries()
    {
        var filePath = Path.Combine(_tempDir, "german.json");
        // Short key ("Save") replaced with itself won't trigger IsLikelySentenceFallback
        // because it's < 20 chars. This is the expected behavior.
        await File.WriteAllTextAsync(filePath,
            """
            {
              "Save": "I am ready to translate"
            }
            """);

        // Fix pass rewrites "Save" value to "Save" (the key)
        await _validator.ValidateAsync(fix: true);

        // Second pass: "Save": "Save" should not flag (< 20 chars → not a sentence fallback)
        var secondPass = await _validator.ValidateAsync(fix: false);

        Assert.Empty(secondPass.Issues);
    }

    [Fact]
    public async Task ValidateAsync_FixMode_PlaceholderMismatchReplacedWithKey()
    {
        var filePath = Path.Combine(_tempDir, "italian.json");
        await File.WriteAllTextAsync(filePath,
            """
            {
              "Hello {0} and {1}": "Ciao",
              "Goodbye": "Arrivederci"
            }
            """);

        await _validator.ValidateAsync(fix: true);

        var rewrittenContent = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"Hello {0} and {1}\": \"Hello {0} and {1}\"", rewrittenContent);
        Assert.Contains("\"Goodbye\": \"Arrivederci\"", rewrittenContent);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Clean file passes validation with zero issues
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_CleanFile_ZeroIssues()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "french.json"),
            """
            {
              "Save": "Enregistrer",
              "Cancel": "Annuler",
              "Hello {0}": "Bonjour {0}",
              "API": "API"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(4, result.EntriesScanned);
        Assert.Empty(result.Issues);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Multiple issues across multiple files
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_MultipleFiles_AggregatesIssuesAcrossAll()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "french.json"),
            """
            {
              "Save": "Please provide the English text"
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "german.json"),
            """
            {
              "Hello {0}": "Hallo"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        Assert.Equal(2, result.FilesScanned);
        Assert.Equal(2, result.EntriesScanned);
        Assert.Equal(2, result.Issues.Count);

        Assert.Contains(result.Issues, i => i.FileName == "french.json" && i.Reason.Contains("meta-response"));
        Assert.Contains(result.Issues, i => i.FileName == "german.json" && i.Reason.Contains("Placeholder"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Localized (non-English) meta-response detection
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_DetectsLocalizedMetaResponse()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "italian.json"),
            """
            {
              "Confirm": "Per favore fornisci il testo da tradurre.",
              "Cancel": "Annulla"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        var metaIssue = Assert.Single(result.Issues);
        Assert.Equal("Confirm", metaIssue.Key);
        Assert.Contains("meta-response", metaIssue.Reason);
    }

    [Theory]
    [InlineData("กรุณาให้ข้อความที่ต้องการแปล")]      // Thai
    [InlineData("翻訳する英語のテキストを提供してください")] // Japanese
    [InlineData("Molim vas dajte mi tekst za prevod")] // Serbian
    [InlineData("menunggu teks bahasa Inggris")]        // Indonesian
    [InlineData("geben Sie den zu übersetzenden")]      // German
    public void IsSuspiciousMetaResponse_DetectsLocalizedVariants(string text)
    {
        Assert.True(TranslationValidationRules.IsSuspiciousMetaResponse(text));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Short-key English fallback detection
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Change Role", "Change Role", true)]
    [InlineData("Confirm", "Confirm", true)]
    [InlineData("Continue", "Continue", true)]
    [InlineData("Edit", "Edit", true)]
    [InlineData("Update Role", "Update Role", true)]
    [InlineData("Yes", "Yes", true)]
    [InlineData("Role created", "Role created", true)]
    [InlineData("Confirm", "Bevestigen", false)]   // translated = no issue
    [InlineData("PSBT", "PSBT", false)]             // technical term = not in denylist
    [InlineData("Save", "Save", false)]             // not in the denylist
    [InlineData("No", "No", false)]                 // cognate, same in many languages
    [InlineData("Start", "Start", false)]           // cognate, same in many languages
    [InlineData("Source", "Source", false)]          // cognate, same in French
    public void IsShortKeyEnglishFallback_DetectsHotspotKeys(string key, string value, bool expected)
    {
        Assert.Equal(expected, TranslationValidationRules.IsShortKeyEnglishFallback(key, value));
    }

    [Fact]
    public async Task ValidateAsync_DetectsShortKeyEnglishFallback()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "thai.json"),
            """
            {
              "Confirm": "Confirm",
              "Cancel": "ยกเลิก"
            }
            """);

        var result = await _validator.ValidateAsync(fix: false);

        var fallbackIssue = Assert.Single(result.Issues);
        Assert.Equal("Confirm", fallbackIssue.Key);
        Assert.Contains("untranslated", fallbackIssue.Reason);
    }
}
