using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayTranslator.Services;

public sealed record ValidationIssue(string FileName, string Key, string Reason);

public sealed record ValidationResult(
    int FilesScanned,
    int EntriesScanned,
    List<ValidationIssue> Issues);

public class LanguagePackValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LanguagePackValidator> _logger;

    public LanguagePackValidator(IConfiguration configuration, ILogger<LanguagePackValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(bool fix)
    {
        var outputDirectory = _configuration["Translation:OutputDirectory"] ?? "translations";

        if (!Directory.Exists(outputDirectory))
        {
            return new ValidationResult(0, 0, new List<ValidationIssue>
            {
                new("<none>", "<none>", $"Translation directory '{outputDirectory}' does not exist")
            });
        }

        var files = Directory.GetFiles(outputDirectory, "*.json").OrderBy(path => path).ToList();
        var issues = new List<ValidationIssue>();
        var totalEntries = 0;

        foreach (var filePath in files)
        {
            JObject json;
            var fileChanged = false;

            void ApplyFix(JProperty property, string key, string currentValue, bool sentenceFallback = false)
            {
                if (TranslationValidationRules.IsShortKeyFallbackHotspot(key))
                {
                    property.Remove();
                }
                else
                {
                    var fallbackText = TranslationValidationRules.ResolveSentenceFallback(key);
                    if (sentenceFallback && string.Equals(currentValue, fallbackText, StringComparison.Ordinal))
                    {
                        // Avoid a no-op for sentence fallbacks: remove the entry so runtime falls back cleanly.
                        property.Remove();
                    }
                    else
                    {
                        property.Value = fallbackText;
                    }
                }

                fileChanged = true;
            }

            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                json = JObject.Parse(content);
            }
            catch (JsonReaderException ex)
            {
                var fileName = Path.GetFileName(filePath);
                issues.Add(new ValidationIssue(fileName, "<file>", $"Invalid JSON: {ex.Message}"));
                _logger.LogError(ex, "Invalid JSON in translation file {FileName}", fileName);
                continue;
            }
            catch (IOException ex)
            {
                var fileName = Path.GetFileName(filePath);
                issues.Add(new ValidationIssue(fileName, "<file>", $"I/O error while reading file: {ex.Message}"));
                _logger.LogError(ex, "I/O error while reading translation file {FileName}", fileName);
                continue;
            }

            foreach (var property in json.Properties().ToList())
            {
                var key = property.Name;
                var value = property.Value?.ToString() ?? string.Empty;
                totalEntries++;

                if (TranslationValidationRules.IsSuspiciousMetaResponse(value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Suspicious LLM/meta-response content"));
                    if (fix)
                    {
                        ApplyFix(property, key, value);
                    }
                    continue;
                }

                if (TranslationValidationRules.IsLikelySentenceFallback(key, value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Suspicious source fallback (sentence-like value equals source key)"));
                    if (fix)
                    {
                        ApplyFix(property, key, value, sentenceFallback: true);
                    }
                    continue;
                }

                if (!TranslationValidationRules.HasMatchingPlaceholders(key, value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Placeholder/token mismatch between source key and translation"));
                    if (fix)
                    {
                        ApplyFix(property, key, value);
                    }
                    continue;
                }

                if (TranslationValidationRules.IsShortKeyEnglishFallback(key, value))
                {
                    issues.Add(new ValidationIssue(Path.GetFileName(filePath), key, "Common UI label left untranslated (value equals English key)"));
                    if (fix)
                    {
                        ApplyFix(property, key, value);
                    }
                }
            }

            if (fix && fileChanged)
            {
                await File.WriteAllTextAsync(filePath, json.ToString(Formatting.Indented));
                _logger.LogInformation("Fixed suspicious/mismatched entries in {FileName}", Path.GetFileName(filePath));
            }
        }

        return new ValidationResult(files.Count, totalEntries, issues);
    }
}
