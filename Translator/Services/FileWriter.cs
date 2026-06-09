using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayTranslator.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayTranslator.Services;

public class FileWriter
{
    private readonly ILogger<FileWriter> _logger;
    private readonly JsonSerializerSettings _jsonSettings;

    public FileWriter(ILogger<FileWriter> logger)
    {
        _logger = logger;
        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
        };
    }

    public async Task WriteCheckoutTranslationFileAsync(
        string outputPath,
        LanguageInfo languageInfo,
        Dictionary<string, string> translations)
    {
        try
        {
            // Create the translation file structure
            var translationFile = new JObject
            {
                ["NOTICE_WARN"] = "THIS CODE HAS BEEN AUTOMATICALLY GENERATED FROM TRANSIFEX, IF YOU WISH TO HELP TRANSLATION COME ON THE SLACK https://chat.btcpayserver.org/ TO REQUEST PERMISSION TO https://www.transifex.com/btcpayserver/btcpayserver/",
                ["code"] = languageInfo.Code,
                ["currentLanguage"] = languageInfo.NativeName
            };

            // Add all translations
            foreach (var translation in translations.OrderBy(t => t.Key))
            {
                translationFile[translation.Key] = translation.Value;
            }

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory: {Directory}", directory);
            }

            // Write the file
            var json = translationFile.ToString(Formatting.Indented);
            await File.WriteAllTextAsync(outputPath, json);

            _logger.LogInformation("Successfully wrote {Count} translations to {OutputPath}", 
                translations.Count, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing translation file to {OutputPath}", outputPath);
            throw;
        }
    }

    public async Task WriteBackendTranslationFileAsync(
        string outputPath,
        LanguageInfo languageInfo,
        Dictionary<string, string> translations)
    {
        try
        {
            // Create the backend translation file structure (simple JSON)
            var translationFile = new JObject();

            // Add all translations
            foreach (var translation in translations.OrderBy(t => t.Key))
            {
                translationFile[translation.Key] = translation.Value;
            }

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory: {Directory}", directory);
            }

            // Write the file
            var json = translationFile.ToString(Formatting.Indented);
            await File.WriteAllTextAsync(outputPath, json);

            _logger.LogInformation("Successfully wrote {Count} backend translations to {OutputPath}", 
                translations.Count, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing backend translation file to {OutputPath}", outputPath);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> LoadExistingBackendTranslationsAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string>();
            }

            var content = await File.ReadAllTextAsync(filePath);
            var jsonObject = JObject.Parse(content);
            var translations = new Dictionary<string, string>();

            foreach (var property in jsonObject.Properties())
            {
                var value = property.Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(value))
                {
                    translations[property.Name] = value;
                }
            }

            _logger.LogInformation("Loaded {Count} existing translations from {FilePath}", 
                translations.Count, filePath);
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading existing translations from {FilePath}", filePath);
            return new Dictionary<string, string>();
        }
    }

    public async Task WriteSummaryReportAsync(
        string outputPath,
        string language,
        BatchTranslationResponse response,
        Dictionary<string, string> finalTranslations)
    {
        try
        {
            var report = new
            {
                Language = language,
                Timestamp = DateTime.UtcNow,
                Translation = new
                {
                    TotalItems = response.Results.Count,
                    SuccessfulTranslations = response.SuccessCount,
                    FailedTranslations = response.FailureCount,
                    Duration = response.Duration.ToString(@"hh\:mm\:ss"),
                    SuccessRate = $"{(double)response.SuccessCount / response.Results.Count * 100:F1}%"
                },
                Output = new
                {
                    FinalTranslationCount = finalTranslations.Count,
                    OutputFile = outputPath
                },
                Failures = response.Results
                    .Where(r => !r.Success)
                    .Select(r => new { r.Key, r.Error })
                    .ToArray()
            };

            var reportPath = Path.ChangeExtension(outputPath, ".report.json");
            var json = JsonConvert.SerializeObject(report, _jsonSettings);
            await File.WriteAllTextAsync(reportPath, json);

            _logger.LogInformation("Translation summary report written to {ReportPath}", reportPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing summary report");
        }
    }

    // Same comparer the writer uses for ordering (OrderBy(t => t.Key) -> Comparer<string>.Default).
    private static readonly IComparer<string> WriterKeyComparer = Comparer<string>.Default;

    private static readonly Regex TrailingCommaRegex = new(@",(\s*)$", RegexOptions.Compiled);

    /// <summary>
    /// Inserts the keys from <paramref name="source"/> that are missing from <paramref name="filePath"/>
    /// as new JSON entries (value = the source value), preserving every existing line byte-for-byte.
    /// This is the insert-only, no-AI "refresh" path. Returns the number of keys added (0 if the file is
    /// missing, already up to date, or could not be rewritten safely).
    /// </summary>
    public async Task<int> InsertMissingKeysAsync(string filePath, IReadOnlyDictionary<string, string> source)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Translation file not found, skipping: {FilePath}", filePath);
            return 0;
        }

        var raw = await File.ReadAllTextAsync(filePath);

        List<string> existingKeys;
        try
        {
            existingKeys = JObject.Parse(raw).Properties().Select(p => p.Name).ToList();
        }
        catch (JsonReaderException ex)
        {
            _logger.LogError(ex, "Invalid JSON, skipping: {FilePath}", filePath);
            return 0;
        }

        var rebuilt = BuildRebuilt(raw, existingKeys, source, out var added);
        if (rebuilt is null)
        {
            _logger.LogWarning("Could not safely insert keys (structure mismatch), skipping: {FilePath}", filePath);
            return 0;
        }

        if (added == 0)
            return 0;

        // Validation gate: never write a corrupt or lossy file.
        JObject check;
        try
        {
            check = JObject.Parse(rebuilt);
        }
        catch (JsonReaderException ex)
        {
            _logger.LogError(ex, "Refusing to write {FilePath}: rebuilt content is not valid JSON", filePath);
            return 0;
        }

        var finalCount = check.Properties().Count();
        var allSourcePresent = source.Keys.All(k => check.ContainsKey(k));
        if (finalCount != existingKeys.Count + added || !allSourcePresent)
        {
            _logger.LogError(
                "Refusing to write {FilePath}: validation failed (final={Final}, expected={Expected}, allSourcePresent={AllPresent})",
                filePath, finalCount, existingKeys.Count + added, allSourcePresent);
            return 0;
        }

        await File.WriteAllTextAsync(filePath, rebuilt, new UTF8Encoding(false));
        _logger.LogInformation("Inserted {Added} new key(s) into {FilePath}", added, filePath);
        return added;
    }

    // Pure (no IO). Returns the rebuilt file text with missing keys spliced in, or null if the file
    // structure is not what we expect (in which case the caller skips it without writing).
    private static string? BuildRebuilt(
        string raw,
        IReadOnlyList<string> existingKeys,
        IReadOnlyDictionary<string, string> source,
        out int addedCount)
    {
        addedCount = 0;

        var newline = raw.Contains("\r\n") ? "\r\n" : "\n";
        var parts = raw.Split(new[] { newline }, StringSplitOptions.None).ToList();

        var trailingNewline = parts.Count > 0 && parts[^1].Length == 0;
        if (trailingNewline)
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count < 2 || parts[0].Trim() != "{" || parts[^1].Trim() != "}")
            return null;

        var entryLines = parts.GetRange(1, parts.Count - 2);
        if (entryLines.Count != existingKeys.Count)
            return null;

        var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        var missing = source.Keys.Where(k => !existingSet.Contains(k)).ToList();
        if (missing.Count == 0)
            return raw;

        // For each missing key, find the existing key it should follow in canonical (writer) order.
        const string topAnchor = " TOP";
        var perAnchor = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var anchor = topAnchor;
        foreach (var key in existingKeys.Concat(missing).OrderBy(k => k, WriterKeyComparer))
        {
            if (existingSet.Contains(key))
            {
                anchor = key;
                continue;
            }

            if (!perAnchor.TryGetValue(anchor, out var list))
                perAnchor[anchor] = list = new List<string>();
            list.Add(key);
        }

        var units = new List<string>(entryLines.Count + missing.Count);
        if (perAnchor.TryGetValue(topAnchor, out var topKeys))
            units.AddRange(topKeys.Select(k => RenderEntryLine(k, source[k])));
        for (var i = 0; i < existingKeys.Count; i++)
        {
            units.Add(entryLines[i]);
            if (perAnchor.TryGetValue(existingKeys[i], out var afterKeys))
                units.AddRange(afterKeys.Select(k => RenderEntryLine(k, source[k])));
        }

        for (var i = 0; i < units.Count; i++)
            units[i] = SetTrailingComma(units[i], needComma: i != units.Count - 1);

        addedCount = missing.Count;
        return parts[0] + newline + string.Join(newline, units) + newline + parts[^1] + (trailingNewline ? newline : "");
    }

    // Adds/removes a single trailing comma only when the required state differs, preserving any
    // trailing whitespace (so existing lines stay byte-identical unless their comma must change).
    private static string SetTrailingComma(string line, bool needComma)
    {
        var hasComma = TrailingCommaRegex.IsMatch(line);
        if (needComma == hasComma)
            return line;
        return needComma ? line + "," : TrailingCommaRegex.Replace(line, "$1");
    }

    // Renders a new entry line with the same indentation and (default) escaping as the existing files.
    // Newtonsoft's default StringEscapeHandling escapes only " \ and control chars - it leaves
    // < > & and non-ASCII raw, which matches how these files are written. Do NOT use _jsonSettings here
    // (its EscapeNonAscii would corrupt non-ASCII placeholders).
    private static string RenderEntryLine(string key, string value) =>
        "  " + new JValue(key).ToString(Formatting.None) + ": " + new JValue(value).ToString(Formatting.None);
}
